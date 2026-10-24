import { httpResource } from '@angular/common/http';
import { Component, input, linkedSignal, model, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { PagedResult } from '../../shared/paged-result';
import { ClientCompany, clientCompaniesUrl } from '../client-company';

export const companySearchDebounceMs = 300;

/**
 * Picks a client company by typing its name or NIF: searched against the API, since a gestoría can have hundreds.
 * The chosen company is the two-way `company` binding; typing again clears it.
 */
@Component({
  selector: 'app-client-company-picker',
  imports: [MatAutocompleteModule, MatFormFieldModule, MatInputModule],
  templateUrl: './client-company-picker.html',
  styleUrl: './client-company-picker.scss',
})
export class ClientCompanyPicker {
  readonly company = model<ClientCompany | undefined>(undefined);
  readonly disabled = input(false);
  readonly label = input('Empresa cliente');
  readonly hint = input('Busca por nombre o NIF.');

  protected readonly companySearch = signal('');

  private readonly debouncedSearch = toSignal(
    toObservable(this.companySearch).pipe(
      map((text) => text.trim()),
      debounceTime(companySearchDebounceMs),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  protected readonly companies = httpResource<PagedResult<ClientCompany>>(() => ({
    url: clientCompaniesUrl,
    params: {
      page: 1,
      pageSize: 10,
      ...(this.debouncedSearch() ? { search: this.debouncedSearch() } : {}),
    },
  }));

  /** Keeps the previous options while a new search loads, so the panel doesn't flicker shut on every keystroke. */
  protected readonly companyOptions = linkedSignal<ClientCompany[] | undefined, ClientCompany[]>({
    source: () => (this.companies.hasValue() ? this.companies.value().items : undefined),
    computation: (current, previous) => current ?? previous?.value ?? [],
  });

  /** What the input shows once an option is picked. While typing it receives the text itself. */
  protected readonly displayCompany = (value: ClientCompany | string | null) =>
    typeof value === 'string' ? value : (value?.name ?? '');

  protected onCompanyInput(text: string): void {
    this.companySearch.set(text);
    // Typing again means looking for another company: the previous choice no longer matches the text
    this.company.set(undefined);
  }
}
