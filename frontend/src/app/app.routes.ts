import { inject } from "@angular/core";
import { type Routes, Router } from "@angular/router";
import { AuthService } from "./auth.service";
import { LoginComponent } from "./login.component";
import { ProfileComponent } from "./profile.component";

export const routes: Routes = [
  {
    path: "login",
    component: LoginComponent,
    canActivate: [
      () =>
        inject(AuthService).user()
          ? inject(Router).createUrlTree(["/profile"])
          : true,
    ],
  },
  {
    path: "profile",
    component: ProfileComponent,
    canActivate: [
      () =>
        inject(AuthService).user()
          ? true
          : inject(Router).createUrlTree(["/login"]),
    ],
  },
  { path: "**", redirectTo: "login" },
];
