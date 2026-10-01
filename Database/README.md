# Database: Int_StplGITPricing

One database for the whole application, organised **by area**: one SQL schema per area, one folder per area.

| Schema | Area | What is in it |
|---|---|---|
| `core` | Core | users, notifications, email log. Shared by every pricing type |
| `intlgit` | International GIT | tours, hubs, departures, cost build, fares, prices, change sets, version history |

A future pricing type gets its own schema and folder (`intlfit` → `03_IntlFit/`) and reuses `core`.

## Files and run order

| File | What it does |
|---|---|
| `00_CreateDatabase.sql` | creates `Int_StplGITPricing` if it is missing |
| `01_Core/01_Schema_And_Tables.sql` | schema `core` and its 3 tables |
| `02_IntlGit/01_Schema_And_Tables.sql` | schema `intlgit` and its 33 tables |
| `02_IntlGit/02_Types_Function_View.sql` | 10 table types, `fn_PriceMatrix` (the pricing formula), `vw_ChangeSetProgress` |
| `01_Core/02_Procedures.sql` | 17 `core` procedures |
| `02_IntlGit/03_Procedures.sql` | 50 `intlgit` procedures |
| `03_Security.sql` | role `stpl_pricing_app` with EXECUTE on both schemas (no table rights) |
| `04_MasterData.sql` | fare bands, occupancies, hubs, and the three starter accounts |
| `05_TourData.sql` | the 23 tours: 266 tour-hubs, 1,874 departure dates, 5,298 airfares, 1,622 flight routings |
| **`Int_StplGITPricing.sql`** | **all of the above in one script, in the right order** (built by `build_single_script.py`) |

**New server:** open `Int_StplGITPricing.sql` in SSMS (connected to any database) and press F5. It needs no other database.

### The tour data

`05_TourData.sql` holds the same tours the old system was loaded with ("Sept 10th Airfare Update.xlsx" + "Active tour list.xlsx", old migration 0070), with the later corrections already applied:

- every tour on `v1`, nothing published yet, no change sets or activity
- Joining / Leaving on every tour, with no airfare (the tour manager's ticket is entered by air-ticketing)
- per-occupancy costs and shared cost empty: the product executive enters them
- strike-through % and markups blank; FX rate only on FED13, FGL10, FPSD6, FBA11, FBA18 (old migration 0074)
- Joining / Leaving gets every upcoming departure date of its tour (old migration 0085)

It loads only into an empty `intlgit.Tour`, so running it again never duplicates tours.

Every script can be run again safely. Tables are created only if missing, procedures use `CREATE OR ALTER`, master data is inserted only if missing, and tours only if there are none.

> These scripts were checked for syntax and against every call the application makes (names, parameters, table types), but not executed against a live SQL Server here. Run them on a test copy first.

## What was removed (not used by the application)

**Tables / columns**

- `app.Outbox`: the event queue between ChangeSets and Pricing. Both are now in one database, so `intlgit.usp_MarkChangeSetApplied` promotes the tour (`EXEC intlgit.usp_PromoteVersion`) in the same transaction. If a promotion ever fails, `UnstampedChangeSetSweeper` retries it every 5 seconds.
- `dbo.SchemaVersions`: the DbUp migration journal. The new database is built from these scripts, not from 95 migrations.
- `ChangeSet.IdempotencyKey`, `ChangeSet.EffectiveFrom`: the key guarded the HTTP retry between web and API, which no longer exists. `EffectiveFrom` was never read.

**Procedures / views (13)**

| Kind | Old object | |
|---|---|---|
| procedure | `airfare.usp_GetLatestSubmissions` | — removed |
| procedure | `airfare.usp_GetOpenFareRequest` | — removed |
| procedure | `airfare.usp_ListFlightSheets` | — removed |
| procedure | `app.usp_ClaimEventBatch` | — removed |
| procedure | `app.usp_EnqueueEvent` | — removed |
| procedure | `app.usp_GetOutboxHealth` | — removed |
| procedure | `app.usp_MarkEventAbandoned` | — removed |
| procedure | `app.usp_MarkEventDispatched` | — removed |
| procedure | `app.usp_MarkEventFailed` | — removed |
| procedure | `pricing.usp_ConfirmPrices` | — removed |
| procedure | `pricing.usp_GetUnconfirmedPrices` | — removed |
| procedure | `pricing.usp_SetHubConditions` | — removed |
| view | `app.vw_OutboxHealth` | — removed |

The outbox procs and view went with the outbox. The other procs had no caller anywhere in the application. `usp_SetHubConditions` had already been dropped by migration 0040.

## Table map (old → new)

36 tables (37 before, minus `app.Outbox`). Columns, keys, constraints and indexes are unchanged unless listed above.

| Old | New |
|---|---|
| `airfare.DepartureFare` | `intlgit.DepartureFare` |
| `airfare.DepartureFlight` | `intlgit.DepartureFlight` |
| `airfare.FareQuery` | `intlgit.FareQuery` |
| `airfare.FareRequest` | `intlgit.FareRequest` |
| `airfare.FareSubmission` | `intlgit.FareSubmission` |
| `airfare.FareSubmissionLine` | `intlgit.FareSubmissionLine` |
| `airfare.TourBaseFare` | `intlgit.TourBaseFare` |
| `app.EmailLog` | `core.EmailLog` |
| `app.Notification` | `core.Notification` |
| `app.ReferenceSequence` | `intlgit.ReferenceSequence` |
| `app.[User]` | `core.[User]` |
| `changesets.ChangeSet` | `intlgit.ChangeSet` |
| `changesets.ChangeSetCell` | `intlgit.ChangeSetCell` |
| `changesets.ChangeSetRow` | `intlgit.ChangeSetRow` |
| `pricing.Activity` | `intlgit.Activity` |
| `pricing.CostBuild` | `intlgit.CostBuild` |
| `pricing.CostBuildOccupancy` | `intlgit.CostBuildOccupancy` |
| `pricing.FareBand` | `intlgit.FareBand` |
| `pricing.Hub` | `intlgit.Hub` |
| `pricing.LivePriceSnapshot` | `intlgit.LivePriceSnapshot` |
| `pricing.Occupancy` | `intlgit.Occupancy` |
| `pricing.PriceCondition` | `intlgit.PriceCondition` |
| `pricing.PriceConfirmation` | `intlgit.PriceConfirmation` |
| `pricing.PublishedPriceOverride` | `intlgit.PublishedPriceOverride` |
| `pricing.SubmittedPriceSnapshot` | `intlgit.SubmittedPriceSnapshot` |
| `pricing.SubmittedRemoval` | `intlgit.SubmittedRemoval` |
| `pricing.Tour` | `intlgit.Tour` |
| `pricing.TourDeparture` | `intlgit.TourDeparture` |
| `pricing.TourHub` | `intlgit.TourHub` |
| `pricing.TourVersionHistory` | `intlgit.TourVersionHistory` |
| `pricing.VersionSnapshot` | `intlgit.VersionSnapshot` |
| `pricing.VersionSnapshotDeparture` | `intlgit.VersionSnapshotDeparture` |
| `pricing.VersionSnapshotFare` | `intlgit.VersionSnapshotFare` |
| `pricing.VersionSnapshotHub` | `intlgit.VersionSnapshotHub` |
| `pricing.VersionSnapshotOccupancy` | `intlgit.VersionSnapshotOccupancy` |
| `pricing.VersionSnapshotPrice` | `intlgit.VersionSnapshotPrice` |

## Procedure / type / function / view map (old → new)

Most objects only moved schema. Where two old schemas had a procedure with the same name, the new one is named after what it does (`airfare.usp_SaveFares` → `intlgit.usp_SaveFaresForTour`, `changesets.usp_MarkApplied` → `intlgit.usp_MarkChangeSetApplied`, …).

| Kind | Old | New |
|---|---|---|
| function | `pricing.fn_PriceMatrix` | `intlgit.fn_PriceMatrix` |
| procedure | `airfare.usp_EnsureFares` | `intlgit.usp_EnsureFares` |
| procedure | `airfare.usp_GetAllFares` | `intlgit.usp_GetAllFares` |
| procedure | `airfare.usp_GetFareQueries` | `intlgit.usp_GetFareQueries` |
| procedure | `airfare.usp_GetFares` | `intlgit.usp_GetFares` |
| procedure | `airfare.usp_GetFlightSheet` | `intlgit.usp_GetFlightSheet` |
| procedure | `airfare.usp_GetWorkingColumnActor` | `intlgit.usp_GetWorkingColumnActor` |
| procedure | `airfare.usp_ListFareRequests` | `intlgit.usp_ListFareRequests` |
| procedure | `airfare.usp_RaiseFareQuery` | `intlgit.usp_RaiseFareQuery` |
| procedure | `airfare.usp_RequestFares` | `intlgit.usp_RequestFaresForTour` |
| procedure | `airfare.usp_ResolveFareQuery` | `intlgit.usp_ResolveFareQuery` |
| procedure | `airfare.usp_SaveFares` | `intlgit.usp_SaveFaresForTour` |
| procedure | `airfare.usp_SaveFlightDetails` | `intlgit.usp_SaveFlightDetails` |
| procedure | `airfare.usp_SubmitFares` | `intlgit.usp_SubmitFaresForTour` |
| procedure | `app.usp_ClaimUnsentNotifications` | `core.usp_ClaimUnsentNotifications` |
| procedure | `app.usp_CountUnreadNotifications` | `core.usp_CountUnreadNotifications` |
| procedure | `app.usp_GetUserForSignIn` | `core.usp_GetUserForSignIn` |
| procedure | `app.usp_ListNotifications` | `core.usp_ListNotifications` |
| procedure | `app.usp_ListUsers` | `core.usp_ListUsers` |
| procedure | `app.usp_LogEmail` | `core.usp_LogEmail` |
| procedure | `app.usp_MarkAllNotificationsRead` | `core.usp_MarkAllNotificationsRead` |
| procedure | `app.usp_MarkNotificationEmailed` | `core.usp_MarkNotificationEmailed` |
| procedure | `app.usp_MarkNotificationRead` | `core.usp_MarkNotificationRead` |
| procedure | `app.usp_NotifyPerson` | `core.usp_NotifyPerson` |
| procedure | `app.usp_NotifyRole` | `core.usp_NotifyRole` |
| procedure | `app.usp_RecordFailedSignIn` | `core.usp_RecordFailedSignIn` |
| procedure | `app.usp_RecordSignIn` | `core.usp_RecordSignIn` |
| procedure | `app.usp_SetPassword` | `core.usp_SetPassword` |
| procedure | `app.usp_SetUserActive` | `core.usp_SetUserActive` |
| procedure | `app.usp_UnlockUser` | `core.usp_UnlockUser` |
| procedure | `app.usp_UpsertUser` | `core.usp_UpsertUser` |
| procedure | `changesets.usp_CompleteAllRows` | `intlgit.usp_CompleteAllChangeSetRows` |
| procedure | `changesets.usp_CompleteRow` | `intlgit.usp_CompleteChangeSetRow` |
| procedure | `changesets.usp_CreateChangeSet` | `intlgit.usp_CreateChangeSet` |
| procedure | `changesets.usp_GetByTour` | `intlgit.usp_GetChangeSetsByTour` |
| procedure | `changesets.usp_GetChangeSet` | `intlgit.usp_GetChangeSet` |
| procedure | `changesets.usp_GetTourHistory` | `intlgit.usp_GetChangeSetHistory` |
| procedure | `changesets.usp_GetWorkList` | `intlgit.usp_GetChangeSetWorkList` |
| procedure | `changesets.usp_ListUnstamped` | `intlgit.usp_ListUnstampedChangeSets` |
| procedure | `changesets.usp_MarkApplied` | `intlgit.usp_MarkChangeSetApplied` |
| procedure | `pricing.usp_AddDeparture` | `intlgit.usp_AddDeparture` |
| procedure | `pricing.usp_AddTourHub` | `intlgit.usp_AddTourHub` |
| procedure | `pricing.usp_AdoptCalculatedPrices` | `intlgit.usp_AdoptCalculatedPrices` |
| procedure | `pricing.usp_AssertUnchanged` | `intlgit.usp_AssertUnchanged` |
| procedure | `pricing.usp_ClearSubmittedPrices` | `intlgit.usp_ClearSubmittedPrices` |
| procedure | `pricing.usp_ConfirmPriceCells` | `intlgit.usp_ConfirmPriceCells` |
| procedure | `pricing.usp_GetChangesSince` | `intlgit.usp_GetChangesSince` |
| procedure | `pricing.usp_GetHubMaster` | `intlgit.usp_GetHubMaster` |
| procedure | `pricing.usp_GetPendingChanges` | `intlgit.usp_GetPendingChanges` |
| procedure | `pricing.usp_GetTourActivity` | `intlgit.usp_GetTourActivity` |
| procedure | `pricing.usp_GetTourList` | `intlgit.usp_GetTourList` |
| procedure | `pricing.usp_GetTourRevision` | `intlgit.usp_GetTourRevision` |
| procedure | `pricing.usp_GetTourStamp` | `intlgit.usp_GetTourStamp` |
| procedure | `pricing.usp_GetVersionHistory` | `intlgit.usp_GetVersionHistory` |
| procedure | `pricing.usp_GetVersionSnapshot` | `intlgit.usp_GetVersionSnapshot` |
| procedure | `pricing.usp_NotifyChangeSetSubmitted` | `intlgit.usp_NotifyChangeSetSubmitted` |
| procedure | `pricing.usp_PreviewPrices` | `intlgit.usp_PreviewPrices` |
| procedure | `pricing.usp_PromoteVersion` | `intlgit.usp_PromoteVersion` |
| procedure | `pricing.usp_RecordSubmittedPrices` | `intlgit.usp_RecordSubmittedPrices` |
| procedure | `pricing.usp_RecordSubmittedRemovals` | `intlgit.usp_RecordSubmittedRemovals` |
| procedure | `pricing.usp_RequestFares` | `intlgit.usp_RequestFares` |
| procedure | `pricing.usp_SaveCostBuild` | `intlgit.usp_SaveCostBuild` |
| procedure | `pricing.usp_SaveFares` | `intlgit.usp_SaveFares` |
| procedure | `pricing.usp_SavePublishedPrices` | `intlgit.usp_SavePublishedPrices` |
| procedure | `pricing.usp_SetConditions` | `intlgit.usp_SetConditions` |
| procedure | `pricing.usp_SetDepartureActive` | `intlgit.usp_SetDepartureActive` |
| procedure | `pricing.usp_SubmitFares` | `intlgit.usp_SubmitFares` |
| procedure | `pricing.usp_UpdateTourHub` | `intlgit.usp_UpdateTourHub` |
| type | `airfare.DepartureList` | `intlgit.DepartureList` |
| type | `airfare.FareEditList` | `intlgit.FareEditList` |
| type | `airfare.FlightDetailList` | `intlgit.FlightDetailList` |
| type | `changesets.ChangeCellList` | `intlgit.ChangeCellList` |
| type | `changesets.ChangeRowList` | `intlgit.ChangeRowList` |
| type | `pricing.HubCodeList` | `intlgit.HubCodeList` |
| type | `pricing.OccupancyCostList` | `intlgit.OccupancyCostList` |
| type | `pricing.PriceCellList` | `intlgit.PriceCellList` |
| type | `pricing.PriceList` | `intlgit.PriceList` |
| type | `pricing.ResolvedFareList` | `intlgit.ResolvedFareList` |
| view | `changesets.vw_ChangeSetProgress` | `intlgit.vw_ChangeSetProgress` |

## Error numbers (unchanged)

Business errors raised by the procs, which the application turns into messages:

- `50001–50099`: pricing (`50030` = the tour changed while you had it open, and the screen redraws what you typed)
- `60001–60099`: change sets
