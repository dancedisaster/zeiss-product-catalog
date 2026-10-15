import { TestBed } from '@angular/core/testing';
import { StockBadge } from './stock-badge';

describe('StockBadge', () => {
  function render(stock: number): HTMLElement {
    const fixture = TestBed.createComponent(StockBadge);
    fixture.componentRef.setInput('stock', stock);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('.stock-badge');
  }

  it.each([
    [0, 'Out of stock', 'stock-badge--out'],
    [3, 'Low · 3', 'stock-badge--low'],
    [10, 'Low · 10', 'stock-badge--low'],
    [11, '11 in stock', 'stock-badge--ok'],
  ])('stock %i shows "%s"', (stock, label, modifier) => {
    const badge = render(stock);

    expect(badge.textContent).toBe(label);
    expect(badge.classList).toContain(modifier);
  });
});
