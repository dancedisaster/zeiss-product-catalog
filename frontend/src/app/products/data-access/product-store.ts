import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  firstValueFrom,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { ApiError, toApiError } from '../../core/http/api-error';
import {
  CreateProductRequest,
  EMPTY_FILTERS,
  ListParams,
  Product,
  ProductFilters,
  ProductPage,
  ProductSort,
  ProductSummary,
  UpdateProductRequest,
} from '../models/product';
import { ProductsApi } from './products-api';

/** Products per page of the list. */
export const PAGE_SIZE = 10;

type LoadStatus = 'loading' | 'loaded' | 'error';

interface ProductState {
  /** The current page. */
  products: readonly Product[];
  /** Matches across all pages. */
  total: number;
  status: LoadStatus;
  error: ApiError | null;
  lastUpdated: Date | null;
}

export type MutationResult<T = void> = { ok: true; value: T } | { ok: false; error: ApiError };

/**
 * Single source of truth for the product list.
 * - State lives in signals; components only read computed slices.
 * - The server filters, sorts and pages; the store holds one page and the total. Typing in the filters
 *   is debounced, page and sort changes load at once, and switchMap cancels stale requests, so a slow
 *   old response can never overwrite a newer one.
 * - Every mutation writes the server's response back into the state, so the UI always shows
 *   what the API actually stored (e.g. the real stock after a decrement). A list request already on its
 *   way when a mutation finishes is restarted, so its older snapshot can never undo the change.
 * - The KPI summary is catalogue-wide and comes from its own endpoint, reloaded with every page and
 *   after each stock change.
 * - When a refresh fails, the last data stays on screen and `stale` says so; it is never silently passed off as current.
 */
@Injectable({ providedIn: 'root' })
export class ProductStore {
  private readonly api = inject(ProductsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly typing$ = new Subject<void>();
  private readonly reload$ = new Subject<void>();
  private readonly summaryReload$ = new Subject<void>();
  private readonly state = signal<ProductState>({
    products: [],
    total: 0,
    status: 'loading',
    error: null,
    lastUpdated: null,
  });
  private readonly pageState = signal(1);
  private readonly sortState = signal<ProductSort | null>(null);
  private readonly summaryState = signal<ProductSummary | null>(null);
  private readonly summaryFailed = signal(false);
  /** True while a list request is on its way (the 'loading' status is also the initial state, before any request). */
  private loadInFlight = false;

  readonly pageSize = PAGE_SIZE;
  readonly filters = signal<ProductFilters>(EMPTY_FILTERS);
  readonly page = this.pageState.asReadonly();
  /** Null keeps the API's default order: by name, or by stock for a stock range. */
  readonly sort = this.sortState.asReadonly();
  /** Catalogue-wide counts; null until the first answer arrives. */
  readonly summary = this.summaryState.asReadonly();
  readonly products = computed(() => this.state().products);
  readonly total = computed(() => this.state().total);
  readonly status = computed(() => this.state().status);
  readonly error = computed(() => this.state().error);
  readonly lastUpdated = computed(() => this.state().lastUpdated);
  readonly hasActiveFilters = computed(() => {
    const f = this.filters();
    return f.name.trim() !== '' || f.minStock !== null || f.maxStock !== null;
  });
  /**
   * The last refresh of the page or of the summary failed, so what is on screen may be out of date.
   * (A failure with nothing to show yet is reported by the page's own error state instead.)
   */
  readonly stale = computed(
    () => (this.status() === 'error' && this.products().length > 0) || this.summaryFailed(),
  );

  constructor() {
    merge(this.typing$.pipe(debounceTime(300)), this.reload$)
      .pipe(
        // Read the filters, page and sort when the request goes out, not when the trigger fired.
        map(() => ({ filters: this.filters(), params: this.listParams() })),
        tap(() => {
          this.loadInFlight = true;
          this.state.update((s) => ({ ...s, status: 'loading' }));
          this.summaryReload$.next();
        }),
        switchMap(({ filters, params }) =>
          this.query(filters, params).pipe(
            map((page): ProductState => ({
              products: page.items,
              total: page.total,
              status: 'loaded',
              error: null,
              lastUpdated: new Date(),
            })),
            catchError((error: unknown) =>
              of<ProductState>({ ...this.state(), status: 'error', error: toApiError(error) }),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => {
        this.loadInFlight = false;
        this.state.set(state);
        // The current page can vanish (e.g. its last product was deleted elsewhere): show the last one that exists.
        const lastPage = Math.max(1, Math.ceil(state.total / PAGE_SIZE));
        if (state.status === 'loaded' && this.page() > lastPage) {
          this.goToPage(lastPage);
        }
      });

    this.summaryReload$
      .pipe(
        // If the summary fails, keep the last known figures but flag them as possibly out of date.
        switchMap(() => this.api.summary().pipe(catchError(() => of(null)))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((summary) => {
        this.summaryFailed.set(summary === null);
        if (summary) this.summaryState.set(summary);
      });
  }

  /** Filters start again from the first page; typing is debounced. */
  setFilters(filters: Partial<ProductFilters>): void {
    this.filters.update((current) => ({ ...current, ...filters }));
    this.pageState.set(1);
    this.typing$.next();
  }

  clearFilters(): void {
    this.filters.set(EMPTY_FILTERS);
    this.pageState.set(1);
    this.typing$.next();
  }

  /** A new order starts again from the first page. */
  setSort(sort: ProductSort | null): void {
    this.sortState.set(sort);
    this.pageState.set(1);
    this.refresh();
  }

  goToPage(page: number): void {
    this.pageState.set(Math.max(1, page));
    this.refresh();
  }

  refresh(): void {
    this.reload$.next();
  }

  async create(request: CreateProductRequest): Promise<MutationResult<Product>> {
    const result = await this.run(this.api.create(request));
    if (result.ok) this.refresh(); // the new product may or may not belong on the current page
    return result;
  }

  async update(id: number, request: UpdateProductRequest): Promise<MutationResult<Product>> {
    const result = await this.run(this.api.update(id, request));
    if (result.ok) {
      this.replace(result.value);
      this.afterMutation({ summaryChanged: false });
    }
    return result;
  }

  /** Optimistic: the row disappears at once and comes back if the API refuses. */
  async remove(product: Product): Promise<MutationResult> {
    const previous = this.state().products;
    this.patchProducts((products) => products.filter((p) => p.id !== product.id));

    const result = await this.run(this.api.delete(product.id));
    if (result.ok) {
      this.refresh(); // the next product moves up into this page, and the total and summary change
    } else {
      this.patchProducts(() => previous);
    }
    return result.ok ? { ok: true, value: undefined } : result;
  }

  async changeStock(product: Product, delta: number): Promise<MutationResult<Product>> {
    const request =
      delta > 0
        ? this.api.addToStock(product.id, delta)
        : this.api.decrementStock(product.id, -delta);
    const result = await this.run(request);

    if (result.ok) {
      this.replace(result.value);
      if (this.stockShapesTheList()) {
        // The product may now enter or leave the result, or move: page, order and total must come from the server.
        this.refresh();
      } else {
        // Name, price and category did not change, so neither did the result: the row keeps its place.
        this.afterMutation({ summaryChanged: true });
      }
    } else if (result.error.status === 404) {
      // Deleted elsewhere: drop it at once, then reload so the next product moves up and the total is right.
      this.patchProducts((products) => products.filter((p) => p.id !== product.id));
      this.refresh();
    } else if (result.error.status === 409) {
      this.refresh(); // someone else changed the stock: show the real value
    }
    return result;
  }

  /**
   * Runs once a mutation is applied locally. A list request already on its way may have read the data before the
   * mutation committed; its reply would undo the change. Restarting it lets switchMap drop that reply (the request is
   * aborted) and read again after the commit. A reload refreshes the summary too; otherwise refresh it alone if needed.
   * With no request in flight nothing is reloaded, so a stock change costs no extra list request.
   */
  private afterMutation({ summaryChanged }: { summaryChanged: boolean }): void {
    if (this.loadInFlight) {
      this.refresh();
    } else if (summaryChanged) {
      this.summaryReload$.next();
    }
  }

  /** A stock change can only change the result (which products, in what order) under a stock filter or a stock sort. */
  private stockShapesTheList(): boolean {
    const { minStock, maxStock } = this.filters();
    return minStock !== null || maxStock !== null || this.sort()?.column === 'stock';
  }

  private listParams(): ListParams {
    return { page: this.page(), pageSize: PAGE_SIZE, sort: this.sort() };
  }

  /** The API offers search and stock range as separate endpoints; search also accepts the range, so both can combine. */
  private query(filters: ProductFilters, params: ListParams): Observable<ProductPage> {
    const name = filters.name.trim();
    if (name) {
      return this.api.search(name, filters.minStock, filters.maxStock, params);
    }

    const hasRange = filters.minStock !== null || filters.maxStock !== null;
    return hasRange
      ? this.api.byStockLevel(filters.minStock, filters.maxStock, params)
      : this.api.list(params);
  }

  private replace(product: Product): void {
    this.patchProducts((products) => products.map((p) => (p.id === product.id ? product : p)));
  }

  private patchProducts(change: (products: readonly Product[]) => readonly Product[]): void {
    this.state.update((s) => ({ ...s, products: change(s.products) }));
  }

  private async run<T>(request: Observable<T>): Promise<MutationResult<T>> {
    try {
      return { ok: true, value: await firstValueFrom(request, { defaultValue: undefined as T }) };
    } catch (error) {
      return { ok: false, error: toApiError(error) };
    }
  }
}
