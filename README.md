# Stpl.PriceManagement

Southern Travels price management, now **one ASP.NET Core MVC application** with **one database** (`Int_StplGITPricing`).

The behaviour is the same as before: same screens, same roles and permissions, same validation messages, same pricing rules. The rules still live in the stored procedures (`intlgit.fn_PriceMatrix` and the save/submit/promote procs). Only the structure changed:

| Before | Now |
|---|---|
| Pricing.Api + Domain + Infrastructure | `Areas/IntlGit` (Controllers → Services → Repositories) |
| ChangeSets.Api + Domain + Infrastructure | `Areas/IntlGit` (change sets are part of GIT pricing) |
| Shared Contracts project | `Areas/IntlGit/Models` |
| Razor Pages web + HTTP clients | MVC Controllers + Views. Services are called in-process, no HTTP |
| Dapper | ADO.NET (`Microsoft.Data.SqlClient`) behind small repositories |
| Outbox table + dispatcher to promote a tour | `intlgit.usp_MarkChangeSetApplied` promotes in the **same transaction**, with a 5-second sweeper as the safety net |
| Schemas `pricing`, `airfare`, `changesets`, `app` | `intlgit` (everything GIT) and `core` (users, notifications, email) |

## Layout

```
Stpl.PriceManagement.sln
Directory.Build.props              net10.0, C# 9, nullable/implicit usings off
Database/                          new database, area by area (see Database/README.md)
Stpl.PriceManagement/
  Program.cs                       all registrations + pipeline, grouped by area
  appsettings*.json
  Infrastructure/                  shared, area-neutral plumbing
    Data/                          SqlDatabase (ADO.NET helper), RowMapper, TVP helpers,
                                   SQL error-number mapping, DB health check
    Formatting/                    Inr (Indian grouping), DepartureDateFormat
    Validation/                    standard validation messages
    Web/                           CurrentUser, Navigation (menu built per area), icon tag helper
    ServiceResult.cs               Ok / Failed / Stale result every service returns
  Areas/
    Core/                          shared by every pricing type
      Controllers/                 Account (login/logout/password), Notifications, Home, LegacyRedirect
      Services/                    SignIn, Notifications, Email dispatcher, account CLI, user seeder
      Repositories/                UserRepository, NotificationRepository  → core.* procs
      Models/ ViewModels/ Views/ Domain/
    IntlGit/                       International GIT tour pricing
      Controllers/                 Products, Revision, Review, Version, FlightSheet, ChangeSets, FareRequests
      Services/                    TourService, ChangeSetService, request validators, UnstampedChangeSetSweeper
      Repositories/                TourRepository, ChangeSetRepository  → intlgit.* procs
      Domain/                      permissions, price states, change-set rules, HTML price table
      Models/ (Rows/)              request/response models and the row shapes read from SQL
      ViewModels/ Views/
      IntlGitNavigation.cs         this area's menu entries
  Views/Shared/                    layout, notification bell, icon sprite
  wwwroot/                         static files, by area (see wwwroot/README.md)
    common/                        css/app.css, js/site.js, images/ - shared by every area
    intlgit/                       js/workspace.js, images/ - International GIT only
```

Each request goes through the same layers: **Controller** (reads the form, checks the role, redirects) → **Service** (validates, maps, turns SQL errors into messages) → **Repository** (one method per stored procedure) → **SQL**.

## Addresses

The screens now sit under their area: `/IntlGit/Products`, `/IntlGit/Revision?code=…`, `/IntlGit/ChangeSets`, `/Core/Notifications`, `/Core/Account/Login`, and so on. The old addresses (`/Products`, `/Products/Revision?code=…`, `/ChangeSets/Detail?reference=…`, `/Notifications`, …) redirect to the new ones, so bookmarks and links in old emails still work.

## Running it

1. **Database:** follow `Database/README.md` (short version: run `Database/Int_StplGITPricing.sql`. It creates the database, all objects, master data and the 23 tours).
2. **Connection string:** `appsettings.Development.json` → `Database:ConnectionString` (production: `appsettings.json`, or the environment variable `Database__ConnectionString`).
3. `dotnet run --project Stpl.PriceManagement` → http://localhost:5200
4. Health: `/health/live` (process up), `/health/ready` (database reachable).

The SQL login the app uses needs only the `stpl_pricing_app` role (EXECUTE on schemas `core` and `intlgit`). It needs no table rights.

### Accounts (no admin screen, same as before)

```
dotnet run --project Stpl.PriceManagement -- account list
dotnet run --project Stpl.PriceManagement -- account reset   someone@southerntravels.com
dotnet run --project Stpl.PriceManagement -- account unlock  someone@southerntravels.com
dotnet run --project Stpl.PriceManagement -- account disable someone@southerntravels.com
dotnet run --project Stpl.PriceManagement -- account enable  someone@southerntravels.com
```

Users listed under `Seed:Users` in configuration are created at start-up (name and role kept in step; an existing password is never overwritten).

### Email

`MailSettings:Enabled = true` sends an email with every notification (SMTP settings and per-role addresses in `MailSettings`) and logs each attempt to `core.EmailLog`. When it is `false`, users get in-app notifications only.

## Adding another pricing type (e.g. International FIT, Domestic fixed)

Everything area-specific is in one folder plus one schema, so a new type sits beside GIT without touching it:

1. **Database:** add `Database/03_IntlFit/` with `01_Schema_And_Tables.sql`, `02_Types_Function_View.sql`, `03_Procedures.sql` using a new schema (`intlfit`). Add `GRANT EXECUTE ON SCHEMA::intlfit TO stpl_pricing_app;` to `03_Security.sql`. Reuse `core.[User]` and `core.Notification`.
2. **Code:** copy the shape of `Areas/IntlGit` to `Areas/IntlFit`: `Controllers` (with `[Area("IntlFit")]`), `Services`, `Repositories`, `Models`, `ViewModels`, `Views` (+ `_ViewImports.cshtml`, `_ViewStart.cshtml`), and an `IntlFitNavigation.cs`.
3. **Wire it up:** register its repositories and services in `Program.AddServices` under a new `// Area: IntlFit` block, and add its section in `Infrastructure/Web/Navigation.cs` (`AppNavigation.For`).
4. Notifications from the new area just call the `core` procs with their own links (`/IntlFit/...`). The bell, the email dispatcher and the notifications page work unchanged.

Routes need no change. `{area}/{controller}/{action}` picks the new area up automatically.
