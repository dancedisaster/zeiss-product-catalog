import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { LOW_STOCK_THRESHOLD, ProductFilters as Filters } from '../../models/product';

@Component({
  selector: 'app-product-filters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <search class="filters">
      <div class="filters__field filters__field--search">
        <label class="filters__label" for="filter-name">Search by name</label>
        <input
          id="filter-name"
          class="filters__input"
          type="search"
          placeholder="e.g. SmartLife"
          maxlength="100"
          autocomplete="off"
          [value]="filters().name"
          (input)="changed.emit({ name: text($event) })"
        />
      </div>
      <div class="filters__field">
        <label class="filters__label" for="filter-min">Min stock</label>
        <input
          id="filter-min"
          class="filters__input"
          type="number"
          min="0"
          inputmode="numeric"
          [value]="filters().minStock ?? ''"
          (input)="changed.emit({ minStock: number($event) })"
        />
      </div>
      <div class="filters__field">
        <label class="filters__label" for="filter-max">Max stock</label>
        <input
          id="filter-max"
          class="filters__input"
          type="number"
          min="0"
          inputmode="numeric"
          [value]="filters().maxStock ?? ''"
          (input)="changed.emit({ maxStock: number($event) })"
        />
      </div>
      <div class="filters__chips" role="group" aria-label="Quick filters">
        <button type="button" class="chip" (click)="changed.emit({ minStock: 1, maxStock: lowStock })">Low stock</button>
        <button type="button" class="chip" (click)="changed.emit({ minStock: 0, maxStock: 0 })">Out of stock</button>
        @if (active()) {
          <button type="button" class="chip chip--clear" (click)="cleared.emit()">Clear filters</button>
        }
      </div>
    </search>
  `,
})
export class ProductFilters {
  readonly filters = input.required<Filters>();
  readonly active = input(false);
  readonly changed = output<Partial<Filters>>();
  readonly cleared = output<void>();

  protected readonly lowStock = LOW_STOCK_THRESHOLD;

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected number(event: Event): number | null {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    return Number.isFinite(value) && value >= 0 ? Math.floor(value) : null;
  }
}
