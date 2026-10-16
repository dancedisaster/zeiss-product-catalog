import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Product, ProductSummary } from '../../models/product';
import { ProductListPage } from './product-list-page';

const product: Product = {
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
  version: 'v1',
};
const summary: ProductSummary = { productCount: 1, unitsInStock: 10, lowStock: 1, outOfStock: 0 };

describe('ProductListPage', () => {
  it('warns that the data may be out of date when a refresh fails, keeps it visible, and can try again', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProductListPage);
    const page = fixture.nativeElement as HTMLElement;
    const notice = () => page.querySelector('.notice');
    const answer = () => {
      http.expectOne('/api/products/summary').flush(summary);
      http
        .expectOne((r) => r.url === '/api/products')
        .flush([product], { headers: { 'X-Total-Count': '1' } });
      fixture.detectChanges();
    };

    answer(); // the page loads when it opens
    expect(notice()).toBeNull();

    // The next refresh cannot reach the server.
    page.querySelector<HTMLButtonElement>('.page__actions button')!.click();
    http.expectOne('/api/products/summary').flush(summary);
    http.expectOne((r) => r.url === '/api/products').error(new ProgressEvent('error'));
    fixture.detectChanges();

    expect(page.textContent).toContain('SmartLife Progressive');
    expect(notice()?.textContent).toContain('This may be out of date.');
    expect(notice()?.textContent).toContain('Cannot reach the server');

    notice()!.querySelector('button')!.click(); // Try again
    answer();
    expect(notice()).toBeNull();
    http.verify();
  });
});
