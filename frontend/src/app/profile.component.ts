import { Component, inject, OnInit, signal } from "@angular/core";
import { DatePipe } from "@angular/common";
import { FormsModule } from "@angular/forms";
import { Router } from "@angular/router";
import { AuthService } from "./auth.service";
import { PasskeyService, type Passkey } from "./passkey.service";
import { errorMessage } from "./api.service";

@Component({
  selector: "app-profile",
  imports: [FormsModule, DatePipe],
  templateUrl: "./profile.component.html",
})
export class ProfileComponent implements OnInit {
  readonly auth = inject(AuthService);
  readonly passkeys = inject(PasskeyService);
  private readonly router = inject(Router);
  readonly keys = signal<Passkey[]>([]);
  readonly busy = signal("loading");
  readonly error = signal("");
  readonly success = signal("");
  readonly removing = signal<string | null>(null);
  name = "";
  submitted = false;
  ngOnInit(): void {
    void this.run("loading", async () => {
      this.keys.set(await this.passkeys.list());
    });
  }

  async register(): Promise<void> {
    this.submitted = true;
    if (!this.name.trim() || this.name.length > 60) return;
    await this.run("register", async () => {
      await this.passkeys.register(this.name.trim());
      this.name = "";
      this.submitted = false;
      this.success.set(
        "Passkey added. Sign out and try signing in with it. Your password still works.",
      );
      this.keys.set(await this.passkeys.list());
    });
  }
  async remove(key: Passkey): Promise<void> {
    await this.run("remove", async () => {
      await this.passkeys.remove(key.id);
      this.keys.update((keys) => keys.filter((k) => k.id !== key.id));
      this.removing.set(null);
      this.success.set(
        "Passkey removed from this demo account. You can still sign in with your password.",
      );
    });
  }
  async logout(): Promise<void> {
    await this.run("logout", async () => {
      await this.auth.logout();
      await this.router.navigateByUrl("/login");
    });
  }
  private async run(task: string, action: () => Promise<void>): Promise<void> {
    if (this.busy() && this.busy() !== "loading") return;
    this.busy.set(task);
    this.error.set("");
    this.success.set("");
    try {
      await action();
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set("");
    }
  }
}
