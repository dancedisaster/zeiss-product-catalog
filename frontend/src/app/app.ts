import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { ToastContainer } from './core/notifications/toast-container';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ToastContainer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="app-header">
      <a class="app-header__brand" routerLink="/products">
        <span class="app-header__logo" aria-hidden="true">Z</span>
        <span class="app-header__title">Product Catalog</span>
      </a>
    </header>
    <main class="app-main">
      <router-outlet />
    </main>
    <app-toast-container />
  `,
})
export class App {}
