import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { LOW_STOCK_THRESHOLD } from '../../models/product';

type StockLevel = 'out' | 'low' | 'ok';

@Component({
  selector: 'app-stock-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="stock-badge" [class]="'stock-badge--' + level()">{{ label() }}</span>`,
})
export class StockBadge {
  readonly stock = input.required<number>();

  protected readonly level = computed<StockLevel>(() => {
    const stock = this.stock();
    if (stock === 0) return 'out';
    return stock <= LOW_STOCK_THRESHOLD ? 'low' : 'ok';
  });

  protected readonly label = computed(() => {
    switch (this.level()) {
      case 'out':
        return 'Out of stock';
      case 'low':
        return `Low · ${this.stock()}`;
      default:
        return `${this.stock()} in stock`;
    }
  });
}
