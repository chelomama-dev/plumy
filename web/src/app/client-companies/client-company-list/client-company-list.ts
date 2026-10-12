import { UpperCasePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, inject, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { debounceTime, distinctUntilChanged, firstValueFrom, map } from 'rxjs';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { PagedResult } from '../../shared/paged-result';
import { provideSpanishPaginatorIntl } from '../../shared/spanish-paginator-intl';
import { ClientCompaniesApi } from '../client-companies-api';
import { ClientCompany, clientCompaniesUrl } from '../client-company';
import {
  ClientCompanyFormDialog,
  ClientCompanyFormDialogData,
} from '../client-company-form-dialog/client-company-form-dialog';

export const searchDebounceMs = 300;

@Component({
  selector: 'app-client-company-list',
  imports: [
    UpperCasePipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  templateUrl: './client-company-list.html',
  styleUrl: './client-company-list.scss',
  providers: [provideSpanishPaginatorIntl()],
})
export class ClientCompanyList {
  private readonly api = inject(ClientCompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly displayedColumns = ['name', 'taxId', 'taxIdType', 'actions'];
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

  /** Without a company it creates one; with one, it renames it. */
  protected async openForm(company?: ClientCompany): Promise<void> {
    const dialogRef = this.dialog.open<
      ClientCompanyFormDialog,
      ClientCompanyFormDialogData,
      ClientCompany
    >(ClientCompanyFormDialog, { data: { company }, width: '480px' });

    const saved = await firstValueFrom(dialogRef.afterClosed());
    if (!saved) {
      return;
    }

    this.snackBar.open(company ? 'Empresa cliente renombrada.' : 'Empresa cliente creada.', 'OK', {
      duration: 4000,
    });
    this.companies.reload();
  }

  protected async archive(company: ClientCompany): Promise<void> {
    const dialogRef = this.dialog.open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
      data: {
        title: 'Archivar empresa cliente',
        message: `«${company.name}» dejará de aparecer en el listado. Sus datos no se borran.`,
        confirmText: 'Archivar',
      },
    });

    if (!(await firstValueFrom(dialogRef.afterClosed()))) {
      return;
    }

    try {
      await firstValueFrom(this.api.archive(company.id));
      this.snackBar.open(`«${company.name}» archivada.`, 'OK', { duration: 4000 });
    } catch {
      this.snackBar.open('No se ha podido archivar la empresa cliente.', 'OK');
    }
    // Also after an error: a 404 means someone else archived it, so the list is stale
    this.companies.reload();
  }
}
