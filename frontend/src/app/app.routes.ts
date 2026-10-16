import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    title: 'Products · ZEISS Product Catalog',
    loadComponent: () =>
      import('./products/pages/product-list-page/product-list-page').then((m) => m.ProductListPage),
  },
  {
    path: 'products/new',
    title: 'New product · ZEISS Product Catalog',
    loadComponent: () =>
      import('./products/pages/product-form-page/product-form-page').then((m) => m.ProductFormPage),
  },
  {
    path: 'products/:id/edit',
    title: 'Edit product · ZEISS Product Catalog',
    loadComponent: () =>
      import('./products/pages/product-form-page/product-form-page').then((m) => m.ProductFormPage),
  },
  { path: '**', redirectTo: 'products' },
];
