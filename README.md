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

## Running locally (for development)

Requirements: .NET SDK 10, Node.js 24 LTS (or ≥ 22.22.3), Docker (for SQL Server and the integration tests).

```bash
# 1. SQL Server
docker compose up -d sqlserver

# 2. API on http://localhost:5080 (migrates + seeds in Development)
cd backend
dotnet run --project src/ProductCatalog.Api

# 3. Angular on http://localhost:4200 (proxies /api to the API, see proxy.conf.json)
cd frontend
npm ci
npm start
```

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

| Suite | What it covers |
|---|---|
| `ProductCatalog.UnitTests` (52) | Domain invariants, validators, every branch of `ProductService` with mocked ports |
| `ProductCatalog.IntegrationTests` (23) | All 9 endpoints over HTTP against a real SQL Server in Docker (Testcontainers): status codes, problem details, search escaping, security headers. Two in-memory API instances on one database prove unique ids and no overselling under parallel load |
| BDD (`Features/*.feature`, 7 scenarios) | Stock management and product discovery written in Gherkin (Reqnroll) |
| Angular (12) | Store behaviour (endpoint selection, optimistic delete + rollback, server-truth updates), error mapping, stock badge |

## API

| Method | Route | Result |
|---|---|---|
| GET | `/api/products` | 200 list (every item includes `stock`) |
| GET | `/api/products/{id}` | 200 / 404 |
| POST | `/api/products` | 201 + `Location` / 400 / 409 duplicate SKU |
| PUT | `/api/products/{id}` | 200 / 400 / 404 / 409 |
| DELETE | `/api/products/{id}` | 204 / 404 |
| POST | `/api/products/{id}/decrement-stock/{quantity}` | 200 with the updated product / 400 / 404 / 409 insufficient stock |
| POST | `/api/products/{id}/add-to-stock/{quantity}` | 200 with the updated product / 400 / 404 |
| GET | `/api/products/search?name={name}` | 200 partial, case-insensitive match / 400 without a term |
| GET | `/api/products/stock-level?min={min}&max={max}` | 200 products with `min ≤ stock ≤ max` (both optional) / 400 |
| GET | `/api/categories` | 200 (extra: feeds the product form) |

Errors always use RFC 9457 problem details with a machine-readable `code`, field `errors` for validation, and a `traceId`.
`backend/requests.http` has ready-made requests.

## Architecture

```
backend/src
├── ProductCatalog.Domain          Product aggregate + Category. No dependencies.
├── ProductCatalog.Application     Use cases (ProductService), DTOs, validators, ports (repository, id generator, unit of work), Result type
├── ProductCatalog.Infrastructure  EF Core DbContext, configurations, migrations, repositories, SQL sequence id generator
└── ProductCatalog.Api             Minimal API endpoints, error → HTTP mapping, security headers, CORS, rate limiting, health checks

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
- **Optimistic concurrency** (`Version` token) protects edits; a lost race returns 409.
- **Validation in three layers**: Angular (UX), FluentValidation (contract), domain + DB constraints (invariants).
- **Security**: strict CSP with per-request nonce in nginx (no `unsafe-inline`), API CSP `default-src 'none'`, CORS allow-list, rate limiting, non-root chiseled images, no stack traces in responses, NuGet audit with transitive pinning.

## Assumptions

- Price is in EUR with 2 decimals; SKU is unique, stored upper-case, letters/digits/dashes.
- Delete is a hard delete (in a real shop with orders it would be a soft delete / "discontinued" flag).
- Authentication is out of scope for the assignment; see "Next steps".
- With 900,000 possible ids the sequence is finite by design; the API fails clearly (500 + log) when it is exhausted.

## Next steps (with more time)

- Authentication/authorisation with Microsoft Entra ID (JWT bearer) and role-based policies (e.g. only warehouse roles change stock).
- Pagination and sorting on `GET /api/products` (kept as a plain array here to match the required contract).
- Real-time updates with SignalR instead of focus/interval refresh; ETag/If-Match on PUT.
- OpenTelemetry traces/metrics to Azure Monitor; CI pipeline (build, test, `dotnet ef migrations bundle`, container scan) in Azure DevOps; deploy to AKS / Container Apps.
- Playwright end-to-end tests in CI.
