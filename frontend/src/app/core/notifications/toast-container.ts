import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NotificationService } from './notification.service';

@Component({
  selector: 'app-toast-container',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toasts" aria-live="polite" aria-atomic="false">
      @for (toast of notifications.toasts(); track toast.id) {
        <div
          class="toasts__item"
          [class]="'toasts__item--' + toast.kind"
          [attr.role]="toast.kind === 'error' ? 'alert' : 'status'"
        >
          <span class="toasts__message">{{ toast.message }}</span>
          <button
            type="button"
            class="toasts__close"
            (click)="notifications.dismiss(toast.id)"
            aria-label="Dismiss notification"
          >
            ×
          </button>
        </div>
      }
    </div>
  `,
})
export class ToastContainer {
  protected readonly notifications = inject(NotificationService);
}
