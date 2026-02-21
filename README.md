<div align="center">

# 📈 Time Series Forecast Master API (C# / .NET 8)

**Time-series forecasting service + “final master” CRUD template** (REST, auth, RBAC, soft-delete, import/export, outbox, audit, rate-limit) — all in **C#**.

![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=.net&logoColor=white)
![C#](https://img.shields.io/badge/C%23-C%23-239120?logo=csharp&logoColor=white)
![SQLite](https://img.shields.io/badge/DB-SQLite-003B57?logo=sqlite&logoColor=white)
![Swagger](https://img.shields.io/badge/OpenAPI-Swagger-85EA2D?logo=swagger&logoColor=black)

</div>

---

## ✨ What you get

- **Master CRUD** for resource **`items`** with: bulk ops, filtering, sorting, pagination (page/size + cursor), projection (`select=`), include/expand (`include=category,tags`), lifecycle (soft-delete/restore/archive), workflows (publish/approve/verify/etc), idempotency, optimistic locking (ETag), audit logs, import/export (CSV/XLSX/PDF), and more.
- **Time Series module** for **`series`** + **forecasting**:
  - Import points from **CSV/JSON**
  - Run forecasts with **Seasonal Naive** or **Exponential Smoothing (ETS)**
  - Store forecast runs + metrics (MAE, RMSE)
- **Auth & Admin**: register/login/refresh, sessions, RBAC permissions, admin endpoints, feature flags.
- **Reliability**: outbox worker + retry/backoff + DLQ.
- **Observability**: activity logs (request logging) + health checks.

---

## 🧱 Tech stack

- **ASP.NET Core Minimal API** (NET 8)
- **EF Core + SQLite** (self-contained DB)
- **JWT** authentication
- **RBAC + permissions** (policy-based authorization)
- **Swagger** docs
- **Rate limiting** (built-in)
- **Export**: CSV/XLSX/PDF (CsvHelper, ClosedXML, QuestPDF)

---

## 🚀 Quick start (Windows PowerShell)

### 1) Prerequisites

- **.NET SDK 8**

### 2) Run

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\TimeSeriesForecast.Api\TimeSeriesForecast.Api.csproj
```

Swagger UI:
- `http://localhost:5000/swagger`

### 3) Default admin (seeded on first run)

- Email: `admin@example.com`
- Password: `Admin123!`

> Important: for real usage, set JWT key via env var:
> `Jwt__Key="YOUR_LONG_RANDOM_SECRET"`

---

## 🔐 Auth flow

### Register
`POST /api/v1/auth/register`

### Login
`POST /api/v1/auth/login`

### Refresh
`POST /api/v1/auth/refresh`

### Sessions
- `GET /api/v1/auth/sessions`
- `POST /api/v1/auth/sessions/{id}/revoke`

---

## 🧩 REST endpoints (items)

Base: `/api/v1/items`

Core:
- `POST /items` (create)
- `GET /items` (list)
- `GET /items/{id}` (detail)
- `PUT /items/{id}` (full update)
- `PATCH /items/{id}` (partial update)
- `DELETE /items/{id}` (soft delete by default, `?force=true` for hard)

Bulk:
- `POST /items/bulk`
- `PATCH /items/bulk`
- `DELETE /items/bulk`

Lifecycle:
- `POST /items/{id}/restore`
- `POST /items/{id}/archive`
- `POST /items/{id}/unarchive`
- `POST /items/purge?days=30`

Query helpers:
- `GET /items/count`
- `HEAD /items/{id}` (exists)
- `GET /items/distinct?field=status`
- `GET /items/aggregate?field=price&op=sum&groupBy=category`
- `GET /items/report?by=category`

Import/Export:
- `POST /items/import` (CSV/JSON)
- `GET /items/export?format=csv|xlsx|pdf`

Relations:
- `POST /items/{id}/tags/attach`
- `POST /items/{id}/tags/detach`
- `POST /items/{id}/tags/sync`

Workflow (state machine):
- `POST /items/{id}/actions/{action}`
  - `submit | approve | reject | publish | unpublish | activate | deactivate | verify | unverify | cancel | close | reopen`

---

## 📈 Time series endpoints

Base: `/api/v1/series`

- `POST /series` (create dataset)
- `GET /series` (list)
- `GET /series/{id}` (detail)
- `DELETE /series/{id}` (delete dataset)

Points:
- `POST /series/{id}/points/import` (CSV/JSON, `?mode=replace|append`)
- `GET /series/{id}/points?take=2000`

Forecast:
- `POST /series/{id}/forecast`
  - body: `{ "method": "seasonalnaive" | "ets", "horizon": 30, "holdout": 7, "alpha": 0.35, "seasonLength": 7 }`
- `GET /series/{id}/forecasts`

---

## 🧰 Admin & Ops

- Users: `GET/POST /api/v1/admin/users`, lock/unlock
- Roles: `GET/POST /api/v1/admin/roles`, sync permissions
- Feature flags: `GET/POST /api/v1/admin/feature-flags`
- Logs: `GET /api/v1/admin/logs`
- DLQ: `GET /api/v1/admin/dlq`

---

## 🗂️ Project structure

```text
TimeSeriesForecast.Master/
├─ src/
│  ├─ TimeSeriesForecast.Core/
│  └─ TimeSeriesForecast.Api/
├─ tests/
│  └─ TimeSeriesForecast.Tests/
├─ scripts/
│  ├─ build.ps1
│  ├─ run.ps1
│  └─ reset-db.ps1
└─ README.md
```

---

## ✅ Mapping to the “Final Master Checklist”

This template implements the **API-level** items directly (CRUD, bulk, query helpers, lifecycle, RBAC, rate limit, audit logs, outbox/DLQ, import/export, health, versioning, Swagger). 
Infra-level topics (WAF/CDN/load balancer, TLS termination, etc.) are **documented as deployment concerns**.

---

## 📝 Notes

- SQLite is great for demos and local runs. For production, switch to PostgreSQL and use EF migrations.
- The JWT key in `appsettings.json` is **dev-only**. Use environment variables or secret store.

