import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'client-companies' },
  {
    path: 'client-companies',
    title: 'Empresas cliente · Plumy',
    loadComponent: () =>
      import('./client-companies/client-company-list/client-company-list').then(
        (m) => m.ClientCompanyList,
      ),
  },
  { path: '**', redirectTo: 'client-companies' },
];
