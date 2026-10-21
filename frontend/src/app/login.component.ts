import { Component, HostListener, inject, OnInit, signal } from "@angular/core";
import { FormsModule } from "@angular/forms";
import { Router } from "@angular/router";
import { AuthService } from "./auth.service";
import { PasskeyService } from "./passkey.service";
import { errorMessage } from "./api.service";

@Component({
  selector: "app-login",
  imports: [FormsModule],
  templateUrl: "./login.component.html",
})
export class LoginComponent implements OnInit {
  readonly auth = inject(AuthService);
  readonly passkeys = inject(PasskeyService);
  private readonly router = inject(Router);
  readonly busy = signal("");
  readonly error = signal("");
  readonly hasPasskeys = signal(false);
  username = "";
  password = "";
  submitted = false;

  ngOnInit(): void {
    void this.refreshPasskeyAvailability();
  }

  @HostListener("window:focus")
  async refreshPasskeyAvailability(): Promise<void> {
    try {
      this.hasPasskeys.set(await this.passkeys.hasRegisteredPasskeys());
    } catch {
      // Keep the optional sign-in method hidden when availability cannot be checked.
      this.hasPasskeys.set(false);
    }
  }

  async passwordLogin(): Promise<void> {
    this.submitted = true;
    if (
      !this.username.trim() ||
      !this.password ||
      this.username.length > 100 ||
      this.password.length > 200
    )
      return;
    await this.run("password", () =>
      this.auth.password(this.username.trim(), this.password),
    );
    this.password = "";
  }
  async passkeyLogin(): Promise<void> {
    await this.run("passkey", () => this.passkeys.login());
  }
  private async run(
    method: string,
    action: () => Promise<void>,
  ): Promise<void> {
    if (this.busy()) return;
    this.busy.set(method);
    this.error.set("");
    try {
      await action();
      await this.router.navigateByUrl("/profile");
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set("");
    }
  }
}
