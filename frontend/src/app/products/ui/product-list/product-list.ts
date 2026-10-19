import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product, ProductSort, ProductSortColumn } from '../../models/product';
import { StockAdjuster } from '../stock-adjuster/stock-adjuster';
import { StockBadge } from '../stock-badge/stock-badge';

export interface StockChange {
  product: Product;
  delta: number;
}

const SORTABLE_COLUMNS: readonly { column: ProductSortColumn; label: string; numeric?: boolean }[] =
  [
    { column: 'name', label: 'Product' },
    { column: 'categoryName', label: 'Category' },
    { column: 'price', label: 'Price', numeric: true },
    { column: 'stock', label: 'Stock' },
  ];

/** Choices for the small-screen "Sort by" menu, where the column headers are hidden. */
const SORT_OPTIONS: readonly { value: string; label: string }[] = [
  { value: 'name:asc', label: 'Name (A–Z)' },
  { value: 'name:desc', label: 'Name (Z–A)' },
  { value: 'categoryName:asc', label: 'Category (A–Z)' },
  { value: 'categoryName:desc', label: 'Category (Z–A)' },
  { value: 'price:asc', label: 'Price (low to high)' },
  { value: 'price:desc', label: 'Price (high to low)' },
  { value: 'stock:asc', label: 'Stock (low to high)' },
  { value: 'stock:desc', label: 'Stock (high to low)' },
];

/**
 * Presentational list: renders products and reports user intent. Cards on mobile, a table-like grid on wider screens.
 * Column headers sort on wide screens; a "Sort by" menu does the same on small ones.
 */
@Component({
  selector: 'app-product-list',
  imports: [CurrencyPipe, RouterLink, StockAdjuster, StockBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="product-sort">
      <label class="product-sort__label" for="product-sort">Sort by</label>
      <select
        id="product-sort"
        class="filters__input product-sort__select"
        (change)="onSortSelected($event)"
      >
        <option value="" [selected]="sortValue() === ''">Default</option>
        @for (option of sortOptions; track option.value) {
          <option [value]="option.value" [selected]="sortValue() === option.value">
            {{ option.label }}
          </option>
        }
      </select>
    </div>

    <div class="product-list" role="table" aria-label="Products">
      <div class="product-list__head" role="row">
        @for (header of sortableColumns; track header.column) {
          <span
            role="columnheader"
            [class.product-list__numeric]="header.numeric"
            [attr.aria-sort]="ariaSort(header.column)"
          >
            <button type="button" class="product-list__sort-button" (click)="sortBy(header.column)">
              {{ header.label }}
              <span
                class="product-list__sort-icon"
                [class.product-list__sort-icon--active]="sort()?.column === header.column"
                aria-hidden="true"
              >
                {{ sortIcon(header.column) }}
              </span>
            </button>
          </span>
        }
        <span role="columnheader">Adjust</span>
        <span role="columnheader"><span class="visually-hidden">Actions</span></span>
      </div>
      @for (product of products(); track product.id) {
        <div
          class="product-list__row"
          role="row"
          [class.product-list__row--busy]="busyIds().has(product.id)"
        >
          <div class="product-list__cell product-list__cell--name" role="cell">
            <span class="product-list__name">{{ product.name }}</span>
            <span class="product-list__meta">#{{ product.id }} · {{ product.sku }}</span>
          </div>
          <div class="product-list__cell" role="cell">{{ product.categoryName }}</div>
          <div class="product-list__cell product-list__numeric" role="cell">
            {{ product.price | currency: 'EUR' }}
          </div>
          <div class="product-list__cell" role="cell">
            <app-stock-badge [stock]="product.stock" />
          </div>
          <div class="product-list__cell" role="cell">
            <app-stock-adjuster
              [stock]="product.stock"
              [productName]="product.name"
              [busy]="busyIds().has(product.id)"
              (changeStock)="stockChange.emit({ product, delta: $event })"
            />
          </div>
          <div class="product-list__cell product-list__actions" role="cell">
            <a class="button button--ghost" [routerLink]="['/products', product.id, 'edit']"
              >Edit</a
            >
            @if (confirmingId() === product.id) {
              <button type="button" class="button button--danger" (click)="confirmDelete(product)">
                Confirm
              </button>
              <button type="button" class="button button--ghost" (click)="confirmingId.set(null)">
                Cancel
              </button>
            } @else {
              <button
                type="button"
                class="button button--ghost button--danger-text"
                (click)="confirmingId.set(product.id)"
              >
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
  /** The current order; null means the order the API returned. */
  readonly sort = input<ProductSort | null>(null);
  readonly stockChange = output<StockChange>();
  readonly delete = output<Product>();
  readonly sortChange = output<ProductSort | null>();

  protected readonly sortableColumns = SORTABLE_COLUMNS;
  protected readonly sortOptions = SORT_OPTIONS;
  protected readonly sortValue = computed(() => {
    const sort = this.sort();
    return sort ? `${sort.column}:${sort.direction}` : '';
  });

  /** Two-step delete instead of a blocking browser confirm(). */
  protected readonly confirmingId = signal<number | null>(null);

  protected confirmDelete(product: Product): void {
    this.confirmingId.set(null);
    this.delete.emit(product);
  }

  /** First click sorts ascending, the next one flips the direction. */
  protected sortBy(column: ProductSortColumn): void {
    const current = this.sort();
    const direction = current?.column === column && current.direction === 'asc' ? 'desc' : 'asc';
    this.sortChange.emit({ column, direction });
  }

  protected onSortSelected(event: Event): void {
    const [column, direction] = (event.target as HTMLSelectElement).value.split(':');
    this.sortChange.emit(column ? ({ column, direction } as ProductSort) : null);
  }

  /** Only the sorted column carries aria-sort, as the ARIA sortable-table pattern asks. */
  protected ariaSort(column: ProductSortColumn): 'ascending' | 'descending' | null {
    const sort = this.sort();
    if (sort?.column !== column) return null;
    return sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIcon(column: ProductSortColumn): string {
    const sort = this.sort();
    if (sort?.column !== column) return '↕';
    return sort.direction === 'asc' ? '↑' : '↓';
  }
}
