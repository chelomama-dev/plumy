import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { authConfig } from './auth.config';
import { AuthService } from './auth.service';

/** Adds the access token to the API calls (and only to them). */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  if (!isApiRequest(request.url) || !auth.isAuthenticated()) {
    return next(request);
  }

  return from(auth.getAccessToken()).pipe(
    switchMap((token) => next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))),
  );
};

function isApiRequest(url: string): boolean {
  const prefix = authConfig.apiPathPrefix;
  return url === prefix || url.startsWith(`${prefix}/`) || url.startsWith(`${prefix}?`);
}
