import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

/** Quantity box with remove/add buttons. Emits a signed delta; the server stays the authority. */
@Component({
  selector: 'app-stock-adjuster',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stock-adjuster" role="group" [attr.aria-label]="'Adjust stock of ' + productName()">
      <button
        type="button"
        class="stock-adjuster__button"
        [disabled]="busy() || !canRemove()"
        (click)="emit(-1)"
        [attr.aria-label]="'Remove ' + quantity() + ' from stock'"
      >−</button>
      <input
        class="stock-adjuster__input"
        type="number"
        inputmode="numeric"
        min="1"
        [value]="quantity()"
        (input)="onQuantity($event)"
        [attr.aria-label]="'Quantity for ' + productName()"
      />
      <button
        type="button"
        class="stock-adjuster__button"
        [disabled]="busy() || !validQuantity()"
        (click)="emit(1)"
        [attr.aria-label]="'Add ' + quantity() + ' to stock'"
      >+</button>
    </div>
  `,
})
export class StockAdjuster {
  readonly stock = input.required<number>();
  readonly productName = input.required<string>();
  readonly busy = input(false);
  readonly changeStock = output<number>();

  protected readonly quantity = signal(1);
  protected readonly validQuantity = computed(() => Number.isInteger(this.quantity()) && this.quantity() > 0);
  protected readonly canRemove = computed(() => this.validQuantity() && this.quantity() <= this.stock());

  protected onQuantity(event: Event): void {
    this.quantity.set((event.target as HTMLInputElement).valueAsNumber);
  }

  protected emit(direction: 1 | -1): void {
    if (this.validQuantity()) {
      this.changeStock.emit(direction * this.quantity());
    }
  }
}
