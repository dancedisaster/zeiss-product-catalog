import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/** "Showing 11–20 of 42" with Previous / Next. The buttons only appear when there is more than one page. */
@Component({
  selector: 'app-paginator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="paginator" aria-label="Product pages">
      <p class="paginator__summary" aria-live="polite">
        @if (total() > 0) {
          Showing {{ first() }}–{{ last() }} of {{ total() }}
        } @else {
          No products
        }
      </p>
      @if (pageCount() > 1) {
        <div class="paginator__controls">
          <button type="button" class="button button--ghost" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)">
            Previous
          </button>
          <span class="paginator__position">Page {{ page() }} of {{ pageCount() }}</span>
          <button type="button" class="button button--ghost" [disabled]="page() >= pageCount()" (click)="pageChange.emit(page() + 1)">
            Next
          </button>
        </div>
      }
    </nav>
  `,
})
export class Paginator {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly first = computed(() => (this.page() - 1) * this.pageSize() + 1);
  protected readonly last = computed(() => Math.min(this.page() * this.pageSize(), this.total()));
}
