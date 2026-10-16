import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Product } from '../../models/product';
import { ProductFormPage } from './product-form-page';

const loaded: Product = {
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
  version: 'version-loaded',
};

/** Lets the async submission action reach its next step. */
const settle = () => new Promise((resolve) => setTimeout(resolve));

describe('ProductFormPage (edit)', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ProductFormPage>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'products', children: [] }])],
    });
    http = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(ProductFormPage);
    fixture.componentRef.setInput('id', String(loaded.id));
    TestBed.tick();
    http.expectOne('/api/categories').flush([{ id: 1, name: 'Spectacle Lenses' }]);
    http.expectOne(`/api/products/${loaded.id}`).flush(loaded);
    await settle();
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  async function submit(): Promise<void> {
    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  const alert = () => (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');

  it('sends the version it loaded, and no stock', async () => {
    await submit();

    const request = http.expectOne({ method: 'PUT', url: `/api/products/${loaded.id}` });
    expect(request.request.body).toEqual({
      name: 'SmartLife Progressive',
      description: null,
      sku: 'LNS-1',
      price: 289,
      categoryId: 1,
      version: 'version-loaded',
    });
    request.flush({ ...loaded, version: 'version-next' });
  });

  it('explains a conflict and can load the version someone else saved', async () => {
    await submit();
    http
      .expectOne({ method: 'PUT', url: `/api/products/${loaded.id}` })
      .flush(
        { code: 'product.concurrent_update', detail: 'Product 100001 was changed by someone else.' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();
    fixture.detectChanges();

    expect(alert()?.textContent).toContain('Someone else saved this product');

    alert()!.querySelector('button')!.click();
    TestBed.tick();
    http.expectOne(`/api/products/${loaded.id}`).flush({ ...loaded, name: 'Renamed by someone else', version: 'version-other' });
    await settle();
    fixture.detectChanges();

    expect(alert()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#name')!.value).toBe('Renamed by someone else');
  });
});
