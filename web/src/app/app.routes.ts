import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { firmGuard, noFirmGuard, ownerGuard } from './core/firm/firm.guards';

export const routes: Routes = [
  {
    // Every page needs a signed-in user (the API rejects anonymous calls anyway)
    path: '',
    canActivateChild: [authGuard],
    children: [
      {
        // Signed in, but without a firm yet
        path: 'onboarding',
        title: 'Bienvenida · Plumy',
        canActivate: [noFirmGuard],
        loadComponent: () => import('./onboarding/create-firm').then((m) => m.CreateFirm),
      },
      {
        // The app proper: everything here works with the firm's data
        path: '',
        canActivateChild: [firmGuard],
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
          {
            path: 'team',
            title: 'Equipo · Plumy',
            canActivate: [ownerGuard],
            loadComponent: () => import('./team/team').then((m) => m.Team),
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: 'client-companies' },
];
