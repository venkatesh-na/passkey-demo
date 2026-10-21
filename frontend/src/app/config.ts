import { isDevMode } from "@angular/core";

// ng serve uses the separate local API. The production bundle shares its host with ASP.NET.
export const API_URL = isDevMode() ? "https://localhost:7043/api" : "/api";
