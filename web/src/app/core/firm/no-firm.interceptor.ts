import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { CurrentUserService } from './current-user.service';

/** ProblemDetails type the API returns when the user doesn't belong to a firm (FirmMembershipFilter). */
export const noFirmProblemType = 'urn:plumy:problem:no-firm';

/**
 * The firm's owner can remove someone while they're using the app. Their next API call is then refused with
 * "no firm": forget the firm we had cached and send them to onboarding, instead of showing errors.
 */
export const noFirmInterceptor: HttpInterceptorFn = (request, next) => {
  const currentUser = inject(CurrentUserService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 403 &&
        error.error?.type === noFirmProblemType
      ) {
        currentUser.reset();
        void router.navigateByUrl('/onboarding');
      }
      // The caller still gets the error, so whatever it was doing stops
      return throwError(() => error);
    }),
  );
};
