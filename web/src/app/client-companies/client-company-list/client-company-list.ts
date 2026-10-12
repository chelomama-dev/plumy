import { UpperCasePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { PagedResult } from '../../shared/paged-result';
import { provideSpanishPaginatorIntl } from '../../shared/spanish-paginator-intl';
import { ClientCompany, clientCompaniesUrl } from '../client-company';

export const searchDebounceMs = 300;

@Component({
  selector: 'app-client-company-list',
  imports: [
    UpperCasePipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  templateUrl: './client-company-list.html',
  styleUrl: './client-company-list.scss',
  providers: [provideSpanishPaginatorIntl()],
})
export class ClientCompanyList {
  protected readonly displayedColumns = ['name', 'taxId', 'taxIdType'];
  protected readonly pageSizeOptions = [10, 20, 50, 100];

  /** Raw text of the search box, updated on every keystroke. */
  protected readonly searchInput = signal('');

  /** Search actually sent to the API: RxJS for the time-based part (debounce), back to a signal. */
  protected readonly search = toSignal(
    toObservable(this.searchInput).pipe(
      map((text) => text.trim()),
      debounceTime(searchDebounceMs),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  protected readonly pageSize = signal(20);

  /** 0-based, like mat-paginator. Writable, but resets to the first page whenever the search changes. */
  protected readonly pageIndex = linkedSignal({
    source: this.search,
    computation: () => 0,
  });

  /** Re-fetches by itself when any signal read here changes, cancelling the in-flight request. */
  protected readonly companies = httpResource<PagedResult<ClientCompany>>(() => ({
    url: clientCompaniesUrl,
    params: {
      page: this.pageIndex() + 1,
      pageSize: this.pageSize(),
      ...(this.search() ? { search: this.search() } : {}),
    },
  }));

  /** Last page received: keeps the table on screen while the next one loads instead of flashing empty. */
  protected readonly page = linkedSignal<
    PagedResult<ClientCompany> | undefined,
    PagedResult<ClientCompany> | undefined
  >({
    // value() throws while the resource is in error, so check hasValue() first
    source: () => (this.companies.hasValue() ? this.companies.value() : undefined),
    computation: (current, previous) => current ?? previous?.value,
  });

  protected onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }
}
