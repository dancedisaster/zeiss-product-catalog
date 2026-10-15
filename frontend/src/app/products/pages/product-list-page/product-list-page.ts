import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { filter, fromEvent, interval, merge } from 'rxjs';
import { NotificationService } from '../../../core/notifications/notification.service';
import { ProductStore } from '../../data-access/product-store';
import { Product } from '../../models/product';
import { ProductFilters } from '../../ui/product-filters/product-filters';
import { ProductList, StockChange } from '../../ui/product-list/product-list';

const AUTO_REFRESH_MS = 30_000;

@Component({
  selector: 'app-product-list-page',
  imports: [DatePipe, DecimalPipe, RouterLink, ProductFilters, ProductList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-list-page.html',
})
export class ProductListPage {
  protected readonly store = inject(ProductStore);
  private readonly notifications = inject(NotificationService);
  protected readonly busyIds = signal<ReadonlySet<number>>(new Set());

  constructor() {
    this.store.refresh();

    // Reflect changes made by others through the API: refresh when the tab becomes visible again
    // and periodically while it is visible. (Server push with SignalR would be the next step.)
    const becameVisible$ = fromEvent(document, 'visibilitychange').pipe(filter(() => !document.hidden));
    const tick$ = interval(AUTO_REFRESH_MS).pipe(filter(() => !document.hidden));
    merge(becameVisible$, tick$)
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.store.refresh());
  }

  protected async onStockChange({ product, delta }: StockChange): Promise<void> {
    this.setBusy(product.id, true);
    const result = await this.store.changeStock(product, delta);
    this.setBusy(product.id, false);

    if (result.ok) {
      const verb = delta > 0 ? 'Added' : 'Removed';
      this.notifications.success(`${verb} ${Math.abs(delta)} · ${product.name} now has ${result.value.stock} in stock.`);
    } else {
      this.notifications.error(result.error.message);
    }
  }

  protected async onDelete(product: Product): Promise<void> {
    const result = await this.store.remove(product);
    if (result.ok) {
      this.notifications.success(`Deleted ${product.name}.`);
    } else {
      this.notifications.error(result.error.message);
    }
  }

  private setBusy(id: number, busy: boolean): void {
    this.busyIds.update((ids) => {
      const next = new Set(ids);
      if (busy) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }
}
