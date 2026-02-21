<div align="center">

<!-- Animated Wave Header -->
<img src="https://capsule-render.vercel.app/api?type=waving&height=210&color=0:0ea5e9,100:8b5cf6&text=Time%20Series%20Forecast%20Master%20API&fontSize=44&fontColor=ffffff&animation=fadeIn&fontAlignY=35&desc=.NET%208%20Minimal%20API%20%7C%20Master%20CRUD%20%2B%20RBAC%20%2B%20Import%2FExport%20%7C%20Forecasting%20ETS%20%26%20SeasonalNaive&descAlignY=58&descSize=16" />

<!-- Typing Animation -->
<img src="https://readme-typing-svg.demolab.com?font=Fira+Code&size=16&duration=2400&pause=700&color=0EA5E9&center=true&vCenter=true&width=1000&lines=REST+API+template+siap+pakai+%E2%9C%85+Auth+JWT+%E2%9C%85+RBAC+%E2%9C%85+Rate+Limit;CRUD+lengkap%3A+bulk%2C+filter%2C+sort%2C+cursor%2Fpage%2C+ETag%2C+audit%2C+soft-delete;Time+Series%3A+import+CSV%2FJSON+%E2%86%92+forecast+ETS%2FSeasonalNaive+%E2%86%92+MAE%2FRMSE+%F0%9F%93%88;Swagger+UI+ready+%7C+SQLite+local+DB+%7C+Outbox+%2B+DLQ+%F0%9F%9B%A1%EF%B8%8F" />

<br/>

<!-- Hero Image -->
<img src="https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=1200&h=280&fit=crop" alt="Time series" width="100%" style="border-radius:14px;" />

<br/><br/>

<b>📈 Time Series Forecast Master API</b><br/>
<i>Service forecasting deret waktu + template “final master” CRUD untuk membangun backend production-ready dengan cepat.</i><br/><br/>

<!-- Premium Buttons -->
<a href="https://github.com/yirassssindaba-coder/time-series-forecasting">
  <img src="https://img.shields.io/badge/%F0%9F%93%A6%20Repository-0ea5e9?style=for-the-badge&logo=github&logoColor=white" />
</a>
<a href="https://github.com/yirassssindaba-coder/time-series-forecasting/issues">
  <img src="https://img.shields.io/badge/%F0%9F%90%9B%20Issues-8b5cf6?style=for-the-badge&logo=github&logoColor=white" />
</a>

<br/><br/>

<!-- Tech Badges -->
<img src="https://img.shields.io/badge/.NET%208-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"/>
<img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white"/>
<img src="https://img.shields.io/badge/Minimal%20API-0f172a?style=for-the-badge&logo=swagger&logoColor=white"/>
<img src="https://img.shields.io/badge/EF%20Core-8b5cf6?style=for-the-badge&logo=nuget&logoColor=white"/>
<img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white"/>
<img src="https://img.shields.io/badge/JWT-2563eb?style=for-the-badge&logo=jsonwebtokens&logoColor=white"/>

<br/>

<!-- Repo Insights -->
<img src="https://img.shields.io/github/last-commit/yirassssindaba-coder/time-series-forecasting?style=for-the-badge&label=%F0%9F%95%92%20Last%20Update&color=0ea5e9" />
<img src="https://img.shields.io/github/languages/top/yirassssindaba-coder/time-series-forecasting?style=for-the-badge&label=%F0%9F%A7%A0%20Lang&color=8b5cf6" />

<br/><br/>

<p>
<b>Forecasting</b> • <b>CRUD Master</b> • <b>RBAC</b> • <b>Audit</b> • <b>Import/Export CSV/XLSX/PDF</b> • <b>Outbox + DLQ</b>
</p>

</div>

---

## 🧭 Table of Contents
- [✨ What you get](#-what-you-get)
- [🚀 Quick start](#-quick-start)
- [🧠 Data flow](#-data-flow)
- [🧪 API at a glance](#-api-at-a-glance)
- [📦 Storage & konfigurasi](#-storage--konfigurasi)
- [🧪 Testing](#-testing)
- [🗺️ Project structure](#-project-structure)
- [🔐 Security notes](#-security-notes)
- [📌 Roadmap](#-roadmap)

---

## ✨ What you get

### ✅ Master CRUD template (resource: `items`)
- Bulk ops, filtering, sorting, pagination (page/size + cursor)
- Projection `select=` + include/expand `include=category,tags`
- Lifecycle: soft-delete/restore/archive + purge
- Workflow/actions (submit/approve/publish/verify/dll)
- Idempotency, optimistic locking (ETag), audit logs
- Import/Export **CSV / XLSX / PDF** (CsvHelper, ClosedXML, QuestPDF)

### 📈 Time Series module (resource: `series`)
- Import points dari **CSV/JSON**
- Forecast methods:
  - **Seasonal Naive**
  - **ETS (Exponential Smoothing)**
- Simpan forecast runs + metrics **MAE** & **RMSE**

### 🛡️ Ops & reliability
- Auth JWT + refresh/session + **RBAC**
- Rate limiting (built-in)
- Outbox worker + retry/backoff + **DLQ**
- Activity logs + health checks
- Swagger docs

> Semua poin di atas berasal dari README repo (fitur & endpoint) dan konfigurasi default.  

---

## 🚀 Quick start

### 1) Prerequisites
- .NET SDK 8

### 2) Run (Windows PowerShell)
```powershell
dotnet restore
dotnet build
dotnet run --project .\src\TimeSeriesForecast.Api\TimeSeriesForecast.Api.csproj
```

Swagger UI (default):
- `http://localhost:5000/swagger`

### 3) Default admin (seeded on first run)
- Email: `admin@example.com`
- Password: `Admin123!`

### 4) Set JWT secret (recommended)
> Dev default di `appsettings.json` itu **bukan** untuk production. Set env var:

```powershell
$env:Jwt__Key="YOUR_LONG_RANDOM_SECRET"
```

---

## 🧠 Data flow

```mermaid
flowchart LR
  C["Client (Swagger / Postman / App)"] --> A["Auth (JWT)"]
  A --> I["Items CRUD API"]
  C --> S["Series API"]
  S --> P["Import points (CSV/JSON)"]
  P --> F["Forecast (ETS / SeasonalNaive)"]
  F --> M["Metrics (MAE, RMSE)"]
  I --> DB["SQLite (tsf_master.db)"]
  S --> DB
  F --> DB
```

---

## 🧪 API at a glance

### Auth
- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `GET  /api/v1/auth/sessions`
- `POST /api/v1/auth/sessions/{id}/revoke`

### Items (base: `/api/v1/items`)
Core:
- `POST /items` • `GET /items` • `GET /items/{id}` • `PUT /items/{id}` • `PATCH /items/{id}`
- `DELETE /items/{id}` (soft delete; `?force=true` untuk hard)

Bulk:
- `POST /items/bulk` • `PATCH /items/bulk` • `DELETE /items/bulk`

Import/Export:
- `POST /items/import` (CSV/JSON)
- `GET  /items/export?format=csv|xlsx|pdf`

### Time series (base: `/api/v1/series`)
- `POST /series` • `GET /series` • `GET /series/{id}` • `DELETE /series/{id}`

Points:
- `POST /series/{id}/points/import?mode=replace|append`
- `GET  /series/{id}/points?take=2000`

Forecast:
- `POST /series/{id}/forecast`
  - body contoh:
    ```json
    { "method": "ets", "horizon": 30, "holdout": 7, "alpha": 0.35, "seasonLength": 7 }
    ```
- `GET /series/{id}/forecasts`

---

## 📦 Storage & konfigurasi

Default config (lihat `appsettings.json`):
- DB: `Data Source=tsf_master.db`
- Storage root: `../../storage`
- Rate limit default: `PermitLimit=60` per `WindowSeconds=60`

> Untuk production: ganti DB ke PostgreSQL + EF migrations, dan simpan secrets via env/secret store.

---

## 🧪 Testing

```powershell
dotnet test
```

---

## 🗺️ Project structure

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

## 🔐 Security notes

- Jangan pakai `Jwt:Key` default dari `appsettings.json` untuk production.
- Pastikan endpoint admin dilindungi RBAC.
- Kalau expose ke publik, tambahkan:
  - HTTPS + reverse proxy
  - WAF/rate-limit tambahan
  - logging + monitoring eksternal

---

## 📌 Roadmap
- [ ] Forecast methods tambahan (ARIMA/Prophet/LightGBM/LSTM) *(opsional)*
- [ ] PostgreSQL + migrations
- [ ] Docker compose (API + DB)
- [ ] Background job dashboard untuk outbox/DLQ

---

<div align="center">

<img src="https://readme-typing-svg.demolab.com?font=Fira+Code&size=14&duration=1900&pause=700&color=8B5CF6&center=true&vCenter=true&width=920&lines=Build+your+backend+faster+%E2%9A%A1+Forecast+your+series+%F0%9F%93%88+Ship+with+confidence+%F0%9F%9B%A1%EF%B8%8F" />

</div>
