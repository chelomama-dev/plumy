import { httpResource } from '@angular/common/http';
import { Component, input, linkedSignal, model, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { PagedResult } from '../../shared/paged-result';
import { ClientCompany, clientCompaniesUrl } from '../client-company';

export const companySearchDebounceMs = 300;

// Legal forms say nothing about which company it is: "Acme S.L." is A, not AS
const legalForms = new Set(['SL', 'SA', 'SLU', 'SAU', 'SC', 'CB', 'SCOOP', 'SLL', 'SLP']);

/** Up to two letters for a company's badge: the first letters of its first words. */
export function companyInitials(name: string): string {
  return name
    .split(/\s+/)
    .filter(
      (word) => /^\p{L}/u.test(word) && !legalForms.has(word.replace(/\./g, '').toUpperCase()),
    )
    .slice(0, 2)
    .map((word) => word[0].toUpperCase())
    .join('');
}

/**
 * Picks a client company by typing its name or NIF: searched against the API, since a gestoría can have hundreds.
 * The chosen company is the two-way `company` binding; typing again clears it.
 */
@Component({
  selector: 'app-client-company-picker',
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
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

  protected readonly initials = companyInitials;

  /** Empties the field and the choice, and leaves the cursor there to look for another one. */
  protected clear(input: HTMLInputElement): void {
    input.value = '';
    this.onCompanyInput('');
    input.focus();
  }

  protected onCompanyInput(text: string): void {
    this.companySearch.set(text);
    // Typing again means looking for another company: the previous choice no longer matches the text
    this.company.set(undefined);
  }
}
