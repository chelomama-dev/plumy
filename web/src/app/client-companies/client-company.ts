/** Mirrors Plumy.Application.ClientCompanies.ClientCompanyResponse. */
export interface ClientCompany {
  id: string;
  name: string;
  taxId: string;
  taxIdType: string;
}

export const clientCompaniesUrl = '/api/client-companies';

/** Same limit as ClientCompany.MaxNameLength in the domain. */
export const maxClientCompanyNameLength = 200;
