import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Product, ProductSummary } from '../models/product';
import { PAGE_SIZE, ProductStore } from './product-store';

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
  version: '3f2b8c1e-0000-4000-8000-000000000001',
  ...overrides,
});

const summary: ProductSummary = {
  productCount: 12,
  unitsInStock: 3423,
  lowStock: 2,
  outOfStock: 1,
};

/** Lets the debounced filter input (300 ms) settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 350));

describe('ProductStore', () => {
  let store: ProductStore;
  let http: HttpTestingController;

  /** Answers the page request to `url`, with the total in the header. */
  function respondPage(url: string, items: Product[], total = items.length): TestRequest {
    const request = http.expectOne((r) => r.url === url);
    request.flush(items, { headers: { 'X-Total-Count': String(total) } });
    return request;
  }

  /**
   * Answers a refresh: the summary request, then the page request to `url`.
   * Summary first: answering the page can trigger the next load straight away, which would cancel a pending summary.
   */
  function respond(url: string, items: Product[], total = items.length): TestRequest {
    http.expectOne('/api/products/summary').flush(summary);
    return respondPage(url, items, total);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(ProductStore);
    http = TestBed.inject(HttpTestingController);

    store.refresh(); // the list page does this when it opens
    respond('/api/products', [product(), product({ id: 100002, stock: 0, sku: 'LNS-2' })], 12);
  });

  afterEach(() => http.verify());

  it('asks for the first page and keeps the page, the total and the catalogue summary', () => {
    store.refresh();
    const request = respond('/api/products', [product()], 12);

    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe(String(PAGE_SIZE));
    expect(request.request.params.has('sortBy')).toBe(false);
    expect(store.status()).toBe('loaded');
    expect(store.products().length).toBe(1);
    expect(store.total()).toBe(12);
    expect(store.summary()).toEqual(summary);
  });

  it('uses the search endpoint when a name is typed, with the stock range applied on the server', async () => {
    store.setFilters({ name: 'smart', minStock: 1, maxStock: 5 });
    await settle();

    const params = respondPage('/api/products/search', [product()]).request.params;
    expect(params.get('name')).toBe('smart');
    expect(params.get('min')).toBe('1');
    expect(params.get('max')).toBe('5');
  });

  it('uses the stock-level endpoint for a stock range', async () => {
    store.setFilters({ minStock: 0, maxStock: 5 });
    await settle();

    const params = respondPage('/api/products/stock-level', []).request.params;
    expect(params.get('min')).toBe('0');
    expect(params.get('max')).toBe('5');
  });

  it('starts again from page 1 when the filters change', async () => {
    store.goToPage(2);
    respondPage('/api/products', [product()], 12);

    store.setFilters({ name: 'smart' });
    await settle();

    expect(respondPage('/api/products/search', [product()]).request.params.get('page')).toBe('1');
    expect(store.page()).toBe(1);
  });

  it('asks the server for the chosen order and starts again from page 1', () => {
    store.goToPage(2);
    expect(respondPage('/api/products', [product()], 12).request.params.get('page')).toBe('2');

    store.setSort({ column: 'price', direction: 'desc' });

    const params = respondPage('/api/products', [product()], 12).request.params;
    expect(params.get('sortBy')).toBe('price');
    expect(params.get('sortDir')).toBe('desc');
    expect(params.get('page')).toBe('1');
  });

  it('leaves the catalogue summary alone when paging, sorting or filtering, which do not change it', async () => {
    store.goToPage(2);
    respondPage('/api/products', [product()], 12);
    store.setSort({ column: 'price', direction: 'asc' });
    respondPage('/api/products', [product()], 12);
    store.setFilters({ name: 'smart' });
    await settle();
    respondPage('/api/products/search', [product()]);

    http.expectNone('/api/products/summary');
    expect(store.summary()).toEqual(summary);
  });

  it('moves to the last page that exists when the current one comes back empty', () => {
    store.goToPage(3);
    respondPage('/api/products', [], 12); // 12 products fill only 2 pages of 10

    expect(respondPage('/api/products', [product()], 12).request.params.get('page')).toBe('2');
    expect(store.page()).toBe(2);
  });

  it('replaces a product with the server response after a stock change and refreshes the summary', async () => {
    const pending = store.changeStock(store.products()[0], -3);

    http
      .expectOne({ method: 'POST', url: '/api/products/100001/decrement-stock/3' })
      .flush(product({ stock: 7 }));
    const result = await pending;
    http.expectOne('/api/products/summary').flush({ ...summary, unitsInStock: 3420 });

    expect(result.ok).toBe(true);
    expect(store.products()[0].stock).toBe(7);
    expect(store.summary()?.unitsInStock).toBe(3420);
  });

  it('reloads the page after a stock change under a stock filter, because the product may leave it', async () => {
    store.setFilters({ minStock: 0, maxStock: 0 }); // "Out of stock"
    await settle();
    respondPage('/api/products/stock-level', [product({ stock: 0 })], 1);

    const pending = store.changeStock(store.products()[0], +1);
    http
      .expectOne({ method: 'POST', url: '/api/products/100001/add-to-stock/1' })
      .flush(product({ stock: 1 }));
    await pending;
    respond('/api/products/stock-level', [], 0);

    expect(store.products()).toEqual([]);
    expect(store.total()).toBe(0);
  });

  it('reloads the page after a stock change when sorted by stock, because the order changes', async () => {
    store.setSort({ column: 'stock', direction: 'asc' });
    respondPage('/api/products', [product({ stock: 10 })], 12);

    const pending = store.changeStock(store.products()[0], -3);
    http
      .expectOne({ method: 'POST', url: '/api/products/100001/decrement-stock/3' })
      .flush(product({ stock: 7 }));
    await pending;

    expect(respond('/api/products', [product({ stock: 7 })], 12).request.params.get('sortBy')).toBe(
      'stock',
    );
  });

  it('keeps the last page on screen when a refresh fails, but flags it as possibly out of date', () => {
    expect(store.stale()).toBe(false);

    store.refresh();
    http.expectOne('/api/products/summary').flush(summary);
    http
      .expectOne((r) => r.url === '/api/products')
      .flush({ detail: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(store.products().length).toBe(2);
    expect(store.stale()).toBe(true);

    store.refresh();
    respond('/api/products', [product()], 12);
    expect(store.stale()).toBe(false);
  });

  it('flags the figures as possibly out of date when only the summary fails, and keeps the last ones', () => {
    store.refresh();
    http
      .expectOne('/api/products/summary')
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    http
      .expectOne((r) => r.url === '/api/products')
      .flush([product()], { headers: { 'X-Total-Count': '12' } });

    expect(store.summary()).toEqual(summary);
    expect(store.stale()).toBe(true);
  });

  it('does not let a list reply that was already on its way undo a stock change', async () => {
    // A reload is under way (e.g. the 30 s auto-refresh); the server read stock 10 but the reply is slow.
    store.refresh();
    http.expectOne('/api/products/summary').flush(summary);
    const slowList = http.expectOne((r) => r.url === '/api/products');

    // Meanwhile the decrement is confirmed: 10 -> 7.
    const pending = store.changeStock(store.products()[0], -3);
    http
      .expectOne({ method: 'POST', url: '/api/products/100001/decrement-stock/3' })
      .flush(product({ stock: 7 }));
    await pending;

    expect(store.products()[0].stock).toBe(7);
    expect(slowList.cancelled).toBe(true); // its reply may predate the decrement, so it is dropped...
    respond('/api/products', [product({ stock: 7 })], 12); // ...and the page is read again after the commit
    expect(store.products()[0].stock).toBe(7);
  });

  it('does not let a list reply that was already on its way undo a saved edit', async () => {
    store.refresh();
    http.expectOne('/api/products/summary').flush(summary);
    const slowList = http.expectOne((r) => r.url === '/api/products');

    const pending = store.update(100001, {
      name: 'Renamed',
      description: null,
      sku: 'LNS-1',
      price: 289,
      categoryId: 1,
      version: 'v1',
    });
    http
      .expectOne({ method: 'PUT', url: '/api/products/100001' })
      .flush(product({ name: 'Renamed' }));
    await pending;

    expect(slowList.cancelled).toBe(true);
    respondPage('/api/products', [product({ name: 'Renamed' })], 12); // an edit leaves the summary as it is
    expect(store.products()[0].name).toBe('Renamed');
  });

  it('reloads the page when a stock change gets no answer, because it may have been applied', async () => {
    const pending = store.changeStock(store.products()[0], -3);

    http
      .expectOne({ method: 'POST', url: '/api/products/100001/decrement-stock/3' })
      .flush(null, { status: 504, statusText: 'Gateway Timeout' });
    const result = await pending;
    respond('/api/products', [product({ stock: 7 })], 12);

    expect(result.ok).toBe(false);
    expect(store.products()[0].stock).toBe(7);
  });

  it('removes a product optimistically, then reloads the page so the next one moves up', async () => {
    const target = store.products()[0];
    const pending = store.remove(target);
    expect(store.products().some((p) => p.id === target.id)).toBe(false);

    http
      .expectOne({ method: 'DELETE', url: '/api/products/100001' })
      .flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    respond('/api/products', [product({ id: 100002 }), product({ id: 100003 })], 11);

    expect(store.total()).toBe(11);
  });

  it('reloads a refused deletion and restores the product from the server', async () => {
    const target = store.products()[0];
    const pending = store.remove(target);

    http
      .expectOne({ method: 'DELETE', url: '/api/products/100001' })
      .flush({ detail: 'Too many requests' }, { status: 429, statusText: 'Too Many Requests' });
    const result = await pending;

    expect(result.ok).toBe(false);
    expect(store.products().some((p) => p.id === target.id)).toBe(false);
    respond('/api/products', [target, product({ id: 100002, stock: 0, sku: 'LNS-2' })], 12);
    expect(store.products().some((p) => p.id === target.id)).toBe(true);
  });

  it('does not resurrect another deleted product when an overlapping deletion receives 429', async () => {
    const [first, second] = store.products();
    const firstDelete = store.remove(first);
    const secondDelete = store.remove(second);

    http
      .expectOne({ method: 'DELETE', url: `/api/products/${second.id}` })
      .flush(null, { status: 204, statusText: 'No Content' });
    expect((await secondDelete).ok).toBe(true);
    respond('/api/products', [first], 11);

    http
      .expectOne({ method: 'DELETE', url: `/api/products/${first.id}` })
      .flush({ detail: 'Too many requests' }, { status: 429, statusText: 'Too Many Requests' });
    expect((await firstDelete).ok).toBe(false);

    // A rollback of the first deletion's snapshot would bring the second product back here.
    expect(store.products().some((p) => p.id === second.id)).toBe(false);
    respond('/api/products', [first], 11);
    expect(store.products()).toEqual([first]);
    expect(store.total()).toBe(11);
  });

  it('does not restore a product that was already deleted elsewhere', async () => {
    const target = store.products()[0];
    const pending = store.remove(target);

    http
      .expectOne({ method: 'DELETE', url: '/api/products/100001' })
      .flush({ detail: 'Product 100001 was not found.' }, { status: 404, statusText: 'Not Found' });
    await pending;
    respond('/api/products', [product({ id: 100002 })], 11);

    expect(store.products().some((p) => p.id === target.id)).toBe(false);
    expect(store.total()).toBe(11);
  });

  it('reloads the page instead of restoring a product when the delete gets a server error', async () => {
    const target = store.products()[0];
    const pending = store.remove(target);

    http
      .expectOne({ method: 'DELETE', url: '/api/products/100001' })
      .flush({ detail: 'Server error' }, { status: 500, statusText: 'Server Error' });
    const result = await pending;
    expect(store.products().some((p) => p.id === target.id)).toBe(false); // not put back on trust...

    respond('/api/products', [product({ id: 100002 })], 11); // ...the server says it is gone
    expect(result.ok).toBe(false);
    expect(store.products().some((p) => p.id === target.id)).toBe(false);
  });
});
