import { test, expect, type Page } from "@playwright/test";

async function passwordLogin(
  page: Page,
  username = "alice",
  password = "AliceDemo!123",
) {
  await page.getByLabel("Username", { exact: true }).fill(username);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in with password" }).click();
  await expect(page).toHaveURL(/\/profile$/);
}

test("password validation, invalid credentials, profile guard, and reload", async ({
  page,
}) => {
  await page.goto("/profile");
  await expect(page).toHaveURL(/\/login$/);
  await expect(
    page.getByRole("button", { name: "Sign in with a passkey" }),
  ).toHaveCount(0);
  await expect(
    page.getByText("or use your device", { exact: true }),
  ).toHaveCount(0);
  await page.setViewportSize({ width: 1366, height: 900 });
  await page.screenshot({
    path: test.info().outputPath("login-desktop.png"),
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: test.info().outputPath("login-mobile.png"),
    fullPage: true,
  });
  await page.setViewportSize({ width: 1366, height: 900 });
  await page.getByRole("button", { name: "Sign in with password" }).click();
  await expect(
    page.getByText("Enter your username.", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Enter your password.", { exact: true }),
  ).toBeVisible();
  await page.getByLabel("Username", { exact: true }).fill("alice");
  await page.getByLabel("Password", { exact: true }).fill("incorrect");
  await page.getByRole("button", { name: "Sign in with password" }).click();
  await expect(page.getByRole("alert")).toContainText(
    "username or password is incorrect",
  );
  await passwordLogin(page);
  await expect(
    page.getByRole("heading", { name: "User Profile Settings" }),
  ).toBeVisible();
  await page.reload();
  await expect(
    page.getByText("Signed in with password", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test("real WebAuthn registration and login, replay rejection, account isolation, removal and password fallback", async ({
  page,
  context,
}) => {
  const cdp = await context.newCDPSession(page);
  await cdp.send("WebAuthn.enable");
  await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
  await page.goto("/login");
  await expect(
    page.getByRole("button", { name: "Sign in with a passkey" }),
  ).toHaveCount(0);
  await passwordLogin(page);
  await page.getByRole("button", { name: "+ Add a passkey" }).click();
  await expect(
    page.getByText("Enter a friendly name for this passkey."),
  ).toBeVisible();
  await page.getByLabel("Give your passkey a name").fill("Test laptop");
  const registration = page.waitForRequest((r) =>
    r.url().endsWith("/registration/verify"),
  );
  await page.getByRole("button", { name: "+ Add a passkey" }).click();
  const registrationBody = (await registration).postDataJSON();
  await expect(page.getByRole("status")).toContainText("Passkey added");
  await expect(page.getByText("Test laptop", { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 1366, height: 900 });
  await page.screenshot({
    path: test.info().outputPath("profile-desktop.png"),
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({
    path: test.info().outputPath("profile-mobile.png"),
    fullPage: true,
  });
  await page.setViewportSize({ width: 1366, height: 900 });

  const replay = await page.evaluate(async (body) => {
    const api = "https://localhost:7043/api";
    const { token } = await (
      await fetch(api + "/auth/csrf", { credentials: "include" })
    ).json();
    const response = await fetch(api + "/passkeys/registration/verify", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
      body: JSON.stringify(body),
    });
    return {
      status: response.status,
      message: (await response.json()).message,
    };
  }, registrationBody);
  expect(replay.status).toBe(400);
  expect(replay.message).toContain("already used");

  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Sign in with a passkey" }),
  ).toBeVisible();
  await passwordLogin(page, "bob", "BobDemo!123");
  await expect(
    page.getByText("No passkeys yet", { exact: true }),
  ).toBeVisible();
  const isolated = await page.evaluate(async (id: string) => {
    const api = "https://localhost:7043/api";
    const { token } = await (
      await fetch(api + "/auth/csrf", { credentials: "include" })
    ).json();
    return (
      await fetch(api + "/passkeys/" + id, {
        method: "DELETE",
        credentials: "include",
        headers: { "X-CSRF-TOKEN": token },
      })
    ).status;
  }, registrationBody.credential.id);
  expect(isolated).toBe(404);
  await page.getByRole("button", { name: "Sign out", exact: true }).click();

  // Exercise the verifier with a real assertion whose challenge, origin, or signature is altered.
  for (const tamper of ["challenge", "origin", "signature"]) {
    let originalAssertion: unknown;
    await page.route(
      "**/api/passkeys/authentication/verify",
      async (route) => {
        const body = route.request().postDataJSON();
        originalAssertion = structuredClone(body);
        if (tamper === "signature") {
          const signature = Buffer.from(
            body.credential.response.signature,
            "base64url",
          );
          signature[signature.length - 1] ^= 1;
          body.credential.response.signature = signature.toString("base64url");
        } else {
          const data = JSON.parse(
            Buffer.from(
              body.credential.response.clientDataJSON,
              "base64url",
            ).toString(),
          );
          if (tamper === "origin") data.origin = "https://untrusted.example";
          else data.challenge = Buffer.alloc(32, 42).toString("base64url");
          body.credential.response.clientDataJSON = Buffer.from(
            JSON.stringify(data),
          ).toString("base64url");
        }
        await route.continue({ postData: JSON.stringify(body) });
      },
      { times: 1 },
    );
    const failedVerification = page.waitForResponse((response) =>
      response.url().endsWith("/authentication/verify"),
    );
    await page.getByRole("button", { name: "Sign in with a passkey" }).click();
    expect((await failedVerification).status()).toBe(400);
    await expect(page.getByRole("alert")).toContainText(
      "could not be verified",
    );
    const replayStatus = await page.evaluate(async (body) => {
      const api = "https://localhost:7043/api";
      const { token } = await (
        await fetch(api + "/auth/csrf", { credentials: "include" })
      ).json();
      const response = await fetch(api + "/passkeys/authentication/verify", {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify(body),
      });
      return {
        status: response.status,
        message: (await response.json()).message,
      };
    }, originalAssertion);
    expect(replayStatus.status).toBe(400);
    expect(replayStatus.message).toContain("already used");
  }

  await page.getByRole("button", { name: "Sign in with a passkey" }).click();
  await expect(page).toHaveURL(/\/profile$/);
  await expect(
    page.getByText("Signed in with passkey", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Alice Morgan" }),
  ).toBeVisible();
  await expect(page.getByText(/Last used/)).toBeVisible();
  await page.getByRole("button", { name: "Remove Test laptop" }).click();
  await page.getByRole("button", { name: "Confirm remove" }).click();
  await expect(page.getByRole("status")).toContainText("Passkey removed");
  await expect(
    page.getByText("No passkeys yet", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  // No server credentials remain, so the complete alternative sign-in section disappears.
  await expect(
    page.getByRole("button", { name: "Sign in with a passkey" }),
  ).toHaveCount(0);
  await expect(
    page.getByText("or use your device", { exact: true }),
  ).toHaveCount(0);
  await page.reload();
  await expect(
    page.getByRole("button", { name: "Sign in with a passkey" }),
  ).toHaveCount(0);
  await passwordLogin(page);
});

test("cancelled WebAuthn request leaves password sign-in usable", async ({
  page,
}) => {
  // This test isolates browser cancellation; the real registration flow is exercised above.
  await page.route("**/api/passkeys/authentication/availability", (route) =>
    route.fulfill({ json: { hasPasskeys: true } }),
  );
  await page.addInitScript(() => {
    Object.defineProperty(navigator.credentials, "get", {
      value: async () => {
        throw new DOMException("Cancelled", "NotAllowedError");
      },
    });
  });
  await page.goto("/login");
  await page.getByRole("button", { name: "Sign in with a passkey" }).click();
  await expect(page.getByRole("alert")).toContainText("cancelled");
  await passwordLogin(page);
});

test("unsupported browser explains fallback", async ({ page }) => {
  await page.route("**/api/passkeys/authentication/availability", (route) =>
    route.fulfill({ json: { hasPasskeys: true } }),
  );
  await page.addInitScript(() => {
    Object.defineProperty(window, "PublicKeyCredential", { value: undefined });
  });
  await page.goto("/login");
  await expect(
    page.getByRole("button", { name: "Sign in with a passkey" }),
  ).toBeDisabled();
  await expect(page.getByText(/Passkeys are unavailable/)).toBeVisible();
  await passwordLogin(page);
});
