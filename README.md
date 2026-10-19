# ZEISS Product Catalog

Full-stack solution for the Carl Zeiss Vision .NET & Angular hands-on assessment: a product API with
CRUD and stock operations (ASP.NET Core 10, EF Core 10, SQL Server) and an Angular 22 frontend that consumes it.

| | |
|---|---|
| **Backend** | .NET 10, ASP.NET Core Minimal APIs, EF Core 10 (code-first migrations), SQL Server, FluentValidation |
| **Frontend** | Angular 22 (standalone, zoneless, signals, Signal Forms, `httpResource`), SCSS with BEM, mobile first |
| **Tests** | xUnit + NSubstitute (unit), Testcontainers + WebApplicationFactory (integration), Reqnroll (BDD), Vitest (Angular) |
| **Run** | Docker Compose (SQL Server + API + nginx-served UI) |

## Quick start (Docker)

Requirements: Docker Desktop.

```bash
docker compose up --build
```

- UI: http://localhost:4200
- API reference (Scalar / OpenAPI): http://localhost:5080/scalar
- Health: http://localhost:5080/health/ready

The API applies the EF Core migrations and seeds 12 sample products on start-up.

SQL Server is published on port 14330, not 1433, so a SQL Server already installed on the machine does not get in the
way. If 4200, 5080 or 14330 is taken, override it: `UI_PORT=4300 API_PORT=5090 SQLSERVER_PORT=14331 docker compose up --build`
(PowerShell: `$env:UI_PORT=4300; docker compose up --build`).

## Running locally (for development)

Requirements: .NET SDK 10, Node.js 24 LTS (or ≥ 22.22.3), Docker (for SQL Server and the integration tests) or SQL Server LocalDB (see below).

```bash
# 1. SQL Server in Docker, on localhost:14330 (appsettings.Development.json points there)
docker compose up -d sqlserver

# 2. API on http://localhost:5080 (migrates + seeds in Development)
cd backend
dotnet run --project src/ProductCatalog.Api

# 3. Angular on http://localhost:4200 (proxies /api to the API, see proxy.conf.json)
cd frontend
npm ci
npm start
```

### Without Docker (Windows, SQL Server LocalDB)

LocalDB ships with Visual Studio (workload "Data storage and processing") or the SQL Server Express installer.
It is the same engine as SQL Server, so the migrations, the id sequence and the stock logic behave identically.

```bash
sqllocaldb start MSSQLLocalDB

cd backend
dotnet run --project src/ProductCatalog.Api --launch-profile localdb   # migrates + seeds on start

cd frontend
npm ci
npm start                                                              # http://localhost:4200
```

The `localdb` profile in `launchSettings.json` only overrides the connection string
(`Server=(localdb)\MSSQLLocalDB;Database=ProductCatalog;Trusted_Connection=True`). In Visual Studio or Rider, pick the
`localdb` profile in the run dropdown. With SQL Server Express installed instead, use `--launch-profile sqlexpress`
(`Server=.\SQLEXPRESS`, Windows authentication). Unit and Angular tests need no database; only the integration/BDD
tests need Docker.

The development connection string lives in `appsettings.Development.json` and targets the local SQL Server
container only. Other environments provide `ConnectionStrings__Catalog` through environment variables /
Azure Key Vault (ideally with Managed Identity instead of a password).

### Database migrations

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> -p src/ProductCatalog.Infrastructure -s src/ProductCatalog.Api -o Persistence/Migrations
dotnet ef database update -p src/ProductCatalog.Infrastructure -s src/ProductCatalog.Api
```

## Tests

```bash
cd backend
dotnet test                                   # everything (Docker must be running)
dotnet test tests/ProductCatalog.UnitTests    # unit tests only, no Docker needed

cd frontend
npm test -- --watch=false
```

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs all of this on every push to `main` and every pull
request, in three parallel jobs:
- **Backend:** formatting, a Release build in which warnings are errors, then the unit, integration and BDD tests
  against SQL Server in Docker.
- **Frontend:** Prettier, the tests and the production build.
- **Quick start:** `docker compose up --build`, then the API, the UI, the product list through nginx and the CSV export
  must answer.

| Suite | What it covers |
|---|---|
| `ProductCatalog.UnitTests` (74) | Domain invariants, validators (including missing fields), every branch of `ProductService` with mocked ports, paging and sorting parameters, export filters and default order |
| `ProductCatalog.IntegrationTests` (55) | All 9 endpoints over HTTP against a real SQL Server in Docker (Testcontainers): status codes, problem details, missing fields, stale edits, updates without a version, paging and sorting across pages, the summary, search escaping, security headers, per-client rate limiting behind the proxy, request timeouts (also inside a SQL command; writes exempt), request logging (fields, traceId, no headers), the CSV export (headers, filters and order, formula injection, quoting). Two in-memory API instances on one database prove unique ids and no overselling under parallel load |
| BDD (`Features/*.feature`, 7 scenarios) | Stock management and product discovery written in Gherkin (Reqnroll) |
| Angular (48) | Store behaviour (endpoint selection, paging and sorting parameters, back to the last page that exists, optimistic delete with server reconciliation after every outcome, overlapping deletion failures, server-truth updates, summary (not reloaded by paging, sorting or filtering), reload after a stock change under a stock filter or sort, no stale reply undoing a mutation, failed refreshes flagged), error mapping, stock badge, paginator, sortable list headers and menu, list page warning for out-of-date data, CSV export link for the current view, edit form (sends the loaded version, explains a conflict, unsaved-change detection and tab-close prompt), unsaved-changes guard |

## API

| Method | Route | Result |
|---|---|---|
| GET | `/api/products` | 200 list (every item includes `stock`); optional paging and sorting, see below |
| GET | `/api/products/summary` | 200 catalogue-wide counts: products, units in stock, low stock, out of stock (extra: feeds the dashboard) |
| GET | `/api/products/export` | 200 CSV download of every product matching the optional `name`, `min`, `max`, `sortBy`, `sortDir` (never paged) / 400 (extra) |
| GET | `/api/products/{id}` | 200 / 404 |
| POST | `/api/products` | 201 + `Location` / 400 / 409 duplicate SKU |
| PUT | `/api/products/{id}` | 200 / 400 / 404 / 409 duplicate SKU or stale `version` |
| DELETE | `/api/products/{id}` | 204 / 404 |
| POST | `/api/products/{id}/decrement-stock/{quantity}` | 200 with the updated product / 400 / 404 / 409 insufficient stock |
| POST | `/api/products/{id}/add-to-stock/{quantity}` | 200 with the updated product / 400 / 404 |
| GET | `/api/products/search?name={name}` | 200 partial, case-insensitive match, optionally within `min`/`max` stock / 400 without a term |
| GET | `/api/products/stock-level?min={min}&max={max}` | 200 products with `min ≤ stock ≤ max` (both optional) / 400 |
| GET | `/api/categories` | 200 (extra: feeds the product form) |

The three list endpoints accept optional `page`, `pageSize` (max 100), `sortBy` (`name`, `categoryName`, `price`,
`stock`) and `sortDir` (`asc`/`desc`), e.g. `/api/products?page=2&pageSize=10&sortBy=price&sortDir=desc`. Without
`page`/`pageSize` they return every match, as the assignment specifies. The body is always a plain array; the number of
matches across all pages is in the `X-Total-Count` header.

Every product carries a `version`. A `PUT` that sends the `version` it loaded is refused (409) if someone else saved
since, instead of overwriting their change. `version` is optional: without it, as in the original contract, the update
applies to the current data.

Errors always use RFC 9457 problem details with a machine-readable `code`, field `errors` for validation, and a `traceId`.
`backend/requests.http` has ready-made requests.

## Architecture

```
backend/src
├── ProductCatalog.Domain          Product aggregate + Category. No dependencies.
├── ProductCatalog.Application     Use cases (ProductService), DTOs, validators, ports (repository, id generator, unit of work), Result type
├── ProductCatalog.Infrastructure  EF Core DbContext, configurations, migrations, repositories, SQL sequence id generator
└── ProductCatalog.Api             Minimal API endpoints, error → HTTP mapping, security headers, CORS, rate limiting, request logging and timeouts, health checks

frontend/src/app
├── core/                          HTTP error mapping, notifications (toasts)
└── products/
    ├── data-access/               ProductsApi (HTTP only) + ProductStore (signals state, RxJS for async flows)
    ├── pages/                     Route components (list, create/edit form)
    └── ui/                        Presentational components (inputs/outputs only)
```

Dependencies point inwards (Clean Architecture): the API knows Application and Infrastructure only to wire them up.

## Key decisions

The full reasoning is in [docs/DECISIONS.md](docs/DECISIONS.md). In short:

- **6-digit ids across instances**: a SQL Server `SEQUENCE` (100000–999999, no cycle). `NEXT VALUE FOR` is atomic in the database, so any number of instances get distinct ids without coordination. Hidden behind `IProductIdGenerator`.
- **Stock changes**: one conditional `UPDATE … SET Stock = Stock + @delta WHERE Id = @id AND Stock >= @qty`. Atomic, no lost updates, no retries; a CHECK constraint backs it up. Tested with 60 parallel requests from two instances.
- **PUT does not touch stock**: stock only moves through the stock endpoints, so an edit form with a stale stock value can never undo a sale.
- **Optimistic concurrency for edits**: PUT sends back the `version` it loaded (optional, so clients of the original contract keep working); if someone else saved since, it gets 409 and nothing is overwritten. Stock movements don't change the version, so a sale never blocks an edit.
- **Validation in three layers**: Angular (UX), FluentValidation (contract), domain + DB constraints (invariants). A missing field is reported as required, never defaulted to 0.
- **Security**: strict CSP with per-request nonce in nginx (no `unsafe-inline`), API CSP `default-src 'none'`, CORS allow-list, per-client rate limiting (client address taken from `X-Forwarded-For` only when it comes from the trusted proxy), non-root chiseled images, no stack traces in responses, NuGet audit with transitive pinning.
- **Operations**: one log entry per request (method, path, status, duration and the `traceId` that error responses carry; never headers or bodies), and reads still running after 30 s are cancelled with a 504. Writes are exempt, since a 504 after the commit would report a stored change as failed.
- **UI safety and export**: leaving the product form with unsaved changes asks first (route guard, plus the browser's prompt when the tab closes). "Export CSV" downloads every product matching the current filters, in the current order, streamed by the API, with values a spreadsheet would run as formulas neutralised.

## Assumptions

- Price is in EUR with 2 decimals; SKU is unique, stored upper-case, letters/digits/dashes.
- Delete is a hard delete (in a real shop with orders it would be a soft delete / "discontinued" flag).
- Authentication is out of scope for the assignment; see "Next steps".
- With 900,000 possible ids the sequence is finite by design; the API fails clearly (500 + log) when it is exhausted.

## How this was built

I used an AI coding assistant, Claude Code, throughout: for the first implementation, to review it against the
assignment, and for the fixes, features and tests that followed. The `Co-Authored-By` lines in the commit history show
where.

My part was to steer and check the work:

- **Scope and trade-offs.** I decided what to build, which review findings to fix and what to leave out, such as
  authentication and bulk actions, and why. The reasoning is in [docs/DECISIONS.md](docs/DECISIONS.md).
- **Verification.** Every change comes with tests: unit tests, integration tests against a real SQL Server, BDD
  scenarios and Angular tests. Reported bugs were first reproduced with a failing test, then fixed. I ran the
  application locally (Docker, and against SQL Server Express), and the quick start was checked from a clean clone.
- **Changes in small steps.** After the first version, changes went through pull requests, so the history shows how
  the project evolved.

## Next steps (with more time)

- Authentication/authorisation with Microsoft Entra ID (JWT bearer) and role-based policies (e.g. only warehouse roles change stock).
- Indexes on `Name` and `Price` for sorting, and keyset (cursor) paging, once the catalogue grows large.
- Real-time updates with SignalR instead of focus/interval refresh.
- Bulk selection and bulk actions, with bulk endpoints that define what happens when some items fail.
- OpenTelemetry traces/metrics to Azure Monitor; extend CI into delivery (the same pipeline in Azure DevOps, `dotnet ef migrations bundle`, image scan and push to Azure Container Registry) and deploy to AKS / Container Apps.
- Playwright end-to-end tests in CI.
