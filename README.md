# Passkey Studio

A runnable authentication demo with **Angular 22**, **ASP.NET Core 10 Web API**, password sign-in, and discoverable WebAuthn passkeys. Both sign-in methods open the same **User Profile Settings** page and use the same cookie session.

There is **no database and no JWT authentication**. The API uses the maintained [FIDO2 .NET library, Fido2 4.3.1](https://www.nuget.org/packages/Fido2/4.3.1); the browser uses [SimpleWebAuthn](https://simplewebauthn.dev/docs/packages/browser) to serialize WebAuthn requests and responses. All cryptographic verification happens in the API.

## Prerequisites

- .NET **10 SDK**.
- Node **24.15+** in the Node 24 line (or Node 22.22.3+ in the Node 22 line), with npm. See [Angular version compatibility](https://angular.dev/reference/versions).
- Chrome, Edge, Safari, or Firefox with WebAuthn support, plus a device authenticator or FIDO2 security key that supports discoverable credentials and user verification. For Windows Hello, set up a PIN in Windows Settings first. An authenticator can use a PIN; biometrics are optional.
- Internet access for the initial NuGet/npm restore. Actual sign-in does not call an external identity provider.

## Run locally

Run these commands from the repository root. The frontend and API must both use **HTTPS and the hostname `localhost`**.

### 1. Configure HTTPS

On Windows, in PowerShell:

```powershell
./scripts/setup-https.ps1 -TrustCertificate
```

This asks the OS to trust the ASP.NET Core local development certificate and exports it for Angular. Alternatively, use these commands on any supported OS:

```sh
mkdir -p certs
dotnet dev-certs https --trust
dotnet dev-certs https --export-path certs/localhost.pem --format Pem --no-password
```

The export creates `localhost.pem` and `localhost.key`. The API uses the ASP.NET Core development certificate; Angular uses the exported certificate and private key. The `certs/` folder is ignored by Git. Do not commit or distribute the private key. Restart your browser after trusting the certificate if necessary. On Linux, follow any browser-specific trust instructions printed by `dotnet dev-certs`.

### 2. Install dependencies (first run)

```sh
dotnet restore PasskeyDemo.sln
npm --prefix frontend ci
```

### 3. Start both applications in one terminal

```sh
npm start
```

Run `npm start` from either the repository root or the `frontend` folder. It starts both the API at **https://localhost:7043** and Angular at **https://localhost:4200**, with `[API]` and `[WEB]` log prefixes. Wait for both servers to report that they are listening, then open **[https://localhost:4200](https://localhost:4200)**. Do not switch to `127.0.0.1` or HTTP. If a certificate warning remains, fix the local certificate trust before testing passkeys.

Press **Ctrl+C once** in that terminal to stop both applications. If either process exits or fails to start, the launcher stops the other process too. Stop any older, separately launched API or Angular instance first to avoid an address-in-use error. The launcher does not terminate unrelated existing processes.

A quick API HTTPS check is [the CSRF initialization endpoint](https://localhost:7043/api/auth/csrf), which returns JSON. The API root `/` has no page.

If you need to run a single application separately, use `npm run start:api` or `npm run start:frontend` from the `frontend` folder. The backend command builds/restores the API as needed; the HTTPS certificate setup remains a one-time prerequisite.

## Demo credentials

| Username | Password |
| --- | --- |
| `alice` | `AliceDemo!123` |
| `bob` | `BobDemo!123` |

Usernames are case-insensitive; passwords are case-sensitive. These users are predefined and their passwords are hashed in memory with ASP.NET Core's `PasswordHasher`. They are intentionally public demo credentials.

## Free deployment on Render

The repository includes `Dockerfile`, `.dockerignore`, and `render.yaml` for **one Free Render web service**. The Docker build compiles Angular and publishes the API, then places the Angular production files in the API's `wwwroot` folder. Both run behind the same public HTTPS URL; the frontend calls `/api` on that origin. Local `npm start` still runs the two development servers.

1. Put this project in a GitHub repository. Include the source, lockfiles, `Dockerfile`, `.dockerignore`, and `render.yaml`. Local certificates, dependencies and build outputs are excluded; Render does not need your local HTTPS certificate.
2. Sign in at [Render](https://dashboard.render.com/) and connect the GitHub repository.
3. Select **New → Blueprint**, choose the repository, and use its root `render.yaml` file.
4. Confirm the service's compute plan is **Free** before deploying. This Blueprint declares one web service and no paid database or other resources.
5. Deploy and wait for the build and `/healthz` check to pass. Open the assigned **`https://<your-service>.onrender.com`** address.
6. Sign in with the demo password and register a passkey at this public address. Localhost passkeys belong to a different RP and cannot authenticate on the public domain.

You can also create a **Web Service** manually: select **Docker**, use `./Dockerfile` and the repository root as the Docker build context, choose **Free**, and set `/healthz` as the health check path. The container provides its own startup command; do not use `npm start` as the hosted Docker command.

Render provides the public `RENDER_EXTERNAL_URL` and `PORT`. At startup the API derives the exact WebAuthn origin, RP hostname, allowed HTTP host, and listening port from these platform values. It reads forwarded HTTPS/client-IP headers only when `RENDER=true`; this proxy trust setting is intended specifically for Render's managed ingress. The Host header is still restricted to the configured host. Health checks are available over internal HTTP; public traffic uses Render-managed HTTPS and secure cookies. For a custom domain, set `PUBLIC_ORIGIN=https://your-domain.example` with no trailing slash, configure the domain in Render, and register new passkeys on that domain.

**Free-plan behavior:** Render's free service sleeps after 15 minutes without inbound traffic and can restart at other times. Waking it can take about a minute. Because this application intentionally stores everything in memory, restarts and sleep/wake cycles clear passkey registrations, challenges, and sessions. Use a demo password and register again. Free instances have shared monthly usage limits; keep the compute plan Free and check the account's usage/billing settings. See [Render's free-service limits](https://render.com/docs/free) and [Docker hosting documentation](https://render.com/docs/docker).

The files prepare a deployment; they do not create a hosted service or repository automatically. Docker is not required on your computer when Render builds the image from GitHub. If you have Docker installed and want to build the image yourself, run `docker build -t passkey-studio .` from the repository root.

## Walkthrough

1. Open the login page and sign in with `alice` / `AliceDemo!123`.
2. In **User Profile Settings → Passkeys**, enter a friendly name such as **Personal laptop** and select **Add a passkey**.
3. Follow your browser or device prompt. Use Windows Hello, Touch ID, a compatible security key, or another offered authenticator. Complete its PIN or biometric verification.
4. The named passkey appears in **Registered passkeys**. Username/password sign-in remains available.
5. Select **Sign out**.
6. Select **Sign in with a passkey** without filling in the username or password. Choose Alice's passkey and complete the device prompt.
7. The same profile page opens, with **Signed in with passkey** and the passkey's last-use time.
8. Optionally select **Remove** next to the passkey and confirm. Sign out and use the password again. Removing the server credential does not remove it from your device's password manager.
9. Sign in as Bob to see a separate passkey list. Credentials are owned by their registered account.

The login page shows the passkey button, divider, and device hint only when the API has at least one registered passkey. The check runs when the login page opens and when the window regains focus, so signing out after registration enables the option; removing the last passkey or restarting the API hides it. The check returns only an aggregate boolean, without usernames or credential details. It reflects server registration, not whether a matching key is available on this particular device.

If a prompt is cancelled or times out, the page shows a helpful message and permits another attempt or password sign-in. When passkeys are registered, unsupported browsers show an explanation and disable the passkey action.

## How the flows work

**Password:** Angular obtains an antiforgery token, then sends the username/password to the API over HTTPS. The API checks the in-memory password hash and creates an ASP.NET Core cookie session. Invalid credentials get a generic error; missing fields get explicit validation messages.

**Registration:** An authenticated user submits a friendly name. The API generates cryptographically random WebAuthn creation options with the account's opaque user handle, RP identity, and existing credential exclusions. It requires a **discoverable credential** and **user verification**; no specific authenticator attachment is forced. The original options and friendly name are held server-side for **120 seconds**, bound to the browser, signed-in account, and current session. The browser invokes `navigator.credentials.create()` through SimpleWebAuthn and returns the attestation. Fido2 verifies the challenge, exact frontend origin, RP ID, user-presence/verification flags, and attestation response. Only after verification does the API store the credential ID, public key, user handle, signature counter, transports, name, and timestamps. It never receives the authenticator's private key or biometrics. Password credentials are independent of this operation.

**Passkey sign-in:** The API generates assertion options with an empty `allowCredentials` list so the authenticator can discover accounts for the RP. It stores a separate, expiring challenge bound to this browser. After `navigator.credentials.get()`, the API resolves the credential ID to an account, checks its user handle, and uses Fido2 to verify the signature, challenge, origin, RP ID, user verification, and signature counter. A successful assertion updates the counter/last-use time and issues the same type of session as password login. Zero counters used by some authenticators are supported by the library.

**Single use:** The ceremony store atomically removes an entry **before** attempting verification. Success, failure, and replay therefore cannot reuse a consumed challenge. Expiration is independently checked by the API; the browser timeout is only a UI hint. Expired/abandoned entries are pruned when new ceremonies begin. A new sign-in or sign-out rotates the browser binding; registration also checks the current session ID. Every attempt starts with fresh options.

**Sessions and request protection:** The authentication cookie is an encrypted opaque ticket key. Its ticket lives in `IMemoryCache` for 30 minutes without sliding renewal. Sign-out removes the ticket, so replaying the old cookie fails. Cookies are `Secure`, `HttpOnly`, `SameSite=Strict`, host-only, and use the `__Host-` prefix and `/` path. All POST/DELETE requests require an ASP.NET antiforgery token in `X-CSRF-TOKEN`. Angular refreshes this identity-bound token after sign-in/sign-out and sends requests with credentials. An in-process limit of 120 API requests per minute per IP bounds repeated demo requests.

## Local origin and relying party configuration

| Setting | Default | Location |
| --- | --- | --- |
| Frontend URL / allowed WebAuthn origin | `https://localhost:4200` | `backend/PasskeyDemo.Api/appsettings.json` → `WebAuthn:FrontendOrigin` |
| RP ID | `localhost` (no scheme or port) | `WebAuthn:RpId` |
| RP display name | `Passkey Studio` | `WebAuthn:RpName` |
| Challenge lifetime | `120` seconds | `WebAuthn:ChallengeLifetimeSeconds` |
| API HTTPS URL | `https://localhost:7043` | `Properties/launchSettings.json` and `frontend/src/app/config.ts` |
| Angular HTTPS certificate | `../certs/localhost.pem`, `../certs/localhost.key` | `frontend/package.json` → `start:frontend` |

The origin checked by WebAuthn is **the Angular page's origin**, including its scheme and port. The RP ID is the host, with no port. CORS permits only the configured frontend origin with credentials; it does not use a wildcard. The two HTTPS localhost URLs are different origins but the same site, so Strict cookies work across their ports. No development proxy is involved. Do not mix hostnames or schemes.

If you change ports or domains, update both applications, `AllowedHosts`, certificate coverage, and the test URLs. Register new passkeys when changing the RP ID. Environment variables can override backend settings, e.g. `WebAuthn__FrontendOrigin`.

## Project structure

```text
PasskeyDemo.sln
package.json                         Root shortcut for the combined npm start
Dockerfile                           Combined Angular + ASP.NET production image
.dockerignore                        Excludes local keys and build artifacts
render.yaml                          One free Render Docker service
backend/
  PasskeyDemo.Api/
    Program.cs                       HTTPS, CORS, cookie auth, CSRF, rate limit
    Models.cs                        API contracts and settings
    DemoStore.cs                     Predefined users and credential ownership
    CeremonyStore.cs                 Expiring, single-use WebAuthn options
    MemoryTicketStore.cs             In-memory cookie session tickets
    Controllers/AuthController.cs    Password login, session, CSRF, sign-out
    Controllers/PasskeysController.cs Registration, assertion, list and removal
    appsettings.json
    Properties/launchSettings.json
  PasskeyDemo.Api.Tests/              xUnit unit and API integration tests
frontend/
  src/app/
    api.service.ts                   Credentialed API calls and CSRF headers
    auth.service.ts                  Shared signed-in user state
    passkey.service.ts               Browser WebAuthn ceremonies
    login.component.*                Both sign-in methods and validation
    profile.component.*              User profile settings and passkey management
    app.routes.ts                    Login/profile route guards
    config.ts                        API URL
  e2e/auth.spec.ts                    Headless Chrome + virtual authenticator
  playwright.config.ts
scripts/setup-https.ps1
```

## API endpoints

All routes are prefixed with `/api`. State-changing requests require the antiforgery header; registration, listing, removal and sign-out also require a session.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/auth/csrf` | Initialize browser cookies and get request token |
| POST | `/auth/password` | Password sign-in |
| GET | `/auth/me` | Current signed-in user |
| POST | `/auth/logout` | Invalidate the current session |
| GET | `/passkeys` | List current user's passkeys |
| GET | `/passkeys/authentication/availability` | Anonymous aggregate flag for passkey sign-in visibility |
| DELETE | `/passkeys/{id}` | Remove an owned passkey |
| POST | `/passkeys/registration/options` | Begin authenticated registration with `{ name }` |
| POST | `/passkeys/registration/verify` | Verify `{ ceremonyId, credential }` |
| POST | `/passkeys/authentication/options` | Begin username-free authentication |
| POST | `/passkeys/authentication/verify` | Verify `{ ceremonyId, credential }` and sign in |

## Build and test

From the repository root:

```sh
dotnet test PasskeyDemo.sln
cd frontend
npm run build
npm run test:e2e
```

The browser tests use installed **Google Chrome**, a headless virtual authenticator, and the local HTTPS certificates. They start both servers automatically if the ports are free. No physical PIN or biometric prompt is involved in these tests. Run against a freshly started API for a clean credential store. If Chrome is absent, install it or change `channel: 'chrome'` in the Playwright config to an installed Chromium channel. Test traces are retained on failure under `frontend/test-results/`.

Unit/integration tests cover single-use concurrency, challenge expiration and binding, account ownership, password fallback, cookie flags, sign-out cookie replay rejection, session reset, antiforgery identity changes, protected routes, and CORS. Browser tests cover password validation/errors, profile reload, registration, passkey sign-in, modified challenges/origins/signatures, replay rejection after success and failure, account isolation/removal, cancelled requests, and unsupported browsers. Desktop and mobile screenshots are saved with the browser test results, and mobile checks reject horizontal overflow.

## In-memory limitations

- **Restarting the API resets all demo data:** predefined users are recreated; passkey records, pending challenges and sessions are lost. Old cookies no longer authenticate. Reload the frontend, sign in with a demo password, and register a new passkey.
- A restart cannot erase credentials from the authenticator. Old device entries may remain visible but cannot authenticate after the server forgets their public key. Remove obsolete entries in the device's password manager before re-registering if it reports an existing credential.
- This is a single-process demo. Multiple API instances do not share users, credentials, challenge state, sessions, or rate-limit state. There is no recovery, account creation, password reset, audit system, or production deployment setup.
- ASP.NET Core may persist its **Data Protection encryption keys** outside the app by default. These are framework keys, not a user or passkey database. Authentication tickets still live only in memory, so persisted encryption keys do not preserve sessions across restarts.
- Passkey removal prevents future use of that credential but does not terminate other already-issued sessions. The current session ends on sign-out or expiry.
- User verification is required, so older U2F keys without resident credentials/PIN capability cannot complete this demo's passkey flow. Try a compatible FIDO2 key or a platform authenticator.

For library implementation details see the [Fido2 project](https://github.com/passwordless-lib/fido2-net-lib). This demo intentionally keeps storage and account lifecycle minimal so the authentication flow is easy to follow.
