import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, debounceTime, firstValueFrom, map, merge, of, switchMap, tap } from 'rxjs';
import { ApiError, toApiError } from '../../core/http/api-error';
import {
  CreateProductRequest,
  EMPTY_FILTERS,
  LOW_STOCK_THRESHOLD,
  Product,
  ProductFilters,
  UpdateProductRequest,
} from '../models/product';
import { ProductsApi } from './products-api';

type LoadStatus = 'loading' | 'loaded' | 'error';

interface ProductState {
  products: readonly Product[];
  status: LoadStatus;
  error: ApiError | null;
  lastUpdated: Date | null;
}

export type MutationResult<T = void> = { ok: true; value: T } | { ok: false; error: ApiError };

/**
 * Single source of truth for the product list.
 * - State lives in signals; components only read computed slices.
 * - Filter changes flow through RxJS: debounced, and switchMap cancels stale requests,
 *   so a slow old response can never overwrite a newer one.
 * - Every mutation writes the server's response back into the state, so the UI always shows
 *   what the API actually stored (e.g. the real stock after a decrement).
 */
@Injectable({ providedIn: 'root' })
export class ProductStore {
  private readonly api = inject(ProductsApi);
  private readonly reload$ = new Subject<void>();
  private readonly state = signal<ProductState>({ products: [], status: 'loading', error: null, lastUpdated: null });

  readonly filters = signal<ProductFilters>(EMPTY_FILTERS);
  readonly products = computed(() => this.state().products);
  readonly status = computed(() => this.state().status);
  readonly error = computed(() => this.state().error);
  readonly lastUpdated = computed(() => this.state().lastUpdated);
  readonly hasActiveFilters = computed(() => {
    const f = this.filters();
    return f.name.trim() !== '' || f.minStock !== null || f.maxStock !== null;
  });
  readonly summary = computed(() => {
    const products = this.products();
    return {
      count: products.length,
      units: products.reduce((sum, p) => sum + p.stock, 0),
      lowStock: products.filter((p) => p.stock > 0 && p.stock <= LOW_STOCK_THRESHOLD).length,
      outOfStock: products.filter((p) => p.stock === 0).length,
    };
  });

  constructor() {
    const filterChanges$ = toObservable(this.filters).pipe(debounceTime(300));
    const reloads$ = this.reload$.pipe(map(() => this.filters()));

    merge(filterChanges$, reloads$)
      .pipe(
        tap(() => this.state.update((s) => ({ ...s, status: 'loading' }))),
        switchMap((filters) =>
          this.query(filters).pipe(
            map((products): ProductState => ({ products, status: 'loaded', error: null, lastUpdated: new Date() })),
            catchError((error: unknown) =>
              of<ProductState>({ ...this.state(), status: 'error', error: toApiError(error) }),
            ),
          ),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((state) => this.state.set(state));
  }

  setFilters(filters: Partial<ProductFilters>): void {
    this.filters.update((current) => ({ ...current, ...filters }));
  }

  clearFilters(): void {
    this.filters.set(EMPTY_FILTERS);
  }

  refresh(): void {
    this.reload$.next();
  }

  async create(request: CreateProductRequest): Promise<MutationResult<Product>> {
    const result = await this.run(this.api.create(request));
    if (result.ok) this.refresh(); // the new product may or may not match the current filters
    return result;
  }

  async update(id: number, request: UpdateProductRequest): Promise<MutationResult<Product>> {
    const result = await this.run(this.api.update(id, request));
    if (result.ok) this.replace(result.value);
    return result;
  }

  /** Optimistic: the row disappears at once and comes back if the API refuses. */
  async remove(product: Product): Promise<MutationResult> {
    const previous = this.state().products;
    this.patchProducts((products) => products.filter((p) => p.id !== product.id));

    const result = await this.run(this.api.delete(product.id));
    if (!result.ok) {
      this.patchProducts(() => previous);
    }
    return result.ok ? { ok: true, value: undefined } : result;
  }

  async changeStock(product: Product, delta: number): Promise<MutationResult<Product>> {
    const request = delta > 0 ? this.api.addToStock(product.id, delta) : this.api.decrementStock(product.id, -delta);
    const result = await this.run(request);

    if (result.ok) {
      this.replace(result.value);
    } else if (result.error.status === 404) {
      this.patchProducts((products) => products.filter((p) => p.id !== product.id));
    } else if (result.error.status === 409) {
      this.refresh(); // someone else changed the stock: show the real value
    }
    return result;
  }

  private query(filters: ProductFilters): Observable<Product[]> {
    const name = filters.name.trim();
    const hasRange = filters.minStock !== null || filters.maxStock !== null;

    if (name) {
      // The API offers search and stock range as separate endpoints; when both are set,
      // search on the server and narrow the (small) result by stock on the client.
      return this.api
        .search(name)
        .pipe(map((products) => (hasRange ? products.filter((p) => inRange(p.stock, filters)) : products)));
    }

    return hasRange ? this.api.byStockLevel(filters.minStock, filters.maxStock) : this.api.list();
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

function inRange(stock: number, filters: ProductFilters): boolean {
  return (filters.minStock === null || stock >= filters.minStock) && (filters.maxStock === null || stock <= filters.maxStock);
}
