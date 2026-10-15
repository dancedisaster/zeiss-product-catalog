import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateProductRequest, Product, UpdateProductRequest } from '../models/product';

/** Thin, typed wrapper over the 9 product endpoints. No state, no UI concerns. */
@Injectable({ providedIn: 'root' })
export class ProductsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/products';

  list(): Observable<Product[]> {
    return this.http.get<Product[]>(this.baseUrl);
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

  search(name: string): Observable<Product[]> {
    return this.http.get<Product[]>(`${this.baseUrl}/search`, { params: { name } });
  }

  byStockLevel(min: number | null, max: number | null): Observable<Product[]> {
    let params = new HttpParams();
    if (min !== null) params = params.set('min', min);
    if (max !== null) params = params.set('max', max);
    return this.http.get<Product[]>(`${this.baseUrl}/stock-level`, { params });
  }
}
