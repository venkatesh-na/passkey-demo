import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  fullyParallel: false,
  use: {
    baseURL: "https://localhost:4200",
    ignoreHTTPSErrors: true,
    channel: "chrome",
    trace: "retain-on-failure",
  },
  webServer: [
    {
      command:
        "dotnet run --project ../backend/PasskeyDemo.Api --no-launch-profile --urls https://localhost:7043",
      url: "https://localhost:7043/api/auth/csrf",
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env["CI"],
      timeout: 120_000,
    },
    {
      command: "npm run start:frontend",
      url: "https://localhost:4200",
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env["CI"],
      timeout: 120_000,
    },
  ],
});
