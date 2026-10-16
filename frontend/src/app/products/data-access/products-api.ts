import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  CreateProductRequest,
  ListParams,
  Product,
  ProductPage,
  ProductSummary,
  UpdateProductRequest,
} from '../models/product';

const TOTAL_COUNT_HEADER = 'X-Total-Count';

/** Thin, typed wrapper over the product endpoints. No state, no UI concerns. */
@Injectable({ providedIn: 'root' })
export class ProductsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/products';

  list(params: ListParams): Observable<ProductPage> {
    return this.getPage(this.baseUrl, toHttpParams(params));
  }

  get(id: number): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateProductRequest): Observable<Product> {
    return this.http.post<Product>(this.baseUrl, request);
  }

  update(id: number, request: UpdateProductRequest): Observable<Product> {
    return this.http.put<Product>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  decrementStock(id: number, quantity: number): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/${id}/decrement-stock/${quantity}`, null);
  }

  addToStock(id: number, quantity: number): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/${id}/add-to-stock/${quantity}`, null);
  }

  /** Name search; the optional stock range is applied on the server too, so paging stays correct. */
  search(name: string, min: number | null, max: number | null, params: ListParams): Observable<ProductPage> {
    return this.getPage(`${this.baseUrl}/search`, withStockRange(toHttpParams(params).set('name', name), min, max));
  }

  byStockLevel(min: number | null, max: number | null, params: ListParams): Observable<ProductPage> {
    return this.getPage(`${this.baseUrl}/stock-level`, withStockRange(toHttpParams(params), min, max));
  }

  summary(): Observable<ProductSummary> {
    return this.http.get<ProductSummary>(`${this.baseUrl}/summary`);
  }

  private getPage(url: string, params: HttpParams): Observable<ProductPage> {
    return this.http.get<Product[]>(url, { params, observe: 'response' }).pipe(map(toPage));
  }
}

function toHttpParams({ page, pageSize, sort }: ListParams): HttpParams {
  const params = new HttpParams().set('page', page).set('pageSize', pageSize);
  return sort ? params.set('sortBy', sort.column).set('sortDir', sort.direction) : params;
}

function withStockRange(params: HttpParams, min: number | null, max: number | null): HttpParams {
  if (min !== null) params = params.set('min', min);
  if (max !== null) params = params.set('max', max);
  return params;
}

/** The body is the page itself; the total across all pages travels in a header. */
function toPage(response: HttpResponse<Product[]>): ProductPage {
  const items = response.body ?? [];
  const total = response.headers.get(TOTAL_COUNT_HEADER);
  return { items, total: total === null ? items.length : Number(total) };
}
