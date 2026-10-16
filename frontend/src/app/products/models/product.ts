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
  /** Version of the details. Sent back on update, so an edit based on stale data is refused instead of overwriting. */
  version: string;
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
export type UpdateProductRequest = Omit<CreateProductRequest, 'stock'> & {
  /** The version of the product the edit started from. */
  version: string;
};

export interface ProductFilters {
  name: string;
  minStock: number | null;
  maxStock: number | null;
}

export const EMPTY_FILTERS: ProductFilters = { name: '', minStock: null, maxStock: null };

/** Columns the product list can be ordered by. */
export type ProductSortColumn = 'name' | 'categoryName' | 'price' | 'stock';

export interface ProductSort {
  column: ProductSortColumn;
  direction: 'asc' | 'desc';
}

/** Which page, in which order. The API sorts and pages; null sort keeps its default order. */
export interface ListParams {
  page: number;
  pageSize: number;
  sort: ProductSort | null;
}

/** One page of products, plus the number of matches across all pages (the X-Total-Count header). */
export interface ProductPage {
  items: Product[];
  total: number;
}

/** Catalogue-wide figures from GET /api/products/summary. Filters and paging do not change them. */
export interface ProductSummary {
  productCount: number;
  unitsInStock: number;
  lowStock: number;
  outOfStock: number;
}

/** With 1 to this many units a product is flagged as "low stock". Mirrors Product.LowStockThreshold in the API. */
export const LOW_STOCK_THRESHOLD = 10;
