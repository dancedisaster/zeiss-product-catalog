import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product } from '../../models/product';
import { StockAdjuster } from '../stock-adjuster/stock-adjuster';
import { StockBadge } from '../stock-badge/stock-badge';

export interface StockChange {
  product: Product;
  delta: number;
}

/** Presentational list: renders products and reports user intent. Cards on mobile, a table-like grid on wider screens. */
@Component({
  selector: 'app-product-list',
  imports: [CurrencyPipe, RouterLink, StockAdjuster, StockBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="product-list" role="table" aria-label="Products">
      <div class="product-list__head" role="row">
        <span role="columnheader">Product</span>
        <span role="columnheader">Category</span>
        <span role="columnheader" class="product-list__numeric">Price</span>
        <span role="columnheader">Stock</span>
        <span role="columnheader">Adjust</span>
        <span role="columnheader"><span class="visually-hidden">Actions</span></span>
      </div>
      @for (product of products(); track product.id) {
        <div class="product-list__row" role="row" [class.product-list__row--busy]="busyIds().has(product.id)">
          <div class="product-list__cell product-list__cell--name" role="cell">
            <span class="product-list__name">{{ product.name }}</span>
            <span class="product-list__meta">#{{ product.id }} · {{ product.sku }}</span>
          </div>
          <div class="product-list__cell" role="cell">{{ product.categoryName }}</div>
          <div class="product-list__cell product-list__numeric" role="cell">{{ product.price | currency: 'EUR' }}</div>
          <div class="product-list__cell" role="cell"><app-stock-badge [stock]="product.stock" /></div>
          <div class="product-list__cell" role="cell">
            <app-stock-adjuster
              [stock]="product.stock"
              [productName]="product.name"
              [busy]="busyIds().has(product.id)"
              (changeStock)="stockChange.emit({ product, delta: $event })"
            />
          </div>
          <div class="product-list__cell product-list__actions" role="cell">
            <a class="button button--ghost" [routerLink]="['/products', product.id, 'edit']">Edit</a>
            @if (confirmingId() === product.id) {
              <button type="button" class="button button--danger" (click)="confirmDelete(product)">Confirm</button>
              <button type="button" class="button button--ghost" (click)="confirmingId.set(null)">Cancel</button>
            } @else {
              <button type="button" class="button button--ghost button--danger-text" (click)="confirmingId.set(product.id)">
                Delete
              </button>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class ProductList {
  readonly products = input.required<readonly Product[]>();
  readonly busyIds = input<ReadonlySet<number>>(new Set());
  readonly stockChange = output<StockChange>();
  readonly delete = output<Product>();

  /** Two-step delete instead of a blocking browser confirm(). */
  protected readonly confirmingId = signal<number | null>(null);

  protected confirmDelete(product: Product): void {
    this.confirmingId.set(null);
    this.delete.emit(product);
  }
}
