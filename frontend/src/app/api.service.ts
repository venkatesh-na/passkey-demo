import { Injectable, inject } from "@angular/core";
import { HttpClient, HttpErrorResponse } from "@angular/common/http";
import { firstValueFrom } from "rxjs";
import { API_URL } from "./config";

@Injectable({ providedIn: "root" })
export class ApiService {
  private readonly http = inject(HttpClient);
  private token = "";

  get<T>(path: string): Promise<T> {
    return firstValueFrom(
      this.http.get<T>(API_URL + path, { withCredentials: true }),
    );
  }
  async refreshCsrf(): Promise<void> {
    this.token = (await this.get<{ token: string }>("/auth/csrf")).token;
  }
  async send<T>(
    method: "POST" | "DELETE",
    path: string,
    body: unknown = {},
  ): Promise<T> {
    if (!this.token) await this.refreshCsrf();
    return firstValueFrom(
      this.http.request<T>(method, API_URL + path, {
        body,
        withCredentials: true,
        headers: { "X-CSRF-TOKEN": this.token },
      }),
    );
  }
}

export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0)
      return "Cannot reach the API. Check that it is running and that you trust the local HTTPS certificate.";
    if (error.status === 401)
      return error.error?.message ?? "Your session has expired. Sign in again.";
    if (error.status === 429)
      return "Too many attempts. Wait a minute and try again.";
    if (error.error?.message) return error.error.message;
    if (error.error?.errors)
      return Object.values(error.error.errors).flat().join(" ");
    return "The request could not be completed. Please try again.";
  }
  const name = error instanceof Error ? error.name : "";
  if (name === "NotAllowedError" || name === "AbortError")
    return "The passkey request was cancelled, timed out, or no matching passkey was available. You can try again or use your password.";
  if (name === "InvalidStateError")
    return "A passkey for this account already exists on that authenticator. Try another device or security key.";
  if (name === "NotSupportedError")
    return "This browser or authenticator cannot create the required passkey. Try a supported browser or another device.";
  if (name === "SecurityError")
    return "Passkeys require the configured HTTPS origin. Open https://localhost:4200 and trust the development certificate.";
  return error instanceof Error
    ? error.message
    : "Passkey authentication failed. Please try again.";
}
