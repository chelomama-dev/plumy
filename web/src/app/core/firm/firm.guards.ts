import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { CurrentUserService } from './current-user.service';

/** The app proper needs a firm: users without one go to onboarding first. */
export const firmGuard: CanActivateFn & CanActivateChildFn = async () => {
  // authGuard runs alongside and is already sending signed-out users to sign in; /api/me would only 401
  if (!inject(AuthService).isAuthenticated()) {
    return false;
  }

  const router = inject(Router);
  const user = await inject(CurrentUserService).load();
  return user.firm ? true : router.createUrlTree(['/onboarding']);
};

/** Onboarding is only for users without a firm: the rest go to the app. */
export const noFirmGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const user = await inject(CurrentUserService).load();
  return user.firm ? router.createUrlTree(['/']) : true;
};

/**
 * Pages that manage the firm itself (its team) are for its owner. Runs under firmGuard, so the user is loaded
 * already. Hiding it is only for the user's sake: the API returns 403 to members anyway.
 */
export const ownerGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const user = await inject(CurrentUserService).load();
  return user.firm?.role === 'Owner' ? true : router.createUrlTree(['/']);
};
