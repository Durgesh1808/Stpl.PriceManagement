using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.Core.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Areas.IntlGit.Models.Rows;
using Stpl.PriceManagement.Areas.IntlGit.Repositories;
using Stpl.PriceManagement.Infrastructure;
using Stpl.PriceManagement.Infrastructure.Data;
using Stpl.PriceManagement.Infrastructure.Validation;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Services
{
    /// <summary>
    /// Everything the International GIT screens ask of a tour: read it, and
    /// change it. Controllers call this; this calls <see cref="ITourRepository"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No pricing arithmetic happens here - that is defined once, in
    /// intlgit.fn_PriceMatrix. What this class does:
    /// </para>
    /// <list type="bullet">
    /// <item>checks the person's role may make the change (<see cref="TourPermissions"/>);</item>
    /// <item>checks the request is sane (<see cref="RequestValidators"/>);</item>
    /// <item>calls the stored procedure through the repository;</item>
    /// <item>turns a procedure's business error into a sentence for the person
    ///   (<see cref="ServiceResult"/>), and the stale-write guard into
    ///   <see cref="ServiceResult.IsStale"/>;</item>
    /// <item>shapes flat result sets into the nested form a screen draws.</item>
    /// </list>
    /// <para>
    /// This is what used to be spread over three layers - the web app's
    /// PricingApiClient, the Pricing API's controllers and filters, and the
    /// API's TourService - and it keeps exactly their rules and messages.
    /// </para>
    /// </remarks>
    public sealed class TourService
    {
        private readonly ITourRepository _repository;
        private readonly CurrentUser _user;
        private readonly ILogger<TourService> _logger;

        public TourService(ITourRepository repository, CurrentUser user, ILogger<TourService> logger)
        {
            _repository = repository;
            _user = user;
            _logger = logger;
        }

        /// <summary>
        /// Who is making this change, for the activity log: the PERSON, and
        /// their role's short code ("product", "airticketing", "techsupport").
        /// </summary>
        private Actor Acting()
        {
            var name = _user.DisplayName ?? _user.RoleName;
            var role = UserRoles.ShortCode(_user.Role);

            return new Actor(
                string.IsNullOrWhiteSpace(name) ? "Unknown user" : name,
                string.IsNullOrWhiteSpace(role) ? "system" : role);
        }

        // ==================================================================
        // Reads
        // ==================================================================

        /// <summary>Every tour, with what is pending against what is live.</summary>
        public Task<IReadOnlyList<TourSummaryResponse>> ListToursAsync(CancellationToken token)
        {
            return ReadAsync(() => ListCoreAsync(token), (IReadOnlyList<TourSummaryResponse>)new List<TourSummaryResponse>());
        }

        /// <summary>The hub master, for the Add hub picker.</summary>
        public Task<IReadOnlyList<HubMasterResponse>> ListHubsAsync(CancellationToken token)
        {
            return ReadAsync(() => ListHubsCoreAsync(token), (IReadOnlyList<HubMasterResponse>)new List<HubMasterResponse>());
        }

        /// <summary>The whole price revision for one tour. Null when there is no tour with that code.</summary>
        public Task<TourRevisionResponse> GetRevisionAsync(string code, CancellationToken token)
        {
            return ReadAsync(() => GetRevisionCoreAsync(code, token), null);
        }

        /// <summary>The change list this revision would hand to tech support. Null for an unknown tour.</summary>
        public Task<PendingChangesResponse> GetPendingChangesAsync(string code, CancellationToken token)
        {
            return ReadAsync(() => GetPendingChangesCoreAsync(code, token), null);
        }

        /// <summary>The airfare summary sheet for one tour. Null when there is no such tour.</summary>
        public Task<FlightSheetResponse> GetFlightSheetAsync(string code, CancellationToken token)
        {
            return ReadAsync(() => GetFlightSheetCoreAsync(code, token), null);
        }

        /// <summary>
        /// The tour's stale-write stamp. Re-read between the writes of one save,
        /// so a batch does not refuse itself.
        /// </summary>
        public Task<DateTime?> GetTourStampAsync(string code, CancellationToken token)
        {
            return ReadAsync(() => GetTourStampCoreAsync(code, token), null);
        }

        /// <summary>Who changed what since a moment, for a refused save to show.</summary>
        public Task<IReadOnlyList<ActivityResponse>> GetChangesSinceAsync(
            string code, DateTime since, CancellationToken token)
        {
            return ReadAsync(() => GetChangesSinceCoreAsync(code, since, token), (IReadOnlyList<ActivityResponse>)new List<ActivityResponse>());
        }

        /// <summary>Every version this tour has been on, newest first.</summary>
        public Task<IReadOnlyList<VersionHistoryEntryResponse>> GetVersionHistoryAsync(
            string code, CancellationToken token)
        {
            return ReadAsync(() => GetVersionHistoryCoreAsync(code, token), (IReadOnlyList<VersionHistoryEntryResponse>)new List<VersionHistoryEntryResponse>());
        }

        /// <summary>
        /// The product page as it stood at that version, or null when there is
        /// no photograph of it (every version raised before snapshots existed).
        /// </summary>
        public Task<VersionSnapshotResponse> GetVersionSnapshotAsync(
            string code, string version, CancellationToken token)
        {
            return ReadAsync(() => GetVersionSnapshotCoreAsync(code, version, token), null);
        }

        /// <summary>Fares sent back on this tour, open ones by default.</summary>
        public Task<IReadOnlyList<FareQueryResponse>> GetFareQueriesAsync(
            string code, CancellationToken token, bool openOnly = true)
        {
            return ReadAsync(() => GetFareQueriesCoreAsync(code, openOnly, token), (IReadOnlyList<FareQueryResponse>)new List<FareQueryResponse>());
        }

        /// <summary>Who changed what on this tour, newest first (latest 200).</summary>
        public Task<IReadOnlyList<ActivityResponse>> GetActivityAsync(string code, CancellationToken token)
        {
            return ReadAsync(() => GetActivityCoreAsync(code, 200, token), (IReadOnlyList<ActivityResponse>)new List<ActivityResponse>());
        }

        /// <summary>Every tour air-ticketing is being waited on for.</summary>
        public Task<IReadOnlyList<FareRequestResponse>> ListFareRequestsAsync(CancellationToken token)
        {
            return ReadAsync(() => ListFareRequestsCoreAsync(token), (IReadOnlyList<FareRequestResponse>)new List<FareRequestResponse>());
        }

        /// <summary>
        /// What the prices would be with the supplied fares. Nothing is saved -
        /// this exists so the grid can show the effect of a fare as it is typed,
        /// answered by the same SQL function that produces the real prices.
        /// </summary>
        /// <remarks>
        /// The margin preview is the product team's (SeeCostAndMargin). Anybody
        /// else, or any failure, gets an empty answer and the grid simply does
        /// not repaint - exactly how the old API call behaved.
        /// </remarks>
        public async Task<IReadOnlyList<PreviewCellResponse>> PreviewPricesAsync(
            string code, PreviewPricesRequest request, CancellationToken token)
        {
            if (!TourPermissions.IsAllowed(_user.Role, TourPermission.SeeCostAndMargin))
            {
                return new List<PreviewCellResponse>();
            }

            try
            {
                return await PreviewPricesCoreAsync(code, request ?? new PreviewPricesRequest(), token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogWarning(ex, "Price preview for {Code} failed.", code);
                return new List<PreviewCellResponse>();
            }
        }

        // ==================================================================
        // Writes - each answers with a ServiceResult the screen can show
        // ==================================================================

        /// <summary>FX, strike, pax slab, shared cost and the per-occupancy costs.</summary>
        public Task<ServiceResult> SaveCostBuildAsync(string code, SaveCostBuildRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditCostBuild, RequestValidators.Validate(request),
                () => SaveCostBuildCoreAsync(code, request, Acting(), token), "save the cost build", code);
        }

        /// <summary>Published selling prices, typed over the calculated ones.</summary>
        public Task<ServiceResult> SavePricesAsync(string code, SavePricesRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditPublishedPrice, RequestValidators.Validate(request),
                () => SavePricesCoreAsync(code, request, Acting(), token), "save prices", code);
        }

        /// <summary>Airfare per hub, departure and band.</summary>
        public Task<ServiceResult> SaveFaresAsync(string code, SaveFaresRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditFares, RequestValidators.Validate(request),
                () => SaveFaresCoreAsync(code, request, Acting(), token), "save fares", code);
        }

        /// <summary>
        /// "Same as calculated SP" for one hub: fills its blank cells with the
        /// calculated figure and agrees to them. An act, not a setting.
        /// </summary>
        public Task<ServiceResult> AdoptCalculatedForHubAsync(string code, string hub, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditPublishedPrice, null,
                () => AdoptCalculatedForHubCoreAsync(code, hub, Acting(), token), "adopt calculated prices", code);
        }

        /// <summary>Records that somebody has reviewed these prices and agreed to them.</summary>
        public Task<ServiceResult> ConfirmPricesAsync(string code, ConfirmPricesRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditPublishedPrice, null,
                () => ConfirmPricesCoreAsync(code, request, Acting(), token), "confirm prices", code);
        }

        /// <summary>
        /// Sends one fare back to air-ticketing with a reason, holding its
        /// departure out of the change set until they answer.
        /// </summary>
        public Task<ServiceResult> RaiseFareQueryAsync(string code, RaiseFareQueryRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.RaiseFareQuery, RequestValidators.Validate(request),
                () => RaiseFareQueryCoreAsync(code, request, Acting(), token), "send a fare back", code);
        }

        /// <summary>Air-ticketing standing by a figure, which releases the departure.</summary>
        public Task<ServiceResult> ResolveFareQueryAsync(string code, ResolveFareQueryRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.ResolveFareQuery, RequestValidators.Validate(request),
                () => ResolveFareQueryCoreAsync(code, request, Acting(), token), "resolve a fare query", code);
        }

        /// <summary>Saves air-ticketing's flight notes for departures.</summary>
        public Task<ServiceResult> SaveFlightDetailsAsync(string code, SaveFlightDetailsRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditFares, RequestValidators.Validate(request),
                () => SaveFlightDetailsCoreAsync(code, request, Acting(), token), "save flight details", code);
        }

        /// <summary>
        /// Sets which of the shown hubs' prices carry conditions. The list is the
        /// complete answer for those hubs; anything absent loses its star.
        /// </summary>
        public Task<ServiceResult> SetConditionsAsync(string code, SetConditionsRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.EditPublishedPrice, null,
                () => SetConditionsCoreAsync(code, request, Acting(), token), "set conditions", code);
        }

        /// <summary>
        /// Records what a change set was submitted with, so any further edit
        /// becomes a new request containing only what changed since - and tells
        /// tech support there is work waiting.
        /// </summary>
        public Task<ServiceResult> MarkSubmittedAsync(string code, RecordSubmittedRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.SubmitChangeSet, null,
                () => RecordSubmittedCoreAsync(code, request, Acting(), token), "record the submitted baseline", code);
        }

        /// <summary>Air-ticketing hands its fares to the product team, with a note.</summary>
        public Task<ServiceResult> SubmitFaresAsync(string code, string note, CancellationToken token)
        {
            var request = new SubmitFaresRequest { Note = note };
            var submittedBy = _user.DisplayName ?? _user.RoleName;
            if (string.IsNullOrWhiteSpace(submittedBy))
            {
                submittedBy = "Air-ticketing executive";
            }

            return WriteAsync(TourPermission.SubmitFares, RequestValidators.Validate(request),
                () => SubmitFaresCoreAsync(code, submittedBy, request.Note, token), "submit fares", code);
        }

        /// <summary>
        /// The product team asking air-ticketing for fares on a tour. Asking again
        /// while a request is open updates it rather than raising a second one.
        /// </summary>
        public Task<ServiceResult> RequestFaresAsync(string code, string note, CancellationToken token)
        {
            var request = new RequestFaresRequest { Note = note };
            var requestedBy = _user.DisplayName ?? _user.RoleName;
            if (string.IsNullOrWhiteSpace(requestedBy))
            {
                requestedBy = "Product executive";
            }

            return WriteAsync(TourPermission.RequestFares, RequestValidators.Validate(request),
                () => RequestFaresCoreAsync(code, requestedBy, request.Note, token), "request fares", code);
        }

        /// <summary>Adds a hub, with or without the tour's existing departure dates.</summary>
        public Task<ServiceResult> AddHubAsync(string code, AddHubRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.ChangeStructure, RequestValidators.Validate(request),
                () => AddHubCoreAsync(code, request, token), "add a hub", code);
        }

        /// <summary>Changes a hub's markup, or withdraws and restores it.</summary>
        public Task<ServiceResult> UpdateHubAsync(string code, string hub, UpdateHubRequest request, CancellationToken token)
        {
            return WriteAsync(TourPermission.ChangeStructure, RequestValidators.Validate(request),
                () => UpdateHubCoreAsync(code, hub, request, token), "update a hub", code);
        }

        /// <summary>Adds a departure date to one hub (and to Joining / Leaving - see usp_AddDeparture).</summary>
        public Task<ServiceResult> AddDepartureAsync(string code, string hub, DateTime date, CancellationToken token)
        {
            var request = new AddDepartureRequest { Date = date };
            return WriteAsync(TourPermission.ChangeStructure, RequestValidators.Validate(request),
                () => AddDepartureCoreAsync(code, hub, request.Date, token), "add a departure", code);
        }

        /// <summary>Withdraws or restores one departure.</summary>
        public Task<ServiceResult> SetDepartureActiveAsync(
            string code, string hub, DateTime date, bool isActive, CancellationToken token)
        {
            return WriteAsync(TourPermission.ChangeStructure, null,
                () => SetDepartureActiveCoreAsync(code, hub, date, isActive, token), "withdraw or restore a departure", code);
        }

        // ==================================================================
        // How a read and a write fail
        // ==================================================================

        /// <summary>
        /// A read. "No such tour" (and the other not-found errors the procedures
        /// raise) answers <paramref name="whenNotFound"/>; anything else is a
        /// fault and goes to the error page.
        /// </summary>
        private static async Task<T> ReadAsync<T>(Func<Task<T>> read, T whenNotFound)
        {
            try
            {
                return await read();
            }
            catch (SqlException ex) when (ProcedureErrors.IsNotFound(ex))
            {
                return whenNotFound;
            }
        }

        /// <summary>
        /// A write: the role check, then the request check, then the procedure.
        /// </summary>
        /// <remarks>
        /// The role is checked BEFORE the request, so somebody who may not make
        /// a change is told so rather than given a field-by-field account of
        /// what the request should have looked like.
        ///
        /// A procedure's own error (50001-50099, 60001-60099) carries a sentence
        /// written for a person and is passed on. The stale-write guard (50030)
        /// is flagged separately, because the screen answers it by showing what
        /// moved and keeping what was typed. Anything else is logged and the
        /// person sees a generic sentence - never SQL or a stack trace.
        /// </remarks>
        private async Task<ServiceResult> WriteAsync(
            TourPermission? permission, ValidationErrors validation, Func<Task> write, string what, string code)
        {
            if (permission.HasValue && !TourPermissions.IsAllowed(_user.Role, permission.Value))
            {
                _logger.LogInformation(
                    "Refused to {What} on {Code} for role {Role}: {Permission} not held.",
                    what, code, _user.Role, permission.Value);
                return ServiceResult.Failed(ServiceResult.ForbiddenMessage);
            }

            if (validation != null && validation.HasErrors)
            {
                return ServiceResult.Failed(validation.Message);
            }

            try
            {
                await write();
                return ServiceResult.Ok();
            }
            catch (SqlException ex) when (ProcedureErrors.IsBusinessRule(ex))
            {
                _logger.LogWarning(
                    "Business rule {ErrorNumber} rejected the attempt to {What} on {Code}: {Message}",
                    ex.Number, what, code, ex.Message);

                return ProcedureErrors.IsStaleWrite(ex)
                    ? ServiceResult.Stale(ex.Message)
                    : ServiceResult.Failed(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not {What} on {Code}.", what, code);
                return ServiceResult.Failed(ServiceResult.UnexpectedMessage);
            }
        }

        // ==================================================================
        // The work itself: calls into the repository and shapes the answers.
        // (Unchanged from the Pricing API's TourService.)
        // ==================================================================

        /// <summary>
        /// Records that somebody has looked at these prices and agreed to them -
        /// exactly the cells named. (The grid only ever names cells; the old
        /// "confirm a whole hub / date / tour" form no longer exists.)
        /// </summary>
        private async Task<ConfirmedCountResponse> ConfirmPricesCoreAsync(
            string tourCode, ConfirmPricesRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            var confirmedBy = actor == null ? "Unknown" : actor.Name;
            var cells = ToCellRefs(request.Cells ?? new List<PriceCellRequest>());

            await _repository.ConfirmPriceCellsAsync(tourCode, cells, confirmedBy, cancellationToken);

            // The procedure silently drops queried cells, so what it wrote is
            // not necessarily what was asked for.
            return new ConfirmedCountResponse { Confirmed = cells.Count };
        }


        private async Task<IReadOnlyList<TourSummaryResponse>> ListCoreAsync(CancellationToken cancellationToken)
        {
            var rows = await _repository.ListToursAsync(cancellationToken);

            return rows.Select(row => new TourSummaryResponse
            {
                Code = row.Code,
                Name = row.Name,
                Region = row.Region,
                Duration = row.Duration,
                Version = row.CurrentVersion,
                HubCount = row.HubCount,
                DepartureCount = row.DepartureCount,
                PendingPriceRows = row.PendingPriceRows,
                PendingRemovals = row.PendingRemovals,
                PendingChanges = row.PendingChanges,
                FaresSubmittedUtc = row.FareSubmittedUtc,
                AwaitingFares = row.AwaitingFares,

                /* Dropped here until round G. usp_GetTourList has counted this
                   correctly since the fare query was built, Dapper has filled
                   it on every call, and it died at this boundary - so the
                   products list never had a chance to show a send-back. */
                HeldDepartures = row.HeldDepartures,
                FareRequestedUtc = row.FareRequestedUtc,
                QueriedDepartures = row.QueriedDepartures,

                LastSavedUtc = row.SavedUtc,
                LastModifiedUtc = row.ModifiedUtc
            }).ToList();
        }


        private async Task<IReadOnlyList<HubMasterResponse>> ListHubsCoreAsync(CancellationToken cancellationToken)
        {
            var rows = await _repository.ListHubsAsync(cancellationToken);

            return rows.Select(row => new HubMasterResponse
            {
                Hub = row.Code,
                Name = row.Name,
                DefaultMarkupPercent = row.DefaultMarkupPct,
                HasPassengerAirfare = row.HasPassengerAirfare
            }).ToList();
        }


        /// <summary>Returns null when there is no tour with that code.</summary>
        private async Task<TourRevisionResponse> GetRevisionCoreAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            var data = await _repository.GetRevisionAsync(tourCode, cancellationToken);
            if (data == null)
            {
                return null;
            }

            var header = data.Header;

            /* The shared cost is spread across the launch pax slab, so each
               occupancy carries the same share of it.

               No slab means the share is UNKNOWN, not nought. Zero here would
               quietly drop the whole group cost out of the cost-per-person the
               product team reads, and leave a total that looks complete -
               while fn_PriceMatrix, which divides the same two figures, would
               say something else about the same tour. */
            var sharedPerPerson = header.PaxSlab > 0
                ? header.SharedCost / header.PaxSlab
                : (decimal?)null;

            // One row per hub, date and fare band comes back flat; fold it into
            // the departure-with-its-fares shape the workspace draws.
            var departuresByHub = data.Fares
                .GroupBy(f => f.HubCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(f => new { f.DepartureDate, f.IsActive })
                          .Select(d => new DepartureResponse
                          {
                              Date = d.Key.DepartureDate,
                              IsActive = d.Key.IsActive,
                              Fares = d.ToDictionary(
                                  f => f.FareBandCode, f => f.Airfare, StringComparer.OrdinalIgnoreCase),
                              PreviousFares = d.ToDictionary(
                                  f => f.FareBandCode, f => f.PreviousAirfare, StringComparer.OrdinalIgnoreCase),
                              HasAllFares = d.All(f => f.Airfare.HasValue),
                              QueriedFares = d.ToDictionary(
                                  f => f.FareBandCode, f => f.IsQueried, StringComparer.OrdinalIgnoreCase),
                              IsQueried = d.Any(f => f.IsQueried),

                              // Belongs to the departure, so every band row in
                              // this group carries the same value - take the
                              // first that has one rather than assuming an order.
                              FlightDetails = d
                                  .Select(f => f.FlightDetails)
                                  .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                          })
                          .OrderBy(d => d.Date)
                          .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            return new TourRevisionResponse
            {
                Code = header.Code,
                Name = header.Name,
                Region = header.Region,
                Duration = header.Duration,
                Version = header.CurrentVersion,
                FaresSubmittedUtc = header.FareSubmittedUtc,
                FaresSubmittedBy = header.FareSubmittedBy,
                FareNote = header.FareNote,
                LastSavedUtc = header.SavedUtc,
                ModifiedUtc = header.ModifiedUtc,
                CostBuild = new CostBuildResponse
                {
                    FxRate = header.FxRate,
                    PreviousFxRate = header.PreviousFxRate,
                    StrikePercent = header.StrikePct,
                    PaxSlab = header.PaxSlab,
                    SharedCost = header.SharedCost,
                    Note = header.Note,
                    Occupancies = data.Costs.Select(cost => new OccupancyCostResponse
                    {
                        Occupancy = cost.OccupancyCode,
                        Label = cost.OccupancyLabel,
                        SortOrder = cost.SortOrder,
                        LandCostFx = cost.LandCostFx,
                        PerPersonInr = cost.PerPersonInr,
                        TotalCost = cost.LandCostFx * header.FxRate + cost.PerPersonInr + sharedPerPerson
                    }).ToList()
                },
                Hubs = data.Hubs.Select(hub => new HubResponse
                {
                    Hub = hub.HubCode,
                    Name = hub.HubName,
                    MarkupPercent = hub.MarkupPct,
                    IsActive = hub.IsActive,
                    ActiveDepartures = hub.DepartureCount,
                    IsNewHub = hub.IsNewHub,
                    HasPassengerAirfare = hub.HasPassengerAirfare,
                    Departures = departuresByHub.ContainsKey(hub.HubCode)
                        ? departuresByHub[hub.HubCode]
                        : new List<DepartureResponse>()
                }).ToList(),
                Prices = data.Prices.Select(price => new PriceCellResponse
                {
                    Hub = price.HubCode,
                    Date = price.DepartureDate,
                    Occupancy = price.OccupancyCode,
                    SortOrder = price.OccupancySort,
                    Calculated = price.CalculatedPrice,
                    Published = price.PublishedPrice,
                    StrikeThrough = price.StrikeThrough,
                    IsOverridden = price.IsOverridden,
                    LivePrice = price.LivePrice,
                    IsNewToSite = price.IsNewToSite,
                    BaselinePrice = price.BaselinePrice,
                    HasFare = price.HasFare,
                    ChangeState = price.ChangeState,
                    HasConditions = price.HasCondition,
                    IsConfirmed = price.IsConfirmed,
                    ConfirmedBy = price.ConfirmedBy,
                    ConfirmedUtc = price.ConfirmedUtc,
                    IsQueried = price.IsQueried
                }).ToList()
            };
        }


        private async Task<PendingChangesResponse> GetPendingChangesCoreAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            var data = await _repository.GetPendingChangesAsync(tourCode, cancellationToken);

            var hubs = data.Prices
                .GroupBy(p => new { p.HubCode, p.HubName, p.IsNewHub })
                .Select(hubGroup => new PendingHubResponse
                {
                    Hub = hubGroup.Key.HubCode,
                    Name = hubGroup.Key.HubName,
                    IsNewHub = hubGroup.Key.IsNewHub,
                    Departures = hubGroup
                        .GroupBy(p => p.DepartureDate)
                        .Select(dateGroup => new PendingDepartureResponse
                        {
                            Date = dateGroup.Key,
                            Cells = dateGroup
                                .OrderBy(c => c.OccupancySort)
                                .Select(c => new PendingCellResponse
                                {
                                    Occupancy = c.OccupancyCode,
                                    SortOrder = c.OccupancySort,
                                    Published = c.PublishedPrice,
                                    StrikeThrough = c.StrikeThrough,
                                    LivePrice = c.LivePrice,
                                    BaselinePrice = c.BaselinePrice,
                                    HasConditions = c.HasCondition,
                                    // A row reaches this list because at least one
                                    // of its cells moved; the others come through
                                    // marked unchanged so tech support can see
                                    // which figures to leave alone.
                                    HasChanged = c.HasChanged
                                })
                                .ToList()
                        })
                        .OrderBy(d => d.Date)
                        .ToList()
                })
                .ToList();

            var removals = data.Removals.Select(removal => new PendingRemovalResponse
            {
                Kind = removal.Kind,
                Hub = removal.HubCode,
                Name = removal.HubName,
                Date = removal.DepartureDate,
                DepartureCount = removal.DepartureCount,
                LivePrice = removal.LivePrice
            }).ToList();

            // A "change" is one departure row or one removal - the unit tech
            // support ticks off, not the number of individual price cells.
            var priceRows = hubs.Sum(h => h.Departures.Count);

            // Held departures are NOT counted in TotalChanges. They are not
            // changes waiting to go out; they are changes that cannot, and
            // adding them would make the number on the products list mean two
            // different things at once.
            var held = (data.Held ?? new List<FareQueryRow>()).Select(ToQueryResponse).ToList();

            var unpriced = (data.Unpriced ?? new List<UnpricedDepartureRow>())
                .Select(row => new UnpricedDepartureResponse
                {
                    Hub = row.HubCode,
                    HubName = row.HubName,
                    Date = row.DepartureDate,
                    BlankCount = row.BlankCount,
                    CalculatedTotal = row.CalculatedTotal
                })
                .ToList();

            return new PendingChangesResponse
            {
                Code = tourCode,
                PriceRows = priceRows,
                TotalChanges = priceRows + removals.Count,
                Hubs = hubs,
                Removals = removals,
                Held = held,
                Unpriced = unpriced
            };
        }


        /// <summary>
        /// Sends one fare back to air-ticketing, holding its departure.
        /// </summary>
        private Task RaiseFareQueryCoreAsync(
            string tourCode, RaiseFareQueryRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.RaiseFareQueryAsync(
                tourCode,
                request.Hub,
                request.Date,
                string.IsNullOrWhiteSpace(request.Band) ? null : request.Band.ToLowerInvariant(),
                request.Reason,
                actor == null ? "Unknown" : actor.Name,
                cancellationToken);
        }


        private Task ResolveFareQueryCoreAsync(
            string tourCode, ResolveFareQueryRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.ResolveFareQueryAsync(
                tourCode,
                request.Id,
                request.Note,
                actor == null ? "Unknown" : actor.Name,
                cancellationToken);
        }


        private async Task<IReadOnlyList<FareQueryResponse>> GetFareQueriesCoreAsync(
            string tourCode, bool openOnly, CancellationToken cancellationToken)
        {
            var rows = await _repository.GetFareQueriesAsync(tourCode, openOnly, cancellationToken);
            return rows.Select(ToQueryResponse).ToList();
        }


        private static FareQueryResponse ToQueryResponse(FareQueryRow row)
        {
            return new FareQueryResponse
            {
                Id = row.FareQueryId,
                Hub = row.HubCode,
                HubName = row.HubName,
                Date = row.DepartureDate,
                Band = row.FareBandCode,
                BandLabel = row.FareBandLabel,
                Airfare = row.Airfare,
                RaisedUtc = row.RaisedUtc,
                RaisedBy = row.RaisedBy,
                Reason = row.Reason,
                ResolvedUtc = row.ResolvedUtc,
                ResolvedBy = row.ResolvedBy,
                ResolutionNote = row.ResolutionNote
            };
        }


        private Task SaveCostBuildCoreAsync(
            string tourCode, SaveCostBuildRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.SaveCostBuildAsync(new SaveCostBuildCommand
            {
                TourCode = tourCode,
                FxRate = request.FxRate,
                StrikePct = request.StrikePercent,
                PaxSlab = request.PaxSlab,
                SharedCost = request.SharedCost,
                Note = request.Note,
                Costs = request.Occupancies.Select(o => new OccupancyCostEntry
                {
                    OccupancyCode = o.Occupancy,
                    LandCostFx = o.LandCostFx,
                    PerPersonInr = o.PerPersonInr
                }).ToList()
            }, actor, cancellationToken, request.ExpectedModifiedUtc);
        }


        private Task SavePricesCoreAsync(
            string tourCode, SavePricesRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.SavePublishedPricesAsync(
                tourCode,
                request.Prices.Select(p => new PriceEntry
                {
                    HubCode = p.Hub,
                    DepartureDate = p.Date,
                    OccupancyCode = p.Occupancy,
                    Price = p.Price
                }).ToList(),
                actor,
                cancellationToken,
                request.ExpectedModifiedUtc);
        }


        private Task SaveFaresCoreAsync(
            string tourCode, SaveFaresRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.SaveFaresAsync(
                tourCode,
                request.Fares.Select(f => new FareEntry
                {
                    HubCode = f.Hub,
                    DepartureDate = f.Date,
                    FareBandCode = f.Band.ToLowerInvariant(),
                    Amount = f.Amount
                }).ToList(),
                actor,
                cancellationToken,
                request.ExpectedModifiedUtc);
        }


        /// <summary>
        /// Replaces the "conditions apply" markers across the hubs the caller
        /// showed, with the cells it says are starred.
        /// </summary>
        private Task SetConditionsCoreAsync(
            string tourCode, SetConditionsRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.SetConditionsAsync(
                tourCode,
                request.Hubs ?? new List<string>(),
                ToCellRefs(request.Cells ?? new List<PriceCellRequest>()),
                actor,
                cancellationToken,
                request.ExpectedModifiedUtc);
        }


        private static IReadOnlyList<PriceCellRef> ToCellRefs(IEnumerable<PriceCellRequest> cells)
        {
            return cells.Select(c => new PriceCellRef
            {
                HubCode = c.Hub,
                DepartureDate = c.Date,
                OccupancyCode = c.Occupancy
            }).ToList();
        }


        /// <summary>Who changed what on this tour, newest first.</summary>
        private async Task<IReadOnlyList<ActivityResponse>> GetActivityCoreAsync(
            string tourCode, int take, CancellationToken cancellationToken)
        {
            var rows = await _repository.GetActivityAsync(tourCode, take, cancellationToken);

            return rows.Select(ToActivity).ToList();
        }


        /// <summary>One row of the log, shaped for a caller.</summary>
        private static ActivityResponse ToActivity(ActivityRow row)
        {
            return new ActivityResponse
            {
                OccurredUtc = row.OccurredUtc,
                ActorName = row.ActorName,
                ActorRole = row.ActorRole,
                Action = row.Action,
                HubCode = row.HubCode,
                HubName = row.HubName,
                DepartureDate = row.DepartureDate,
                Field = row.Field,
                OldValue = row.OldValue,
                NewValue = row.NewValue,
                Note = row.Note
            };
        }


        private Task SubmitFaresCoreAsync(
            string tourCode, string submittedBy, string note, CancellationToken cancellationToken)
        {
            return _repository.SubmitFaresAsync(tourCode, submittedBy, note, cancellationToken);
        }


        private Task RequestFaresCoreAsync(
            string tourCode, string requestedBy, string note, CancellationToken cancellationToken)
        {
            return _repository.RequestFaresAsync(tourCode, requestedBy, note, cancellationToken);
        }


        private async Task<IReadOnlyList<FareRequestResponse>> ListFareRequestsCoreAsync(
            CancellationToken cancellationToken)
        {
            var rows = await _repository.ListFareRequestsAsync(cancellationToken);

            return rows.Select(row => new FareRequestResponse
            {
                TourCode = row.TourCode,
                TourName = row.TourName,
                Region = row.Region,
                RequestedUtc = row.RequestedUtc,
                RequestedBy = row.RequestedBy,
                Note = row.Note,
                AwaitingFares = row.AwaitingFares,
                QueriedDepartures = row.QueriedDepartures,
                EarliestDeparture = row.EarliestDeparture
            }).ToList();
        }


        /// <summary>
        /// The airfare summary sheet: what air-ticketing has handed over on
        /// this tour, one column per submission, oldest first.
        /// </summary>
        /// <remarks>
        /// The pivot happens here rather than in SQL because the column count
        /// is the number of submissions, which grows with the season - SQL
        /// would have to be told it in advance.
        ///
        /// The last column is the working one: what air-ticketing has saved
        /// but not yet sent. It carries submission id 0 and is only included
        /// when it differs from the last real submission, because otherwise
        /// every tour ends with a duplicate of its own last column.
        /// </remarks>
        private async Task<FlightSheetResponse> GetFlightSheetCoreAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            var data = await _repository.GetFlightSheetAsync(tourCode, cancellationToken);
            if (data == null)
            {
                return null;
            }

            var byColumn = data.Lines
                .GroupBy(l => l.FareSubmissionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var columnIds = data.Submissions.Select(s => s.FareSubmissionId).ToList();

            // Does the working state differ from the last thing they sent? If
            // nothing has moved since, there is nothing to show as in progress.
            var working = byColumn.ContainsKey(0) ? byColumn[0] : new List<FlightSheetLineRow>();
            var showWorking = working.Count > 0
                && (columnIds.Count == 0 || DiffersFromLast(working, byColumn, columnIds));

            if (showWorking)
            {
                columnIds.Add(0);
            }

            /*
                Who the working column belongs to. Both the product team and
                air-ticketing may move a fare - CanEditFares is "is editing",
                not "is air-ticketing" - so labelling it air-ticketing's would
                be a guess, and wrong exactly when it matters: when the product
                team has overridden a figure air-ticketing sent.
            */
            var actor = await _repository.GetWorkingColumnActorAsync(tourCode, cancellationToken);
            var workingBy = actor == null ? null : actor.ActorName;

            var columns = columnIds.Select(id =>
            {
                var submission = data.Submissions.FirstOrDefault(s => s.FareSubmissionId == id);
                return new FlightSheetColumnResponse
                {
                    SubmissionId = id,
                    SubmittedUtc = submission == null ? (DateTime?)null : submission.SubmittedUtc,
                    SubmittedBy = submission != null ? submission.SubmittedBy : workingBy,
                    Note = submission == null ? null : submission.Note,
                    IsSubmitted = submission != null
                };
            }).ToList();

            var hubs = data.Lines
                .Where(l => !string.IsNullOrEmpty(l.HubCode))
                .GroupBy(l => new { l.HubCode, l.HubName })
                .OrderBy(g => g.Key.HubName)
                .Select(hubGroup => new FlightSheetHubResponse
                {
                    Hub = hubGroup.Key.HubCode,
                    Name = hubGroup.Key.HubName,
                    Departures = hubGroup
                        .GroupBy(l => l.DepartureDate)
                        .OrderBy(g => g.Key)
                        .Select(dateGroup => BuildDeparture(dateGroup.Key, dateGroup.ToList(), columnIds))
                        .ToList()
                }).ToList();

            // A column "has changes" when any departure changed in it.
            for (var i = 0; i < columns.Count; i++)
            {
                var id = columns[i].SubmissionId;
                columns[i].HasChanges = i > 0 && hubs
                    .SelectMany(h => h.Departures)
                    .SelectMany(d => d.Cells)
                    .Any(c => c.SubmissionId == id && c.Changed);
            }

            return new FlightSheetResponse
            {
                TourCode = data.Tour.TourCode,
                TourName = data.Tour.TourName,
                Region = data.Tour.Region,
                Duration = data.Tour.Duration,
                Columns = columns,
                Hubs = hubs
            };
        }


        private static bool DiffersFromLast(
            List<FlightSheetLineRow> working,
            Dictionary<int, List<FlightSheetLineRow>> byColumn,
            List<int> columnIds)
        {
            var lastId = columnIds[columnIds.Count - 1];
            var last = byColumn.ContainsKey(lastId) ? byColumn[lastId] : new List<FlightSheetLineRow>();

            if (working.Count != last.Count)
            {
                return true;
            }

            foreach (var w in working)
            {
                var match = last.FirstOrDefault(l =>
                    string.Equals(l.HubCode, w.HubCode, StringComparison.OrdinalIgnoreCase)
                    && l.DepartureDate == w.DepartureDate);

                if (match == null
                    || match.AdultFare != w.AdultFare
                    || match.ChildFare != w.ChildFare
                    || match.InfantFare != w.InfantFare
                    || !string.Equals(match.FlightDetails ?? "", w.FlightDetails ?? "",
                                      StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }


        private static FlightSheetDepartureResponse BuildDeparture(
            DateTime date, List<FlightSheetLineRow> lines, List<int> columnIds)
        {
            var cells = new List<FlightSheetCellResponse>();
            FlightSheetLineRow previous = null;

            foreach (var id in columnIds)
            {
                var line = lines.FirstOrDefault(l => l.FareSubmissionId == id);

                if (line == null)
                {
                    // The departure was not on the tour at that submission.
                    // An empty cell, not a zero - they are different facts.
                    cells.Add(new FlightSheetCellResponse { SubmissionId = id, IsPresent = false });
                    continue;
                }

                var changed = previous != null
                    && (previous.AdultFare != line.AdultFare
                        || previous.ChildFare != line.ChildFare
                        || previous.InfantFare != line.InfantFare);

                cells.Add(new FlightSheetCellResponse
                {
                    SubmissionId = id,
                    IsPresent = true,
                    Adult = line.AdultFare,
                    Child = line.ChildFare,
                    Infant = line.InfantFare,
                    FlightDetails = line.FlightDetails,
                    Changed = changed,
                    AdultChangePercent = PercentMoved(
                        previous == null ? null : previous.AdultFare, line.AdultFare),
                    ChildChangePercent = PercentMoved(
                        previous == null ? null : previous.ChildFare, line.ChildFare),
                    InfantChangePercent = PercentMoved(
                        previous == null ? null : previous.InfantFare, line.InfantFare)
                });

                previous = line;
            }

            // The live column is the only one that knows whether the date is
            // still on the tour. Submitted columns report what they carried.
            var live = lines.FirstOrDefault(l => l.FareSubmissionId == 0);

            // Absent from the first column but present later: added mid-season.
            var firstCell = cells.Count > 0 ? cells[0] : null;

            return new FlightSheetDepartureResponse
            {
                Date = date,
                Cells = cells,
                HasMoved = cells.Any(c => c.Changed),
                IsWithdrawn = live != null && !live.IsActive,
                IsNew = firstCell != null && !firstCell.IsPresent && cells.Any(c => c.IsPresent)
            };
        }


        /// <summary>
        /// How far a fare moved, as a percentage of what it was.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Null when nothing moved, when there is nothing to compare against,
        /// or when the previous figure was zero or absent - a rise from nothing
        /// is not a percentage, and the two figures say it better than "∞%".
        /// </para>
        /// <para>
        /// Two decimals, rounded half-up: the third decimal decides, 0-4 keeps
        /// the second digit and 5-9 increments it. That is the project's
        /// rounding rule, and this was the one call in the codebase that did
        /// not follow it - it took one decimal and .NET's default, which rounds
        /// midpoints to EVEN.
        /// </para>
        /// <para>
        /// <see cref="MidpointRounding.AwayFromZero"/> rather than a signed
        /// half-up, because this figure goes negative when a price falls and
        /// the rule increments the digit of the MAGNITUDE. It is also what
        /// every other rounding call here uses - see Pricing.Domain/Inr.cs,
        /// which exists so a figure on screen cannot disagree with the same
        /// figure in the database.
        /// </para>
        /// </remarks>
        private static decimal? PercentMoved(decimal? before, decimal? after)
        {
            if (!before.HasValue || !after.HasValue
                || before.Value == 0m || before.Value == after.Value)
            {
                return null;
            }

            return Math.Round(
                (after.Value - before.Value) / before.Value * 100m,
                2,
                MidpointRounding.AwayFromZero);
        }


        private Task SaveFlightDetailsCoreAsync(
            string tourCode,
            SaveFlightDetailsRequest request,
            Actor actor,
            CancellationToken cancellationToken)
        {
            var entries = (request.Details ?? new List<FlightDetailRequest>())
                .Select(d => new FlightDetailEntry
                {
                    Hub = d.Hub,
                    Date = d.Date,
                    Details = string.IsNullOrWhiteSpace(d.Details) ? null : d.Details.Trim()
                }).ToList();

            return _repository.SaveFlightDetailsAsync(
                tourCode, entries, actor == null ? null : actor.Name, cancellationToken);
        }


        /// <summary>
        /// The tour's stale-write stamp, and what has changed since a given
        /// one. Together these are what a refusal needs: the current state, and
        /// why the caller's was out of date.
        /// </summary>
        private Task<DateTime?> GetTourStampCoreAsync(string tourCode, CancellationToken cancellationToken)
        {
            return _repository.GetTourStampAsync(tourCode, cancellationToken);
        }


        private async Task<IReadOnlyList<ActivityResponse>> GetChangesSinceCoreAsync(
            string tourCode, DateTime since, CancellationToken cancellationToken)
        {
            var rows = await _repository.GetChangesSinceAsync(tourCode, since, cancellationToken);
            return rows.Select(ToActivity).ToList();
        }


        /// <summary>Every version this tour has been on, newest first.</summary>
        private async Task<IReadOnlyList<VersionHistoryEntryResponse>> GetVersionHistoryCoreAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            var rows = await _repository.GetVersionHistoryAsync(tourCode, cancellationToken);

            return rows.Select(row => new VersionHistoryEntryResponse
            {
                Version = row.Version,
                ChangeSetRef = row.ChangeSetRef,
                Changes = row.Changes,
                Summary = row.Summary,
                AppliedUtc = row.AppliedUtc,
                HasSnapshot = row.HasSnapshot
            }).ToList();
        }


        /// <summary>
        /// The product page as it stood at a version. Null when that version
        /// has no photograph.
        /// </summary>
        /// <remarks>
        /// The flat result sets are stitched into the page's own shape here -
        /// hubs carrying departures carrying fares and cells - so that the
        /// read-only version page and the live revision page render from the
        /// same arrangement rather than each inventing one.
        /// </remarks>
        private async Task<VersionSnapshotResponse> GetVersionSnapshotCoreAsync(
            string tourCode, string version, CancellationToken cancellationToken)
        {
            var data = await _repository.GetVersionSnapshotAsync(
                tourCode, version, cancellationToken);

            if (data == null)
            {
                return null;
            }

            var faresByDeparture = data.Fares
                .GroupBy(f => new { f.HubCode, f.DepartureDate })
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyDictionary<string, decimal>)g
                        .ToDictionary(f => f.BandCode, f => f.Amount));

            var cellsByDeparture = data.Prices
                .GroupBy(p => new { p.HubCode, p.DepartureDate })
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<VersionSnapshotCellResponse>)g
                        .Select(p => new VersionSnapshotCellResponse
                        {
                            Occupancy = p.OccupancyCode,
                            Published = p.PublishedPrice,
                            StrikeThrough = p.StrikeThrough,
                            HasConditions = p.HasConditions
                        }).ToList());

            var hubs = data.Hubs.Select(hub => new VersionSnapshotHubResponse
            {
                Hub = hub.HubCode,
                Name = hub.HubName,
                MarkupPercent = hub.MarkupPct,
                IsActive = hub.IsActive,
                Departures = data.Departures
                    .Where(d => string.Equals(d.HubCode, hub.HubCode, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.DepartureDate)
                    .Select(d =>
                    {
                        var key = new { HubCode = d.HubCode, DepartureDate = d.DepartureDate };
                        return new VersionSnapshotDepartureResponse
                        {
                            Date = d.DepartureDate,
                            IsActive = d.IsActive,
                            Fares = faresByDeparture.ContainsKey(key)
                                ? faresByDeparture[key]
                                : new Dictionary<string, decimal>(),
                            Cells = cellsByDeparture.ContainsKey(key)
                                ? cellsByDeparture[key]
                                : new List<VersionSnapshotCellResponse>()
                        };
                    }).ToList()
            }).ToList();

            return new VersionSnapshotResponse
            {
                TourCode = data.Header.TourCode,
                TourName = data.Header.TourName,
                Region = data.Header.Region,
                Duration = data.Header.Duration,
                Version = data.Header.Version,
                ChangeSetRef = data.Header.ChangeSetRef,
                CapturedUtc = data.Header.CapturedUtc,
                CurrentVersion = data.Header.CurrentVersion,
                FxRate = data.Header.FxRate,
                StrikePercent = data.Header.StrikePct,
                PaxSlab = data.Header.PaxSlab,
                SharedCost = data.Header.SharedCost,
                Note = data.Header.Note,
                Occupancies = data.Occupancies.Select(o => new VersionSnapshotOccupancyResponse
                {
                    Occupancy = o.OccupancyCode,
                    Label = o.OccupancyLabel,
                    SortOrder = o.SortOrder,
                    LandCostFx = o.LandCostFx,
                    PerPersonInr = o.PerPersonInr
                }).ToList(),
                Hubs = hubs
            };
        }


        /// <summary>
        /// What the prices would be with the supplied fares. Writes nothing, and
        /// is answered by the same function that produces the real prices.
        /// </summary>
        private async Task<IReadOnlyList<PreviewCellResponse>> PreviewPricesCoreAsync(
            string tourCode, PreviewPricesRequest request, CancellationToken cancellationToken)
        {
            var fares = (request.Fares ?? new List<FareRequest>()).Select(f => new FareEntry
            {
                HubCode = f.Hub,
                DepartureDate = f.Date,
                FareBandCode = (f.Band ?? string.Empty).ToLowerInvariant(),
                Amount = f.Amount
            }).ToList();

            var rows = await _repository.PreviewPricesAsync(tourCode, fares, cancellationToken);

            return rows.Select(row => new PreviewCellResponse
            {
                Hub = row.HubCode,
                Date = row.DepartureDate,
                Occupancy = row.OccupancyCode,
                SortOrder = row.OccupancySort,
                Calculated = row.CalculatedPrice,
                Published = row.PublishedPrice,
                StrikeThrough = row.StrikeThrough,
                IsOverridden = row.IsOverridden,
                HasFare = row.HasFare,
                LivePrice = row.LivePrice
            }).ToList();
        }


        private Task AddHubCoreAsync(
            string tourCode, AddHubRequest request, CancellationToken cancellationToken)
        {
            return _repository.AddHubAsync(
                tourCode, request.Hub, request.MarkupPercent, cancellationToken,
                request.CopyDepartureDates);
        }


        /// <summary>
        /// "Same as calculated SP" for one hub. An act, not a setting: it fills
        /// the blank cells once and nothing is remembered afterwards.
        /// </summary>
        private async Task<ConfirmedCountResponse> AdoptCalculatedForHubCoreAsync(
            string tourCode, string hubCode, Actor actor, CancellationToken cancellationToken)
        {
            var outcome = await _repository.AdoptCalculatedForHubAsync(
                tourCode, hubCode, actor == null ? "Unknown user" : actor.Name, cancellationToken);

            return new ConfirmedCountResponse { Confirmed = outcome.CellsInScope, Skipped = 0 };
        }


        private Task UpdateHubCoreAsync(
            string tourCode, string hubCode, UpdateHubRequest request, CancellationToken cancellationToken)
        {
            return _repository.UpdateHubAsync(
                tourCode, hubCode, request.MarkupPercent, request.IsActive, cancellationToken);
        }


        private Task AddDepartureCoreAsync(
            string tourCode, string hubCode, DateTime date, CancellationToken cancellationToken)
        {
            return _repository.AddDepartureAsync(tourCode, hubCode, date, cancellationToken);
        }


        private Task SetDepartureActiveCoreAsync(
            string tourCode, string hubCode, DateTime date, bool isActive, CancellationToken cancellationToken)
        {
            return _repository.SetDepartureActiveAsync(tourCode, hubCode, date, isActive, cancellationToken);
        }


        /// <summary>
        /// Records what a change set was submitted with, so further edits are
        /// measured against it.
        /// </summary>
        private Task RecordSubmittedCoreAsync(
            string tourCode, RecordSubmittedRequest request, Actor actor,
            CancellationToken cancellationToken)
        {
            return _repository.RecordSubmittedPricesAsync(
                tourCode,
                request.Reference,
                (request.Prices ?? new List<PromotedPrice>()).Select(p => new PriceEntry
                {
                    HubCode = p.Hub,
                    DepartureDate = p.Date,
                    OccupancyCode = p.Occupancy,
                    Price = p.Price
                }).ToList(),
                actor == null ? null : actor.Name,
                cancellationToken);
        }
    }
}
