/** Mirrors ProductDto in the API. */
export interface Product {
  id: number;
  name: string;
  description: string | null;
  sku: string;
  price: number;
  stock: number;
  categoryId: number;
  categoryName: string;
  createdAt: string;
  updatedAt: string;
}

export interface Category {
  id: number;
  name: string;
}

export interface CreateProductRequest {
  name: string;
  description: string | null;
  sku: string;
  price: number;
  stock: number;
  categoryId: number;
}

/** Stock is not part of an update: it only changes through the stock endpoints. */
export type UpdateProductRequest = Omit<CreateProductRequest, 'stock'>;

export interface ProductFilters {
  name: string;
  minStock: number | null;
  maxStock: number | null;
}

export const EMPTY_FILTERS: ProductFilters = { name: '', minStock: null, maxStock: null };

/** At or below this many units a product is flagged as "low stock". */
export const LOW_STOCK_THRESHOLD = 10;
