# Price Transparency Explorer

Search and compare negotiated rates from health plan **Transparency in Coverage** (TiC) machine-readable files, starting from Centene's Fidelis index.

> Work in progress. This README grows with each feature PR; the final version covers architecture, data findings, and tradeoffs.

## Run it

Requires Docker.

```bash
docker compose up --build
```

- App: http://localhost:8080
- API docs (OpenAPI + Scalar): http://localhost:8080/scalar

## Develop

| Part | Needs | Command | URL |
|---|---|---|---|
| API | .NET 10 SDK | `dotnet run --project src/PriceExplorer.Api` | http://localhost:5080 |
| Web | Node 24 | `cd web && npm ci && npm run dev` | http://localhost:5173 (proxies `/api` to :5080) |
| Tests | | `dotnet test` · `cd web && npm run test:unit` | |

## Stack

- **API:** ASP.NET Core (.NET 10), controllers + MediatR CQRS, FluentValidation pipeline, RFC 9457 problem responses, OpenAPI + Scalar, Serilog.
- **Web:** Vue 3 + Vite + TypeScript, Vuetify 4, TanStack Vue Query, Pinia, zod, ECharts.
- **Tests:** xUnit v3 + AwesomeAssertions, Vitest, Playwright.
