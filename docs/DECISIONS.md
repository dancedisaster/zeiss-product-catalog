# Design decisions

Short records of the choices made, the alternatives considered and the trade-offs.

## 1. Clean Architecture with four projects

**Decision.** Domain → Application → Infrastructure → Api, dependencies pointing inwards.
**Why.** The job description asks for DDD and Clean Architecture; it keeps business rules testable without a
database and lets the persistence technology change without touching use cases.
**Trade-off.** More projects than a CRUD needs. For a single small service a vertical-slice layout would also be fine;
I chose layers because it makes the boundaries easy to discuss and grow into a microservice.

## 2. Unique 6-digit ids in a multi-instance deployment

**Decision.** SQL Server `SEQUENCE catalog.ProductIds` from 100000 to 999999, `NO CYCLE`, read with `NEXT VALUE FOR`
through `IProductIdGenerator`. The id is assigned before the insert, so the aggregate is valid from creation.

**Why.** All instances share the same database; the sequence is atomic there, so two instances can never get the same
value. No locks, no extra infrastructure, one tiny round-trip.

**Alternatives considered.**
| Option | Why not |
|---|---|
| `IDENTITY` column | Also unique, but cannot be constrained to 6 digits as cleanly and the id is only known after insert. |
| Random 6-digit number + unique index + retry | Not guessable, but collisions grow fast (birthday problem in a space of 900k); needs retry logic. |
| GUID / Snowflake / ULID | Unique without the DB, but do not fit in 6 digits. |
| In-memory counter / `Interlocked` | Breaks with more than one instance. |
| HiLo (EF Core `UseHiLo`) | Fewer round-trips, but leaves gaps and reserves blocks per instance; not needed at this scale. |

**Trade-off.** Sequential ids are predictable (enumeration). That is acceptable for a catalogue protected by
authentication/authorisation; if ids had to be secret I would expose a separate random public id.
Capacity is 900,000 ids; when the sequence is exhausted inserts fail loudly. Gaps can appear (e.g. a failed insert
after taking a value), which is fine for an identifier.

**Proof.** `DistributedIdGenerationTests` starts two API instances on one database and creates 100 products in
parallel: 100 distinct ids, all in range. The DB also has `CHECK (Id BETWEEN 100000 AND 999999)`.

## 3. Atomic stock movements

**Decision.** `decrement-stock` / `add-to-stock` run a single conditional update:
`UPDATE Products SET Stock = Stock + @delta, Version = @v, UpdatedAt = @now WHERE Id = @id AND Stock >= @qty`
(EF Core `ExecuteUpdateAsync`). 0 rows affected → the product is missing (404) or there is not enough stock (409).

**Why.** Stock is a hot counter that many requests change at once. A read-modify-write with optimistic concurrency was
my first version: correct (never oversold) but under contention it rejected requests that could have succeeded
(16 of 50 parallel decrements failed with 409 despite enough stock). The atomic statement serialises on the row lock,
needs no retries and does one round trip. `CHECK (Stock >= 0)` is the last line of defence.

**Trade-off.** The rule "stock cannot go negative" is expressed in the query instead of a domain method. I accept that
for this concurrency-critical path; the alternative that keeps it in the domain is a pessimistic lock
(`SELECT … WITH (UPDLOCK, ROWLOCK)` in a transaction, then `product.RemoveStock()`).

## 4. Optimistic concurrency for edits

**Decision.** `Product.Version` (GUID) is an EF concurrency token, renewed on every change. A PUT that loses a race gets 409.
**Why.** Prevents silent lost updates between instances. Next step: expose it as an `ETag` and require `If-Match` so the
check covers the time the user spends in the form, not only the request.

## 5. PUT excludes stock

**Decision.** `UpdateProductRequest` has no `Stock`; initial stock is set on POST, later only via stock endpoints.
**Why.** Otherwise an edit form opened before a sale would write back the old stock and "undo" the sale. Inventory is a
separate concern from catalogue data.

## 6. Errors: Result pattern + problem details

**Decision.** Application services return `Result<T>` with typed errors (Validation, NotFound, Conflict); one function maps
them to HTTP (400/404/409) as RFC 9457 problem details with a `code`. Unexpected exceptions go to a global
`IExceptionHandler` that logs and returns a generic 500 with a `traceId` (no stack traces).
**Why.** Expected failures are not exceptional; using values makes them explicit in signatures and cheap. Clients get a
stable machine-readable `code` (the Angular form maps `product.duplicate_sku` to the SKU field).

## 7. Validation in layers

Angular Signal Forms (instant feedback) → FluentValidation in the application layer (the contract; runs for any caller)
→ domain guards and DB constraints (invariants that must hold whatever the entry point).

## 8. Minimal APIs with typed results

`MapGroup("/api/products")`, `TypedResults` and `Results<…>` give compile-time checked responses and accurate OpenAPI
metadata, with less ceremony than controllers. Route constraints (`{id:int}`) stop `/search` and `/stock-level` from
being captured as an id.

## 9. Library choices

No MediatR and no AutoMapper: both moved to commercial licences in 2025, and here a service plus explicit mapping
methods are simpler to read and debug. FluentValidation (Apache 2.0) for validation. NSubstitute for mocks. Reqnroll (the
open-source SpecFlow successor) for BDD. Central Package Management, `TreatWarningsAsErrors`, NuGet audit with
transitive pinning (the build caught a vulnerable `System.Security.Cryptography.Pkcs` pulled in by SqlClient, pinned to a
patched version).

## 10. Tests against a real SQL Server

Integration tests use Testcontainers (SQL Server 2022 in Docker), with a fresh database per test class and the real
migrations. In-memory providers or SQLite would not support the sequence, `ExecuteUpdate` semantics, collation-based
case-insensitive search or CHECK constraints the same way, so they would test a different system.

## 11. Frontend state: signals + RxJS where it shines

`ProductStore` keeps state in signals and exposes `computed` slices (list, status, KPIs). Filter changes go through
RxJS (`debounceTime` + `switchMap`), which cancels stale requests so an older response can never overwrite a newer one.
Mutations write the server's response back into state (the server is the source of truth); delete is optimistic with
rollback. Simple reads (`/api/categories`, product for editing) use `httpResource`, and the form model is a
`linkedSignal` that resets when the loaded product changes. Presentational components only have inputs/outputs.

"Reflect changes made through the API": the UI updates from every response, and refreshes on tab focus and every 30 s
while visible. SignalR push would be the next step.

## 12. Security

- **CSP**: nginx generates a per-request nonce (`$request_id`), injects it into `index.html`
  (`<app-root ngCspNonce>`), and Angular applies it to the styles/scripts it adds. Result: `script-src 'self' 'nonce-…'`,
  `style-src 'self' 'nonce-…'`, no `unsafe-inline`. Critical-CSS inlining is disabled because it uses an inline handler.
- **API**: `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `nosniff`, `X-Frame-Options`,
  no `Server` header, HSTS + HTTPS redirect outside development, CORS allow-list, per-IP rate limiting.
- **Data**: EF Core parameterises every query; search uses `Contains`, which escapes `%`, `_` and `[` (tested).
- **Containers**: chiseled .NET image (no shell, non-root; `-extra` variant because SqlClient needs ICU) and unprivileged nginx.
- **Secrets**: none committed except the throwaway local Docker password; real environments use Key Vault / Managed Identity.

## 13. Migrations at start-up only in development

`Database:ApplyMigrationsOnStartup` is on for local runs. In production several replicas would race to migrate, so the
pipeline would run an EF migration bundle (`dotnet ef migrations bundle`) once before rolling out.
