import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal } from '@angular/core';
import { FormField, FormRoot, form, max, maxLength, min, pattern, required, schema } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { NotificationService } from '../../../core/notifications/notification.service';
import { ProductStore } from '../../data-access/product-store';
import { Category, CreateProductRequest, Product } from '../../models/product';

interface ProductFormModel {
  name: string;
  description: string;
  sku: string;
  price: number;
  stock: number;
  categoryId: string;
}

const EMPTY_MODEL: ProductFormModel = { name: '', description: '', sku: '', price: 0, stock: 0, categoryId: '' };

/** Client-side rules mirror the API validators for fast feedback; the API remains the authority. */
const productSchema = schema<ProductFormModel>((p) => {
  required(p.name, { message: 'Name is required.' });
  maxLength(p.name, 100, { message: 'Name must be at most 100 characters.' });
  maxLength(p.description, 1000, { message: 'Description must be at most 1000 characters.' });
  required(p.sku, { message: 'SKU is required.' });
  maxLength(p.sku, 32, { message: 'SKU must be at most 32 characters.' });
  pattern(p.sku, /^[A-Za-z0-9-]+$/, { message: 'Use only letters, digits and dashes.' });
  min(p.price, 0.01, { message: 'Price must be greater than zero.' });
  max(p.price, 1_000_000, { message: 'Price must be at most 1,000,000.' });
  min(p.stock, 0, { message: 'Stock cannot be negative.' });
  required(p.categoryId, { message: 'Choose a category.' });
});

@Component({
  selector: 'app-product-form-page',
  imports: [FormField, FormRoot, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-form-page.html',
})
export class ProductFormPage {
  private readonly store = inject(ProductStore);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Bound from the :id route parameter; absent on /products/new. */
  readonly id = input<string>();

  protected readonly isEdit = computed(() => this.id() !== undefined);

  protected readonly categories = httpResource<Category[]>(() => '/api/categories', { defaultValue: [] });
  protected readonly product = httpResource<Product>(() => (this.id() ? `/api/products/${this.id()}` : undefined));

  /** Writable form model that resets whenever a (different) product is loaded. */
  protected readonly model = linkedSignal<ProductFormModel>(() => {
    const product = this.product.value();
    return product
      ? {
          name: product.name,
          description: product.description ?? '',
          sku: product.sku,
          price: product.price,
          stock: product.stock,
          categoryId: String(product.categoryId),
        }
      : EMPTY_MODEL;
  });

  protected readonly productForm = form(this.model, productSchema, {
    submission: {
      action: async (field) => {
        const value = field().value();
        const request: CreateProductRequest = {
          name: value.name.trim(),
          description: value.description.trim() || null,
          sku: value.sku.trim(),
          price: value.price,
          stock: value.stock,
          categoryId: Number(value.categoryId),
        };

        const id = this.id();
        const result = id ? await this.store.update(Number(id), request) : await this.store.create(request);

        if (result.ok) {
          this.notifications.success(`${result.value.name} saved (#${result.value.id}).`);
          await this.router.navigate(['/products']);
          return undefined;
        }

        // Show server-side errors (e.g. duplicate SKU) next to the field they belong to.
        const fieldErrors = Object.entries(result.error.fieldErrors);
        if (result.error.code === 'product.duplicate_sku') {
          return { kind: 'server', message: result.error.message, fieldTree: field.sku };
        }
        if (fieldErrors.length > 0) {
          return fieldErrors
            .filter(([key]) => key in value)
            .map(([key, messages]) => ({
              kind: 'server',
              message: messages[0],
              fieldTree: field[key as keyof ProductFormModel],
            }));
        }

        this.notifications.error(result.error.message);
        return undefined;
      },
    },
  });
}
