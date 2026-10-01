using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Stpl.PriceManagement.Areas.IntlGit.Models.Rows;
using Stpl.PriceManagement.Infrastructure.Data;

namespace Stpl.PriceManagement.Areas.IntlGit.Repositories
{
    public interface ITourRepository
    {
        Task<IReadOnlyList<TourListRow>> ListToursAsync(CancellationToken cancellationToken);

        /// <summary>Returns null when there is no tour with that code.</summary>
        Task<TourRevisionData> GetRevisionAsync(string tourCode, CancellationToken cancellationToken);

        Task<PendingChangesData> GetPendingChangesAsync(string tourCode, CancellationToken cancellationToken);

        Task<IReadOnlyList<HubMasterRow>> ListHubsAsync(CancellationToken cancellationToken);

        Task SaveCostBuildAsync(
            SaveCostBuildCommand command, Actor actor, CancellationToken cancellationToken,
            DateTime? expectedModifiedUtc = null);

        Task SavePublishedPricesAsync(
            string tourCode, IReadOnlyList<PriceEntry> prices, Actor actor,
            CancellationToken cancellationToken, DateTime? expectedModifiedUtc = null);

        Task SaveFaresAsync(
            string tourCode, IReadOnlyList<FareEntry> fares, Actor actor,
            CancellationToken cancellationToken, DateTime? expectedModifiedUtc = null);

        Task SubmitFaresAsync(
            string tourCode, string submittedBy, string note, CancellationToken cancellationToken);

        Task RequestFaresAsync(
            string tourCode, string requestedBy, string note, CancellationToken cancellationToken);

        Task<IReadOnlyList<FareRequestRow>> ListFareRequestsAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<VersionHistoryRow>> GetVersionHistoryAsync(
            string tourCode, CancellationToken cancellationToken);

        Task<FlightSheetData> GetFlightSheetAsync(string tourCode, CancellationToken cancellationToken);

        Task<WorkingColumnActorRow> GetWorkingColumnActorAsync(
            string tourCode, CancellationToken cancellationToken);

        Task SaveFlightDetailsAsync(
            string tourCode, IReadOnlyList<FlightDetailEntry> details, string changedBy,
            CancellationToken cancellationToken);

        Task<VersionSnapshotData> GetVersionSnapshotAsync(
            string tourCode, string version, CancellationToken cancellationToken);

        Task<IReadOnlyList<ActivityRow>> GetActivityAsync(
            string tourCode, int take, CancellationToken cancellationToken);

        Task<IReadOnlyList<ActivityRow>> GetChangesSinceAsync(
            string tourCode, DateTime since, CancellationToken cancellationToken);

        Task<DateTime?> GetTourStampAsync(string tourCode, CancellationToken cancellationToken);

        Task<IReadOnlyList<PreviewCellRow>> PreviewPricesAsync(
            string tourCode, IReadOnlyList<FareEntry> fares, CancellationToken cancellationToken);

        Task AddHubAsync(
            string tourCode, string hubCode, decimal? markupPct, CancellationToken cancellationToken,
            bool copyDates = true);

        Task UpdateHubAsync(
            string tourCode, string hubCode, decimal? markupPct, bool? isActive,
            CancellationToken cancellationToken);

        Task AddDepartureAsync(
            string tourCode, string hubCode, DateTime departureDate, CancellationToken cancellationToken);

        Task SetDepartureActiveAsync(
            string tourCode, string hubCode, DateTime departureDate, bool isActive,
            CancellationToken cancellationToken);

        Task RecordSubmittedPricesAsync(
            string tourCode, string changeSetReference, IReadOnlyList<PriceEntry> prices,
            string submittedBy, CancellationToken cancellationToken);

        Task<AdoptOutcome> AdoptCalculatedForHubAsync(
            string tourCode, string hubCode, string actedBy, CancellationToken cancellationToken);

        Task ConfirmPriceCellsAsync(
            string tourCode, IReadOnlyList<PriceCellRef> cells, string confirmedBy,
            CancellationToken cancellationToken);

        Task SetConditionsAsync(
            string tourCode, IReadOnlyList<string> hubCodes, IReadOnlyList<PriceCellRef> cells,
            Actor actor, CancellationToken cancellationToken, DateTime? expectedModifiedUtc = null);

        Task RaiseFareQueryAsync(
            string tourCode, string hubCode, DateTime departureDate, string fareBandCode,
            string reason, string raisedBy, CancellationToken cancellationToken);

        Task ResolveFareQueryAsync(
            string tourCode, int fareQueryId, string note, string resolvedBy,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<FareQueryRow>> GetFareQueriesAsync(
            string tourCode, bool openOnly, CancellationToken cancellationToken);
    }

    /// <summary>
    /// All database access for International GIT pricing: tours, hubs,
    /// departures, cost build, airfare, prices and their history.
    /// </summary>
    /// <remarks>
    /// Every method calls one stored procedure in schema "intlgit" with
    /// ADO.NET. There is no SQL text here: the query logic - including the
    /// price formula, intlgit.fn_PriceMatrix - belongs to the database.
    /// </remarks>
    public sealed class TourRepository : ITourRepository
    {
        private readonly SqlDatabase _db;

        public TourRepository(SqlDatabase db)
        {
            _db = db;
        }

        /// <summary>
        /// Who is making the change, for the activity log the procedure writes.
        /// A null actor leaves the parameters off, and the procedures then log
        /// nothing - inventing a person would put fiction in an audit trail.
        /// </summary>
        private static void AddActor(SqlParameterCollection p, Actor actor)
        {
            if (actor == null)
            {
                return;
            }

            p.Value("ChangedBy", actor.Name);
            p.Value("ActorRole", actor.Role);
        }

        /// <summary>
        /// The tour as the page saw it, for the stale-write guard. Null is sent
        /// as NULL, which the procedures read as "not using the guard".
        /// </summary>
        private static void AddGuard(SqlParameterCollection p, DateTime? expectedModifiedUtc)
        {
            p.DateTime2("ExpectedModifiedUtc", expectedModifiedUtc);
        }

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        public async Task<IReadOnlyList<TourListRow>> ListToursAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<TourListRow>("intlgit.usp_GetTourList", null, cancellationToken);
        }

        public Task<TourRevisionData> GetRevisionAsync(string tourCode, CancellationToken cancellationToken)
        {
            return _db.QueryMultipleAsync(
                "intlgit.usp_GetTourRevision",
                p => p.Value("TourCode", tourCode),
                async sets =>
                {
                    // An unknown code raises 50001 in the procedure, so reaching
                    // here means the tour exists.
                    var header = (await sets.ReadAsync<CostBuildHeaderRow>()).FirstOrDefault();
                    if (header == null)
                    {
                        return null;
                    }

                    return new TourRevisionData
                    {
                        Header = header,
                        Costs = await sets.ReadAsync<OccupancyCostRow>(),
                        Hubs = await sets.ReadAsync<HubRow>(),
                        Fares = await sets.ReadAsync<DepartureFareRow>(),
                        Prices = await sets.ReadAsync<PriceCellRow>()
                    };
                },
                cancellationToken);
        }

        public Task<PendingChangesData> GetPendingChangesAsync(string tourCode, CancellationToken cancellationToken)
        {
            return _db.QueryMultipleAsync(
                "intlgit.usp_GetPendingChanges",
                p => p.Value("TourCode", tourCode),
                async sets => new PendingChangesData
                {
                    Prices = await sets.ReadAsync<PendingPriceRow>(),
                    Removals = await sets.ReadAsync<PendingRemovalRow>(),

                    // What the change list deliberately leaves out, read in the
                    // same call so no screen can show the list without also
                    // being able to say what is missing from it.
                    Held = await sets.ReadAsync<FareQueryRow>(),
                    Unpriced = await sets.ReadAsync<UnpricedDepartureRow>()
                },
                cancellationToken);
        }

        public async Task<IReadOnlyList<HubMasterRow>> ListHubsAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<HubMasterRow>("intlgit.usp_GetHubMaster", null, cancellationToken);
        }

        public Task<DateTime?> GetTourStampAsync(string tourCode, CancellationToken cancellationToken)
        {
            return _db.QueryValueOrDefaultAsync<DateTime?>(
                "intlgit.usp_GetTourStamp", p => p.Value("TourCode", tourCode), cancellationToken);
        }

        public async Task<IReadOnlyList<ActivityRow>> GetChangesSinceAsync(
            string tourCode, DateTime since, CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<ActivityRow>(
                "intlgit.usp_GetChangesSince",
                p => p.Value("TourCode", tourCode).Value("Since", since),
                cancellationToken);
        }

        public async Task<IReadOnlyList<FareRequestRow>> ListFareRequestsAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<FareRequestRow>("intlgit.usp_ListFareRequests", null, cancellationToken);
        }

        public Task<FlightSheetData> GetFlightSheetAsync(string tourCode, CancellationToken cancellationToken)
        {
            return _db.QueryMultipleAsync(
                "intlgit.usp_GetFlightSheet",
                p => p.Value("TourCode", tourCode),
                async sets =>
                {
                    var tour = (await sets.ReadAsync<FlightSheetTourRow>()).FirstOrDefault();
                    var submissions = await sets.ReadAsync<FlightSheetSubmissionRow>();
                    var lines = await sets.ReadAsync<FlightSheetLineRow>();

                    if (tour == null)
                    {
                        return null;
                    }

                    return new FlightSheetData { Tour = tour, Submissions = submissions, Lines = lines };
                },
                cancellationToken);
        }

        public async Task<WorkingColumnActorRow> GetWorkingColumnActorAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            var rows = await _db.QueryAsync<WorkingColumnActorRow>(
                "intlgit.usp_GetWorkingColumnActor", p => p.Value("TourCode", tourCode), cancellationToken);
            return rows.FirstOrDefault();
        }

        public async Task<IReadOnlyList<VersionHistoryRow>> GetVersionHistoryAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<VersionHistoryRow>(
                "intlgit.usp_GetVersionHistory", p => p.Value("TourCode", tourCode), cancellationToken);
        }

        public Task<VersionSnapshotData> GetVersionSnapshotAsync(
            string tourCode, string version, CancellationToken cancellationToken)
        {
            return _db.QueryMultipleAsync(
                "intlgit.usp_GetVersionSnapshot",
                p => p.Value("TourCode", tourCode).Value("Version", version),
                async sets =>
                {
                    /* An unknown version is not an error: versions raised
                       before the snapshot existed have no photograph. Every set
                       is still read before deciding. */
                    var header = (await sets.ReadAsync<VersionSnapshotHeaderRow>()).FirstOrDefault();
                    var occupancies = await sets.ReadAsync<VersionSnapshotOccupancyRow>();
                    var hubs = await sets.ReadAsync<VersionSnapshotHubRow>();
                    var departures = await sets.ReadAsync<VersionSnapshotDepartureRow>();
                    var fares = await sets.ReadAsync<VersionSnapshotFareRow>();
                    var prices = await sets.ReadAsync<VersionSnapshotPriceRow>();

                    if (header == null)
                    {
                        return null;
                    }

                    return new VersionSnapshotData
                    {
                        Header = header,
                        Occupancies = occupancies,
                        Hubs = hubs,
                        Departures = departures,
                        Fares = fares,
                        Prices = prices
                    };
                },
                cancellationToken);
        }

        public async Task<IReadOnlyList<ActivityRow>> GetActivityAsync(
            string tourCode, int take, CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<ActivityRow>(
                "intlgit.usp_GetTourActivity",
                p => p.Value("TourCode", tourCode).Value("Take", take),
                cancellationToken);
        }

        public async Task<IReadOnlyList<PreviewCellRow>> PreviewPricesAsync(
            string tourCode, IReadOnlyList<FareEntry> fares, CancellationToken cancellationToken)
        {
            if (fares.Count == 0)
            {
                return new List<PreviewCellRow>();
            }

            return await _db.QueryAsync<PreviewCellRow>(
                "intlgit.usp_PreviewPrices",
                p => p.Value("TourCode", tourCode).Table("Fares", "intlgit.FareEditList", FareTable(fares)),
                cancellationToken);
        }

        public async Task<IReadOnlyList<FareQueryRow>> GetFareQueriesAsync(
            string tourCode, bool openOnly, CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<FareQueryRow>(
                "intlgit.usp_GetFareQueries",
                p => p.Value("TourCode", tourCode).Value("OpenOnly", openOnly),
                cancellationToken);
        }

        // ------------------------------------------------------------------
        // Cost build, fares, prices
        // ------------------------------------------------------------------

        public Task SaveCostBuildAsync(
            SaveCostBuildCommand command, Actor actor, CancellationToken cancellationToken,
            DateTime? expectedModifiedUtc = null)
        {
            // AllowDBNull stated: a cost nobody has entered crosses as absent,
            // never as a nought.
            var costs = new DataTable();
            costs.Columns.Add("OccupancyCode", typeof(string));
            costs.Columns.Add("LandCostFx", typeof(decimal)).AllowDBNull = true;
            costs.Columns.Add("PerPersonInr", typeof(decimal)).AllowDBNull = true;
            foreach (var cost in command.Costs)
            {
                costs.Rows.Add(
                    cost.OccupancyCode,
                    (object)cost.LandCostFx ?? DBNull.Value,
                    (object)cost.PerPersonInr ?? DBNull.Value);
            }

            return _db.ExecuteAsync("intlgit.usp_SaveCostBuild", p =>
            {
                p.Value("TourCode", command.TourCode);
                p.Value("FxRate", command.FxRate);
                p.Value("StrikePct", command.StrikePct);
                p.Value("PaxSlab", command.PaxSlab);
                p.Value("SharedCost", command.SharedCost);
                p.Value("Note", command.Note);
                p.Table("Costs", "intlgit.OccupancyCostList", costs);
                AddActor(p, actor);
                AddGuard(p, expectedModifiedUtc);
            }, cancellationToken);
        }

        public Task SavePublishedPricesAsync(
            string tourCode, IReadOnlyList<PriceEntry> prices, Actor actor,
            CancellationToken cancellationToken, DateTime? expectedModifiedUtc = null)
        {
            if (prices.Count == 0)
            {
                return Task.CompletedTask;
            }

            /* Two lists, because they are two instructions: a figure goes to
               @Prices and is written; a cell with no figure goes to @Cleared and
               is taken back. */
            var table = PriceTableType();
            var cleared = CellTableType();

            foreach (var price in prices)
            {
                if (price.Price.HasValue)
                {
                    table.Rows.Add(price.HubCode, price.DepartureDate.Date, price.OccupancyCode, price.Price.Value);
                }
                else
                {
                    cleared.Rows.Add(price.HubCode, price.DepartureDate.Date, price.OccupancyCode);
                }
            }

            return _db.ExecuteAsync("intlgit.usp_SavePublishedPrices", p =>
            {
                p.Value("TourCode", tourCode);
                p.Table("Prices", "intlgit.PriceList", table);
                p.Table("Cleared", "intlgit.PriceCellList", cleared);
                AddActor(p, actor);
                AddGuard(p, expectedModifiedUtc);
            }, cancellationToken);
        }

        public Task SaveFaresAsync(
            string tourCode, IReadOnlyList<FareEntry> fares, Actor actor,
            CancellationToken cancellationToken, DateTime? expectedModifiedUtc = null)
        {
            if (fares.Count == 0)
            {
                return Task.CompletedTask;
            }

            return _db.ExecuteAsync("intlgit.usp_SaveFares", p =>
            {
                p.Value("TourCode", tourCode);
                p.Table("Fares", "intlgit.FareEditList", FareTable(fares));
                AddActor(p, actor);
                AddGuard(p, expectedModifiedUtc);
            }, cancellationToken);
        }

        public Task SubmitFaresAsync(
            string tourCode, string submittedBy, string note, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync(
                "intlgit.usp_SubmitFares",
                p => p.Value("TourCode", tourCode).Value("SubmittedBy", submittedBy).Value("Note", note),
                cancellationToken);
        }

        public Task RequestFaresAsync(
            string tourCode, string requestedBy, string note, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync(
                "intlgit.usp_RequestFares",
                p => p.Value("TourCode", tourCode).Value("RequestedBy", requestedBy).Value("Note", note),
                cancellationToken);
        }

        public Task SaveFlightDetailsAsync(
            string tourCode, IReadOnlyList<FlightDetailEntry> details, string changedBy,
            CancellationToken cancellationToken)
        {
            var table = new DataTable();
            table.Columns.Add("HubCode", typeof(string));
            table.Columns.Add("DepartureDate", typeof(DateTime));
            table.Columns.Add("Details", typeof(string));

            foreach (var entry in details)
            {
                table.Rows.Add(entry.Hub, entry.Date, (object)entry.Details ?? DBNull.Value);
            }

            return _db.ExecuteAsync("intlgit.usp_SaveFlightDetails", p =>
            {
                p.Value("TourCode", tourCode);
                p.Table("Details", "intlgit.FlightDetailList", table);
                p.Value("ChangedBy", changedBy);
            }, cancellationToken);
        }

        public Task<AdoptOutcome> AdoptCalculatedForHubAsync(
            string tourCode, string hubCode, string actedBy, CancellationToken cancellationToken)
        {
            /* BlanksOnly, and KeepTyped off: only the blank cells are in scope,
               and a blank cell may still carry an override typed months ago and
               hidden since - sparing it would leave the stale number behind. */
            return _db.QuerySingleAsync<AdoptOutcome>("intlgit.usp_AdoptCalculatedPrices", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("HubCode", hubCode);
                p.Value("By", actedBy);
                p.Value("WhatIf", false);
                p.Value("KeepTyped", false);
                p.Value("BlanksOnly", true);
            }, cancellationToken);
        }

        public Task ConfirmPriceCellsAsync(
            string tourCode, IReadOnlyList<PriceCellRef> cells, string confirmedBy,
            CancellationToken cancellationToken)
        {
            if (cells.Count == 0)
            {
                return Task.CompletedTask;
            }

            return _db.ExecuteAsync("intlgit.usp_ConfirmPriceCells", p =>
            {
                p.Value("TourCode", tourCode);
                p.Table("Cells", "intlgit.PriceCellList", CellTable(cells));
                p.Value("ConfirmedBy", confirmedBy);
            }, cancellationToken);
        }

        public Task SetConditionsAsync(
            string tourCode, IReadOnlyList<string> hubCodes, IReadOnlyList<PriceCellRef> cells,
            Actor actor, CancellationToken cancellationToken, DateTime? expectedModifiedUtc = null)
        {
            if (hubCodes.Count == 0)
            {
                return Task.CompletedTask;
            }

            var hubs = new DataTable();
            hubs.Columns.Add("HubCode", typeof(string));
            foreach (var hub in hubCodes)
            {
                hubs.Rows.Add(hub);
            }

            // An empty cell list is meaningful and is still sent: it is how the
            // last star on a hub gets cleared.
            return _db.ExecuteAsync("intlgit.usp_SetConditions", p =>
            {
                p.Value("TourCode", tourCode);
                p.Table("Hubs", "intlgit.HubCodeList", hubs);
                p.Table("Cells", "intlgit.PriceCellList", CellTable(cells));
                if (actor != null)
                {
                    p.Value("ChangedBy", actor.Name);
                }

                AddGuard(p, expectedModifiedUtc);
            }, cancellationToken);
        }

        // ------------------------------------------------------------------
        // Structure
        // ------------------------------------------------------------------

        public Task AddHubAsync(
            string tourCode, string hubCode, decimal? markupPct, CancellationToken cancellationToken,
            bool copyDates = true)
        {
            return _db.ExecuteAsync("intlgit.usp_AddTourHub", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("HubCode", hubCode);
                p.Value("MarkupPct", markupPct);
                p.Value("CopyDates", copyDates);
            }, cancellationToken);
        }

        public Task UpdateHubAsync(
            string tourCode, string hubCode, decimal? markupPct, bool? isActive,
            CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("intlgit.usp_UpdateTourHub", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("HubCode", hubCode);
                p.Value("MarkupPct", markupPct);
                p.Value("IsActive", isActive);
            }, cancellationToken);
        }

        public Task AddDepartureAsync(
            string tourCode, string hubCode, DateTime departureDate, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("intlgit.usp_AddDeparture", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("HubCode", hubCode);
                p.Date("DepartureDate", departureDate);
            }, cancellationToken);
        }

        public Task SetDepartureActiveAsync(
            string tourCode, string hubCode, DateTime departureDate, bool isActive,
            CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("intlgit.usp_SetDepartureActive", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("HubCode", hubCode);
                p.Date("DepartureDate", departureDate);
                p.Value("IsActive", isActive);
            }, cancellationToken);
        }

        // ------------------------------------------------------------------
        // Fare hand-over and queries
        // ------------------------------------------------------------------

        public Task RaiseFareQueryAsync(
            string tourCode, string hubCode, DateTime departureDate, string fareBandCode,
            string reason, string raisedBy, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("intlgit.usp_RaiseFareQuery", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("HubCode", hubCode);
                p.Date("DepartureDate", departureDate);
                p.Value("FareBandCode", fareBandCode);
                p.Value("Reason", reason);
                p.Value("RaisedBy", raisedBy);
            }, cancellationToken);
        }

        public Task ResolveFareQueryAsync(
            string tourCode, int fareQueryId, string note, string resolvedBy,
            CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("intlgit.usp_ResolveFareQuery", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("FareQueryId", fareQueryId);
                p.Value("Note", note);
                p.Value("ResolvedBy", resolvedBy);
            }, cancellationToken);
        }

        // ------------------------------------------------------------------
        // Submission baseline
        // ------------------------------------------------------------------

        /// <summary>
        /// Records the prices a change set was submitted with, so further edits
        /// are measured against them rather than against what is still live.
        /// </summary>
        public async Task RecordSubmittedPricesAsync(
            string tourCode, string changeSetReference, IReadOnlyList<PriceEntry> prices,
            string submittedBy, CancellationToken cancellationToken)
        {
            // A set may be removals only, so an empty price list is still recorded.
            var table = PriceTableType();
            foreach (var price in prices)
            {
                table.Rows.Add(price.HubCode, price.DepartureDate.Date, price.OccupancyCode, price.Price);
            }

            await _db.ExecuteAsync("intlgit.usp_RecordSubmittedPrices", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("ChangeSetRef", changeSetReference);
                p.Table("Prices", "intlgit.PriceList", table);
            }, cancellationToken);

            // Removals need the same treatment, or a withdrawn hub would stay
            // pending after being sent and repeat in the next request.
            await _db.ExecuteAsync("intlgit.usp_RecordSubmittedRemovals", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("ChangeSetRef", changeSetReference);
            }, cancellationToken);

            // And tech support are told there is work waiting.
            await _db.ExecuteAsync("intlgit.usp_NotifyChangeSetSubmitted", p =>
            {
                p.Value("TourCode", tourCode);
                p.Value("ChangeSetRef", changeSetReference);
                p.Value("SubmittedBy", submittedBy);
            }, cancellationToken);
        }

        // ------------------------------------------------------------------
        // Table-valued parameter shapes (column order must match the SQL type)
        // ------------------------------------------------------------------

        /// <summary>intlgit.FareEditList. A null amount is "take this fare back".</summary>
        private static DataTable FareTable(IEnumerable<FareEntry> fares)
        {
            var table = new DataTable();
            table.Columns.Add("HubCode", typeof(string));
            table.Columns.Add("DepartureDate", typeof(DateTime));
            table.Columns.Add("FareBandCode", typeof(string));
            table.Columns.Add("Amount", typeof(decimal)).AllowDBNull = true;
            foreach (var fare in fares)
            {
                table.Rows.Add(
                    fare.HubCode, fare.DepartureDate.Date, fare.FareBandCode,
                    (object)fare.Amount ?? DBNull.Value);
            }

            return table;
        }

        /// <summary>intlgit.PriceList - (hub, date, occupancy, price).</summary>
        private static DataTable PriceTableType()
        {
            var table = new DataTable();
            table.Columns.Add("HubCode", typeof(string));
            table.Columns.Add("DepartureDate", typeof(DateTime));
            table.Columns.Add("OccupancyCode", typeof(string));
            table.Columns.Add("Price", typeof(decimal));
            return table;
        }

        /// <summary>intlgit.PriceCellList - (hub, date, occupancy).</summary>
        private static DataTable CellTableType()
        {
            var table = new DataTable();
            table.Columns.Add("HubCode", typeof(string));
            table.Columns.Add("DepartureDate", typeof(DateTime));
            table.Columns.Add("OccupancyCode", typeof(string));
            return table;
        }

        private static DataTable CellTable(IReadOnlyList<PriceCellRef> cells)
        {
            var table = CellTableType();
            foreach (var cell in cells)
            {
                table.Rows.Add(cell.HubCode, cell.DepartureDate.Date, cell.OccupancyCode);
            }

            return table;
        }
    }
}
