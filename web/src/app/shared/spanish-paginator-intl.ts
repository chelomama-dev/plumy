import { Injectable, Provider } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** Spanish texts for mat-paginator (Material only ships English). */
@Injectable()
export class SpanishPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Elementos por página';
  override nextPageLabel = 'Página siguiente';
  override previousPageLabel = 'Página anterior';
  override firstPageLabel = 'Primera página';
  override lastPageLabel = 'Última página';

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0) {
      return '0 de 0';
    }
    const start = page * pageSize;
    const end = Math.min(start + pageSize, length);
    return `${start + 1} – ${end} de ${length}`;
  };
}

/**
 * Provide it in each lazy component/route that has a paginator, not in app.config: importing
 * MatPaginatorIntl there pulls the whole paginator (select, overlay...) into the initial bundle.
 */
export function provideSpanishPaginatorIntl(): Provider {
  return { provide: MatPaginatorIntl, useClass: SpanishPaginatorIntl };
}
