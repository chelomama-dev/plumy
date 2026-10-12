/** Mirrors Plumy.Application.ClientCompanies.ClientCompanyResponse. */
export interface ClientCompany {
  id: string;
  name: string;
  taxId: string;
  taxIdType: string;
}

export const clientCompaniesUrl = '/api/client-companies';
