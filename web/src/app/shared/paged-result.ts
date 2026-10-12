/** Mirrors Plumy.Application.Common.PagedResult<T>. Page is 1-based, as in the API. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
