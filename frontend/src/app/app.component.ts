import { Component } from "@angular/core";
import { RouterOutlet } from "@angular/router";

@Component({
  selector: "app-root",
  imports: [RouterOutlet],
  template: ` <div class="app-shell">
    <header class="topbar">
      <a class="brand" href="/" aria-label="Passkey Studio home"
        ><span class="brand-icon" aria-hidden="true">p</span>Passkey Studio<span
          class="demo-badge"
          >DEMO</span
        ></a
      >
      <span class="environment"
        ><span class="status-dot"></span> LOCAL ENVIRONMENT</span
      >
    </header>
    <router-outlet />
    <footer>
      Built to explore a simpler sign-in.<span
        >Angular + ASP.NET Core · WebAuthn / FIDO2</span
      >
    </footer>
  </div>`,
})
export class AppComponent {}
