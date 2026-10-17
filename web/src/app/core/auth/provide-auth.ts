import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { AuthService } from './auth.service';

/** Initializes MSAL (and finishes a pending sign-in) before the router's first navigation. */
export function provideAuth(): EnvironmentProviders {
  return makeEnvironmentProviders([provideAppInitializer(() => inject(AuthService).initialize())]);
}
