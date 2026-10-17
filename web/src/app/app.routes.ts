import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    // Every page needs a signed-in user (the API rejects anonymous calls anyway)
    path: '',
    canActivateChild: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'client-companies' },
      {
        path: 'client-companies',
        title: 'Empresas cliente · Plumy',
        loadComponent: () =>
          import('./client-companies/client-company-list/client-company-list').then(
            (m) => m.ClientCompanyList,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'client-companies' },
];
