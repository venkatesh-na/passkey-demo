import { Injectable, inject, signal } from "@angular/core";
import { HttpErrorResponse } from "@angular/common/http";
import { ApiService, errorMessage } from "./api.service";

export interface User {
  username: string;
  displayName: string;
  loginMethod: string;
}

@Injectable({ providedIn: "root" })
export class AuthService {
  readonly user = signal<User | null>(null);
  readonly startupError = signal("");
  private readonly api = inject(ApiService);

  async initialize(): Promise<void> {
    try {
      await this.api.refreshCsrf();
      this.user.set(await this.api.get<User>("/auth/me"));
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401))
        this.startupError.set(errorMessage(error));
    }
  }
  async acceptSession(user: User): Promise<void> {
    this.user.set(user);
    this.startupError.set("");
    // ASP.NET antiforgery tokens are identity-bound; refresh after every identity change.
    await this.api.refreshCsrf();
  }
  async password(username: string, password: string): Promise<void> {
    await this.acceptSession(
      await this.api.send<User>("POST", "/auth/password", {
        username,
        password,
      }),
    );
  }
  async logout(): Promise<void> {
    try {
      await this.api.send<void>("POST", "/auth/logout");
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401))
        throw error;
    }
    this.user.set(null);
    await this.api.refreshCsrf();
  }
}
