import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Product } from '../models/product';
import { ProductStore } from './product-store';

const product = (overrides: Partial<Product> = {}): Product => ({
  id: 100001,
  name: 'SmartLife Progressive',
  description: null,
  sku: 'LNS-1',
  price: 289,
  stock: 10,
  categoryId: 1,
  categoryName: 'Spectacle Lenses',
  createdAt: '2026-10-15T09:00:00Z',
  updatedAt: '2026-10-15T09:00:00Z',
  ...overrides,
});

/** Lets the debounced filter stream (300 ms) settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 350));

describe('ProductStore', () => {
  let store: ProductStore;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(ProductStore);
    http = TestBed.inject(HttpTestingController);

    // Initial load triggered by the (debounced) initial filters.
    TestBed.tick();
    await settle();
    http.expectOne('/api/products').flush([product(), product({ id: 100002, stock: 0, sku: 'LNS-2' })]);
  });

  afterEach(() => http.verify());

  it('exposes loaded products and a computed summary', () => {
    expect(store.status()).toBe('loaded');
    expect(store.products().length).toBe(2);
    expect(store.summary()).toEqual({ count: 2, units: 10, lowStock: 1, outOfStock: 1 });
  });

  it('uses the search endpoint when a name is typed', async () => {
    store.setFilters({ name: 'smart' });
    TestBed.tick();
    await settle();

    http.expectOne((req) => req.url === '/api/products/search' && req.params.get('name') === 'smart').flush([product()]);
    expect(store.products().length).toBe(1);
  });

  it('uses the stock-level endpoint for a stock range', async () => {
    store.setFilters({ minStock: 0, maxStock: 5 });
    TestBed.tick();
    await settle();

    const req = http.expectOne((r) => r.url === '/api/products/stock-level');
    expect(req.request.params.get('min')).toBe('0');
    expect(req.request.params.get('max')).toBe('5');
    req.flush([]);
  });

  it('replaces a product with the server response after a stock change', async () => {
    const pending = store.changeStock(store.products()[0], -3);

    http.expectOne({ method: 'POST', url: '/api/products/100001/decrement-stock/3' }).flush(product({ stock: 7 }));
    const result = await pending;

    expect(result.ok).toBe(true);
    expect(store.products()[0].stock).toBe(7);
  });

  it('removes a product optimistically and restores it when the API refuses', async () => {
    const target = store.products()[0];
    const pending = store.remove(target);

    expect(store.products().some((p) => p.id === target.id)).toBe(false);

    http.expectOne({ method: 'DELETE', url: '/api/products/100001' }).flush(
      { detail: 'Server error' },
      { status: 500, statusText: 'Server Error' },
    );
    const result = await pending;

    expect(result.ok).toBe(false);
    expect(store.products().some((p) => p.id === target.id)).toBe(true);
  });
});
