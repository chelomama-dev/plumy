import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

/** Signed-in users go through; everyone else is sent to sign in and comes back to the same URL. */
export const authGuard: CanActivateFn & CanActivateChildFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated()) {
    return true;
  }

  void auth.login(state.url);
  return false;
};
