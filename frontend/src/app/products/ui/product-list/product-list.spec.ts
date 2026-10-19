import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Product, ProductSort } from '../../models/product';
import { ProductList } from './product-list';

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
  version: '3f2b8c1e-0000-4000-8000-000000000001',
};

describe('ProductList sorting', () => {
  function render(sort: ProductSort | null) {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(ProductList);
    fixture.componentRef.setInput('products', [product]);
    fixture.componentRef.setInput('sort', sort);
    const emitted: (ProductSort | null)[] = [];
    fixture.componentInstance.sortChange.subscribe((next) => emitted.push(next));
    fixture.detectChanges();
    return { element: fixture.nativeElement as HTMLElement, emitted };
  }

  const header = (element: HTMLElement, label: string) =>
    [...element.querySelectorAll<HTMLElement>('[role="columnheader"]')].find((h) =>
      h.textContent?.includes(label),
    )!;

  it('sorts a new column ascending first', () => {
    const { element, emitted } = render(null);

    header(element, 'Price').querySelector('button')!.click();

    expect(emitted).toEqual([{ column: 'price', direction: 'asc' }]);
  });

  it('marks only the sorted column with aria-sort and flips it on the next click', () => {
    const { element, emitted } = render({ column: 'price', direction: 'asc' });

    expect(header(element, 'Price').getAttribute('aria-sort')).toBe('ascending');
    expect(header(element, 'Stock').hasAttribute('aria-sort')).toBe(false);

    header(element, 'Price').querySelector('button')!.click();
    expect(emitted).toEqual([{ column: 'price', direction: 'desc' }]);
  });

  it('offers the same choices in the small-screen menu, including back to the default order', () => {
    const { element, emitted } = render(null);
    const menu = element.querySelector<HTMLSelectElement>('#product-sort')!;

    menu.value = 'stock:desc';
    menu.dispatchEvent(new Event('change'));
    menu.value = '';
    menu.dispatchEvent(new Event('change'));

    expect(emitted).toEqual([{ column: 'stock', direction: 'desc' }, null]);
  });
});
