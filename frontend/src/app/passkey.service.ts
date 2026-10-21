import { Injectable, inject } from "@angular/core";
import {
  browserSupportsWebAuthn,
  startAuthentication,
  startRegistration,
  type PublicKeyCredentialCreationOptionsJSON,
  type PublicKeyCredentialRequestOptionsJSON,
} from "@simplewebauthn/browser";
import { ApiService } from "./api.service";
import { AuthService, type User } from "./auth.service";

export interface Passkey {
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
}
interface Ceremony<T> {
  ceremonyId: string;
  options: T;
}

@Injectable({ providedIn: "root" })
export class PasskeyService {
  readonly supported = window.isSecureContext && browserSupportsWebAuthn();
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  async hasRegisteredPasskeys(): Promise<boolean> {
    const result = await this.api.get<{ hasPasskeys: boolean }>(
      "/passkeys/authentication/availability",
    );
    return result.hasPasskeys;
  }
  list(): Promise<Passkey[]> {
    return this.api.get("/passkeys");
  }
  remove(id: string): Promise<void> {
    return this.api.send("DELETE", "/passkeys/" + encodeURIComponent(id));
  }

  async register(name: string): Promise<void> {
    const { ceremonyId, options } = await this.api.send<
      Ceremony<PublicKeyCredentialCreationOptionsJSON>
    >("POST", "/passkeys/registration/options", { name });
    const credential = await startRegistration({ optionsJSON: options });
    await this.api.send("POST", "/passkeys/registration/verify", {
      ceremonyId,
      credential,
    });
  }
  async login(): Promise<void> {
    const { ceremonyId, options } = await this.api.send<
      Ceremony<PublicKeyCredentialRequestOptionsJSON>
    >("POST", "/passkeys/authentication/options");
    const credential = await startAuthentication({ optionsJSON: options });
    const user = await this.api.send<User>(
      "POST",
      "/passkeys/authentication/verify",
      { ceremonyId, credential },
    );
    await this.auth.acceptSession(user);
  }
}
