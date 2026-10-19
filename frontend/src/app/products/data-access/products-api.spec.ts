import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { EMPTY_FILTERS } from '../models/product';
import { ProductsApi } from './products-api';

describe('ProductsApi.exportUrl', () => {
  function api(): ProductsApi {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    return TestBed.inject(ProductsApi);
  }

  it('exports the whole catalogue when nothing is filtered or sorted', () => {
    expect(api().exportUrl(EMPTY_FILTERS, null)).toBe('/api/products/export');
  });

  it('carries the current filters and order, but never a page', () => {
    const url = api().exportUrl(
      { name: '  smart ', minStock: 1, maxStock: 10 },
      { column: 'price', direction: 'desc' },
    );

    expect(url).toBe('/api/products/export?min=1&max=10&name=smart&sortBy=price&sortDir=desc');
  });
});
