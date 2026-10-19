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
`UPDATE Products SET Stock = Stock + @delta, UpdatedAt = @now WHERE Id = @id AND Stock >= @qty`
(EF Core `ExecuteUpdateAsync`). 0 rows affected → the product is missing (404) or there is not enough stock (409).

**Why.** Stock is a hot counter that many requests change at once. A read-modify-write with optimistic concurrency was
my first version: correct (never oversold) but under contention it rejected requests that could have succeeded
(16 of 50 parallel decrements failed with 409 despite enough stock). The atomic statement serialises on the row lock,
needs no retries and does one round trip. `CHECK (Stock >= 0)` is the last line of defence.

**Trade-off.** The rule "stock cannot go negative" is expressed in the query instead of a domain method. I accept that
for this concurrency-critical path; the alternative that keeps it in the domain is a pessimistic lock
(`SELECT … WITH (UPDLOCK, ROWLOCK)` in a transaction, then `product.RemoveStock()`).

## 4. Optimistic concurrency for edits

**Decision.** `Product.Version` (GUID) is an EF concurrency token, renewed whenever the details change. Every response
carries it as `version`, and `PUT` sends back the version it loaded. If someone else saved in between, the update is
refused with 409 `product.concurrent_update` and nothing is overwritten. EF also puts the token in the `UPDATE … WHERE`,
which covers the milliseconds between our read and our write. The edit form shows the conflict and offers to load the
latest version.

**Why.** Without it, a form opened five minutes ago silently overwrites whatever another user saved since. A check
inside a single request only protects those few milliseconds, not the time the user spends in the form.

**`version` is optional.** The original contract has no such field, so requiring it would turn every update from a
client written to that contract into a 400. A `PUT` without `version` updates the current details (last write wins, as
before); only the race between our read and our write is still checked. An empty GUID is a 400. The UI always sends it.

**Stock movements do not renew the version.** It guards the details, which a stock movement never touches (PUT has no
stock, see 5). If sales renewed it, every sale of a popular product would turn open edit forms into conflicts. It also
means a sale can commit while a `PUT` is running without a conflict, so the `PUT` reads the stock again after saving:
its response shows the stock as it is, not as it was when the product was loaded.

**Why a body field and not `ETag` / `If-Match`.** An HTTP ETag identifies the whole representation, and that includes
the stock, so it has to change on every sale. Requiring it in `If-Match` would bring back exactly the conflict above.
A `version` field that is explicitly about the details says what it protects.

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

"Present" is checked as carefully as "valid": the numeric fields of the request contracts are nullable, because
System.Text.Json turns a missing `int` into `0`, so a product posted without `stock` would quietly be created with no
stock. Now a missing field is reported as `'Stock' is required.`, and each field gets one message (the first rule that fails).

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
Mutations write the server's response back into state (the server is the source of truth); delete is optimistic, and
every outcome reloads the page and summary. Even a refused deletion must not restore an old list snapshot, which
could undo another deletion or stock change that finished meanwhile. A mutation that gets no answer or a server error
(a proxy timeout, a dropped connection) may still have been applied, so it is never rolled back on trust: the page is reloaded instead. That matters
most for stock changes, where clicking again after a false failure would move the stock twice. Simple reads (`/api/categories`, product for editing) use `httpResource`, and the form model is a
`linkedSignal` that resets when the loaded product changes. Presentational components only have inputs/outputs.

"Reflect changes made through the API": the UI updates from every response, and refreshes on tab focus and every 30 s
while visible. SignalR push would be the next step.

**Sorting and paging happen on the server** (see 15); the store holds one page of 10 and the total. Column headers sort
on wide screens (only the sorted one carries `aria-sort`). On small screens the cards have no header, so a "Sort by"
menu does the same. A new sort or filter starts again from page 1. Sort and filters live in the store, so they survive
going to the edit form and back. After a stock change the row keeps its place, with no reload and no jump under the
cursor, unless a stock filter or a sort by stock is active. Then the product may enter or leave the result, or move,
so the page is loaded again to keep the filter, the order and the page boundaries true.

**Failed refreshes are visible.** If the page or the summary cannot be refreshed, the last data stays on screen under a
warning ("This may be out of date", the reason, the time of the last successful update and Try again). Only when
there is nothing to show yet does the page fall back to its full error state.

**Mutations and loads in flight.** `switchMap` only coordinates loads with each other. A list request that was already
on its way when a stock change or an edit finished may have read the data before it committed, and its reply would put
the old value back on screen. So when a mutation finishes while a list request is in flight, the store restarts it:
`switchMap` aborts the old request and the new one reads after the commit. With nothing in flight, nothing is reloaded,
so a stock change costs no extra request.

## 12. Security

- **CSP**: nginx generates a per-request nonce (`$request_id`), injects it into `index.html`
  (`<app-root ngCspNonce>`), and Angular applies it to the styles/scripts it adds. Result: `script-src 'self' 'nonce-…'`,
  `style-src 'self' 'nonce-…'`, no `unsafe-inline`. Critical-CSS inlining is disabled because it uses an inline handler.
- **API**: `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `nosniff`, `X-Frame-Options`,
  no `Server` header, HSTS + HTTPS redirect outside development, CORS allow-list, per-IP rate limiting.
- **Client address behind the proxy**: nginx forwards every request, so without help the API would see one address for
  all users, and the per-IP rate limit would become one shared limit. `UseForwardedHeaders` takes the client address from
  `X-Forwarded-For`, but only from the addresses in `ForwardedHeaders:KnownProxies`. Anyone else could forge the header
  to get a fresh limit on every request. In Docker Compose nginx has a fixed address on the network for this reason,
  outside the `ip_range` that Docker hands out to the other containers, so none of them can ever be given it.
- **Data**: EF Core parameterises every query; search uses `Contains`, which escapes `%`, `_` and `[` (tested).
- **Containers**: chiseled .NET image (no shell, non-root; `-extra` variant because SqlClient needs ICU) and unprivileged nginx.
- **Secrets**: none committed except the throwaway local Docker password; real environments use Key Vault / Managed Identity.

## 13. Migrations at start-up only in development

`Database:ApplyMigrationsOnStartup` is on for local runs. In production several replicas would race to migrate, so the
pipeline would run an EF migration bundle (`dotnet ef migrations bundle`) once before rolling out.

Seeding (also local only) checks for products and inserts them inside a transaction that holds a SQL Server
application lock (`sp_getapplock`). Instances starting together therefore seed exactly once: the second one waits,
then finds the products. Without the lock both saw an empty table and one failed on the unique SKUs at start-up
(reproduced 5 times out of 5, now covered by a test). EF Core locks migrations itself, but only once the database
exists. Creating the database is not coordinated, so starting several instances at once against a server where it does
not exist yet is not supported. The waiting instance gives up after 60 s (`@LockTimeout`), so the lock command gets a
90 s command timeout: with SqlClient's default of 30 s the wait ended early and the instance crashed at start-up
(reproduced by holding the lock for 40 s).

## 14. No UI component library

**Decision.** The UI is built with plain SCSS: BEM class names and design tokens as CSS custom properties
(`styles/_tokens.scss`). The only runtime dependencies are Angular and RxJS.

**Why.** The screens are simple: a list, filters, a form, a stock adjuster and toasts. Writing them directly kept the
initial bundle small (about 300 kB, 84 kB over the wire), and it works under the strict nonce-based CSP (see 12) with
no exceptions. It also keeps the code focused on what the assignment emphasises, how data is consumed and presented,
rather than on configuring a library.

**Alternatives considered.**
| Option | Why not here |
|---|---|
| Angular Material | Accessible, ready-made components (sortable/paginated table, dialog, select). Adopting it means rebuilding every template, a bigger bundle and the Material look. Its form controls predate Signal Forms, so the integration would need checking. |
| Bootstrap (+ ng-bootstrap) | Mostly CSS that these styles already cover. Its own JavaScript manipulates the DOM outside Angular, so interactive parts would need ng-bootstrap, which tends to lag new Angular releases. |
| Angular CDK only | The right tool for single pieces, e.g. a focus-trapping dialog for delete confirmation. Not needed yet. |

**Trade-off.** More CSS of our own to maintain, and richer components (date pickers, dialogs, a full data table)
have to be built by hand, as the sortable headers and the paginator were. In a real product the UI would follow the
team's design system: the company's own component library if there is one, otherwise Angular Material or Bootstrap
with ng-bootstrap, depending on what the team already uses.

## 15. Paging and sorting: on the server, opt-in

**Decision.** The three list endpoints accept optional `page`, `pageSize` (1–100), `sortBy` (`name`, `categoryName`,
`price`, `stock`) and `sortDir` (`asc`, `desc`). Without `page`/`pageSize` they return every match, exactly as the
assignment specifies; with them, one page. The body stays a plain array either way, and the number of matches across
all pages travels in the `X-Total-Count` header (exposed through CORS). The UI always asks for pages of 10.

**Why.** The ids allow 900,000 products; sending and rendering all of them does not scale. Paging only in the browser
would not help, because it still downloads everything. Sorting has to happen where the paging happens, since sorting
one page in the browser gives the wrong order. Every sort ends with the name and then the id, so the order is total and
no product shows up on two pages, or on none.

**Knock-on changes.**
- The KPI cards used to be computed from the loaded list; with paging they would only count one page. They now come
  from `GET /api/products/summary` (one aggregate query) and describe the whole catalogue. Filters don't change them;
  the paginator says how many products match. The summary is reloaded on refresh (the button, every 30 s, tab focus)
  and after changes that affect it, but not when paging, sorting or filtering, which never change it.
- `/search` also accepts `min`/`max`. Before, the UI searched by name and narrowed by stock in the browser, which would
  break with paging; now both filters run, and page, on the server.

**Alternatives considered.**
| Option | Why not |
|---|---|
| Always paged, with a default page size | Breaks the required contract: `GET /api/products` would no longer return all products. |
| An envelope `{ items, total, page }` | Same problem, and the body shape would depend on the query string. |
| Keyset (cursor) paging | Faster for deep pages and stable while rows are inserted, but no "page N of M" and more complex. Offset paging is enough at this scale. |

**Trade-off.** `OFFSET` gets slower on deep pages, and sorting by name or price would need indexes at real scale (only
`Stock` is indexed today). Counting and fetching are two queries, so a product created in between can leave the total
off by one for a moment. Names sort the way SQL Server compares them, case-insensitively but character by character,
so "Lens 10" comes before "Lens 2".

## 16. Request logging and request timeouts

**Decision.** Two built-in middleware, configured in `ApiServiceCollectionExtensions`:
- `UseHttpLogging` writes one entry per request: method, path, query string, status, duration, and the same `traceId`
  that problem-details responses carry (added by `TraceIdLoggingInterceptor`). It sits outside the exception handler,
  so a 500 is logged with the status the client really got. Headers and bodies are never logged, because they can carry
  credentials and personal data. Health probes are not logged at all, since orchestrators call them every few seconds.
- `UseRequestTimeouts` cancels a request after 30 seconds (`RequestTimeouts:Default`) and answers 504 with problem
  details. The cancellation reaches EF Core through the endpoints' `CancellationToken`, which cancels the SQL command,
  so slow work cannot pile up. SqlClient reports a cancelled command as a `SqlException`, not as an
  `OperationCanceledException`, so `UseRequestCancellation` rethrows whatever fails during a cancelled request as the
  cancellation it is. Otherwise a timeout inside the database would come back as a 500 and be logged as an error.
  ASP.NET Core does not apply timeouts while a debugger is attached.
- Writes (create, update, delete, stock movements) are exempt. The timeout could fire after the change committed, and
  the client would get a 504 for a change that was stored; after a stock movement, trying again would move the stock
  twice. Writes are short, and each SQL command is still bounded by SqlClient's own 30 s command timeout.

**Why.** When a user reports an error, the `traceId` in the response leads straight to the request in the log. A timeout
stops one slow dependency from tying up the server.

**Trade-off.** Locally the log is plain console text; in production it would go to a structured sink (OpenTelemetry /
Application Insights), where each field stays searchable. The query string is logged; here it only holds search terms,
filters and paging, but a query string carrying personal data would need redacting. 30 s is generous for this API, and
a single endpoint can get its own limit with `.WithRequestTimeout(...)`.
