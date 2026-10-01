using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.IntlGit.Models
{
    /// <summary>
    /// The API's own shapes.
    /// </summary>
    /// <remarks>
    /// Kept separate from the database read models on purpose: renaming a
    /// column should not become a breaking change for callers, and the API
    /// exposes only what a caller needs. Money is decimal, never double, and
    /// never a pre-formatted string - formatting is the frontend's job.
    /// </remarks>
    public sealed class TourSummaryResponse
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }

        /// <summary>As the product master states it, e.g. "10 D / 9 N".</summary>
        public string Duration { get; set; }

        public string Version { get; set; }
        public int HubCount { get; set; }
        public int DepartureCount { get; set; }

        /// <summary>Departure rows whose prices differ from what is live.</summary>
        public int PendingPriceRows { get; set; }

        /// <summary>Hubs or departures to be taken off the website.</summary>
        public int PendingRemovals { get; set; }

        public int PendingChanges { get; set; }

        /// <summary>Set when air-ticketing has handed fares over.</summary>
        public DateTime? FaresSubmittedUtc { get; set; }

        /// <summary>Departures with no airfare yet, so they cannot be priced.</summary>
        public int AwaitingFares { get; set; }

        /// <summary>
        /// Departures that cannot travel in a change request yet - either a
        /// fare on them is under query with air-ticketing, or they have no
        /// agreed price. Two reasons deliberately counted as one: the products
        /// list has room for "not going anywhere yet" and not for why.
        /// </summary>
        public int HeldDepartures { get; set; }

        /// <summary>
        /// When the open fare request was raised - the tour is with
        /// air-ticketing. Null when nothing is open.
        /// </summary>
        public DateTime? FareRequestedUtc { get; set; }

        /// <summary>Departures whose fare has been sent back and not yet answered.</summary>
        public int QueriedDepartures { get; set; }

        public DateTime? LastSavedUtc { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }

    public sealed class TourRevisionResponse
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }
        public string Version { get; set; }

        public CostBuildResponse CostBuild { get; set; }
        public List<HubResponse> Hubs { get; set; }

        /// <summary>Every priced cell: hub, date and occupancy.</summary>
        public List<PriceCellResponse> Prices { get; set; }

        public DateTime? FaresSubmittedUtc { get; set; }

        /// <summary>Who sent the fares, and the note they sent with them.</summary>
        public string FaresSubmittedBy { get; set; }
        public string FareNote { get; set; }

        public DateTime? LastSavedUtc { get; set; }

        /// <summary>
        /// When this tour last changed in any way. The page carries it into
        /// every save so the save can be refused if somebody else has moved
        /// the tour since this page was drawn.
        /// </summary>
        public DateTime ModifiedUtc { get; set; }
    }

    public sealed class CostBuildResponse
    {
        /// <summary>Null when nobody has entered one yet — see SharedCost.</summary>
        public decimal? FxRate { get; set; }

        /// <summary>The rate in force at the last publish. Null before there was one.</summary>
        public decimal? PreviousFxRate { get; set; }

        /// <summary>Null when nobody has set one. Then there is no struck-through price.</summary>
        public decimal? StrikePercent { get; set; }
        public int PaxSlab { get; set; }
        /// <summary>Null when nobody has entered one yet — see LandCostFx.</summary>
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }
        public List<OccupancyCostResponse> Occupancies { get; set; }
    }

    public sealed class OccupancyCostResponse
    {
        public string Occupancy { get; set; }
        public string Label { get; set; }
        public int SortOrder { get; set; }
        /// <summary>
        /// Null means nobody has entered a cost, exactly as a null airfare
        /// means nobody has quoted a fare.
        /// </summary>
        /// <remarks>
        /// Not zero. Zero is a real answer - an infant with no land cost -
        /// and pricing one is correct. Null is the absence of an answer, and
        /// a price built on it would be a price built on nothing: a Europe
        /// tour once calculated to 85,600 against a real 340,000 that way.
        /// </remarks>
        public decimal? LandCostFx { get; set; }
        public decimal? PerPersonInr { get; set; }

        /// <summary>Land plus per-person plus the shared-cost share, per person.</summary>
        public decimal? TotalCost { get; set; }
    }

    public sealed class HubResponse
    {
        public string Hub { get; set; }
        public string Name { get; set; }

        /// <summary>Null when nobody has set this hub's margin. No markup, no price.</summary>
        public decimal? MarkupPercent { get; set; }
        public bool IsActive { get; set; }
        public int ActiveDepartures { get; set; }

        /// <summary>No live prices at all - added during this revision.</summary>
        public bool IsNewHub { get; set; }

        /// <summary>
        /// False when customers join this hub at the destination.
        /// </summary>
        /// <remarks>
        /// Not the same as "no fares". Air-ticketing still enters an ADULT
        /// fare here, and that figure is the TOUR MANAGER's ticket, which
        /// every price on the departure carries a share of. What is absent is
        /// the passenger's own seat - they fly themselves, or do not fly.
        ///
        /// Own-fare-less, not fare-less. The child and infant boxes are dead
        /// on such a hub, and no fare row exists behind them.
        /// </remarks>
        public bool HasPassengerAirfare { get; set; }

        public List<DepartureResponse> Departures { get; set; }
    }

    public sealed class DepartureResponse
    {
        public DateTime Date { get; set; }
        public bool IsActive { get; set; }

        /// <summary>Airfare by band. A missing or null value means not entered yet.</summary>
        public Dictionary<string, decimal?> Fares { get; set; }

        /// <summary>What each fare was before its last change.</summary>
        public Dictionary<string, decimal?> PreviousFares { get; set; }

        /// <summary>False until every band has a fare - no fare, no price.</summary>
        public bool HasAllFares { get; set; }

        /// <summary>
        /// Bands sent back to air-ticketing on this departure. Present means
        /// disputed; the figure itself is unchanged.
        /// </summary>
        public Dictionary<string, bool> QueriedFares { get; set; }

        /// <summary>True when any band on this departure is under query.</summary>
        public bool IsQueried { get; set; }

        /// <summary>
        /// What air-ticketing has written about the flight on this departure -
        /// carrier, number, timings, whatever they keep. Free text, and null
        /// until somebody writes something.
        /// </summary>
        public string FlightDetails { get; set; }
    }

    public sealed class PriceCellResponse
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }
        public string Occupancy { get; set; }
        public int SortOrder { get; set; }

        /// <summary>What the cost build and markup produce. Null without a fare.</summary>
        public decimal? Calculated { get; set; }

        /// <summary>What would go on the website - the override if there is one.</summary>
        public decimal? Published { get; set; }

        public decimal? StrikeThrough { get; set; }

        /// <summary>False when the departure has no airfare yet.</summary>
        public bool HasFare { get; set; }

        /// <summary>True when somebody typed over the calculated price.</summary>
        public bool IsOverridden { get; set; }

        /// <summary>Null when this departure has never been on the website.</summary>
        public decimal? LivePrice { get; set; }

        public bool IsNewToSite { get; set; }

        /// <summary>
        /// The price as it stood before this revision - what was last sent to
        /// tech support, or failing that what is live. Null when the departure
        /// is new, because there is nothing it moved from.
        /// </summary>
        public decimal? BaselinePrice { get; set; }

        /// <summary>
        /// Where this figure stands, from the database: "nofare", "new",
        /// "draft" (changed here, not sent), "sent" (with tech support) or
        /// "live". The names are listed in PriceStates in Pricing.Domain.
        /// </summary>
        public string ChangeState { get; set; }

        /// <summary>Conditions apply - the star shown beside the price.</summary>
        public bool HasConditions { get; set; }

        /// <summary>
        /// False when the figure underneath this cell has moved and nobody has
        /// agreed to the new one yet. The grid shows an empty box with the
        /// calculated figure behind it, and submission is blocked until it is
        /// confirmed.
        /// </summary>
        public bool IsConfirmed { get; set; }

        public string ConfirmedBy { get; set; }
        public DateTime? ConfirmedUtc { get; set; }

        /// <summary>
        /// This departure's fare has been sent back to air-ticketing. The
        /// figure and the price are untouched; what is held is the departure,
        /// which stays out of the change set until they answer.
        /// </summary>
        public bool IsQueried { get; set; }
    }

    /// <summary>A fare the product team has disputed, and the departure it holds.</summary>
    public sealed class FareQueryResponse
    {
        public int Id { get; set; }
        public string Hub { get; set; }
        public string HubName { get; set; }
        public DateTime Date { get; set; }

        /// <summary>Null when the whole departure was queried, not one band.</summary>
        public string Band { get; set; }
        public string BandLabel { get; set; }

        /// <summary>The figure being disputed.</summary>
        public decimal? Airfare { get; set; }

        public DateTime RaisedUtc { get; set; }
        public string RaisedBy { get; set; }
        public string Reason { get; set; }

        public DateTime? ResolvedUtc { get; set; }
        public string ResolvedBy { get; set; }
        public string ResolutionNote { get; set; }

        public bool IsOpen
        {
            get { return !ResolvedUtc.HasValue; }
        }
    }

    /// <summary>
    /// How many price cells a confirmation covered. The bulk scopes do not know
    /// until the database has worked out which departures are live and priced.
    /// </summary>
    public sealed class ConfirmedCountResponse
    {
        public int Confirmed { get; set; }

        /// <summary>
        /// Prices the confirmation passed over because their fare is under
        /// query. Reported so a bulk button can say it did less than it
        /// offered, rather than quietly doing less.
        /// </summary>
        public int Skipped { get; set; }
    }

    /// <summary>
    /// A departure kept out of a change set because at least one of its prices
    /// has not been agreed. The website table prices a whole date at once, so
    /// one blank holds all six - there is no half a departure to send.
    /// </summary>
    public sealed class UnpricedDepartureResponse
    {
        public string Hub { get; set; }
        public string HubName { get; set; }
        public DateTime Date { get; set; }

        /// <summary>How many of the occupancies have no figure.</summary>
        public int BlankCount { get; set; }

        /// <summary>What the date would come to if the calculated prices were taken.</summary>
        public decimal? CalculatedTotal { get; set; }
    }

    /// <summary>One change somebody made to a tour.</summary>
    public sealed class ActivityResponse
    {
        public DateTime OccurredUtc { get; set; }

        /// <summary>Display name of whoever made the change.</summary>
        public string ActorName { get; set; }

        /// <summary>product, airticketing, techsupport or system.</summary>
        public string ActorRole { get; set; }

        /// <summary>
        /// What kind of change: fare, price, costbuild, hub, departure,
        /// fares-sent, fares-requested, changeset, promoted.
        /// </summary>
        public string Action { get; set; }

        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime? DepartureDate { get; set; }

        /// <summary>Which figure moved: a fare band, an occupancy, or a cost field.</summary>
        public string Field { get; set; }

        public decimal? OldValue { get; set; }
        public decimal? NewValue { get; set; }

        /// <summary>Carried by the handovers, which send a note rather than a figure.</summary>
        public string Note { get; set; }
    }

    /// <summary>One tour air-ticketing is being waited on for.</summary>
    public sealed class FareRequestResponse
    {
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public DateTime RequestedUtc { get; set; }
        public string RequestedBy { get; set; }
        public string Note { get; set; }

        /// <summary>Departures with no fare entered yet.</summary>
        public int AwaitingFares { get; set; }

        /// <summary>Departures whose fare the product team has sent back.</summary>
        public int QueriedDepartures { get; set; }

        /// <summary>
        /// Earliest departure that needs work - missing a fare or queried.
        /// Null when neither applies.
        /// </summary>
        public DateTime? EarliestDeparture { get; set; }
    }

    /// <summary>One previewed cell: what the price would become.</summary>
    public sealed class PreviewCellResponse
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }
        public string Occupancy { get; set; }
        public int SortOrder { get; set; }
        public decimal? Calculated { get; set; }
        public decimal? Published { get; set; }
        public decimal? StrikeThrough { get; set; }
        public bool IsOverridden { get; set; }
        public bool HasFare { get; set; }

        /// <summary>
        /// What is on the website now. A preview does not move it, but the
        /// grid redraws the whole cell, so it has to travel with the rest or
        /// the reference figure under the box goes stale mid-edit.
        /// </summary>
        public decimal? LivePrice { get; set; }
    }

    public sealed class PendingChangesResponse
    {
        public string Code { get; set; }
        public int TotalChanges { get; set; }
        public int PriceRows { get; set; }
        public List<PendingHubResponse> Hubs { get; set; }
        public List<PendingRemovalResponse> Removals { get; set; }

        /// <summary>
        /// Departures deliberately NOT in <see cref="Hubs"/>, because their fare
        /// is under query. A screen showing the change list has to be able to
        /// say what it is leaving out.
        /// </summary>
        public List<FareQueryResponse> Held { get; set; }

        /// <summary>
        /// Departures also NOT in <see cref="Hubs"/>, for the other reason:
        /// nobody has agreed a price for them. Separate from
        /// <see cref="Held"/> because the remedy is different - these are
        /// waiting on the person reading the screen, not on air-ticketing.
        /// </summary>
        public List<UnpricedDepartureResponse> Unpriced { get; set; }
    }

    public sealed class PendingHubResponse
    {
        public string Hub { get; set; }
        public string Name { get; set; }
        public bool IsNewHub { get; set; }
        public List<PendingDepartureResponse> Departures { get; set; }
    }

    public sealed class PendingDepartureResponse
    {
        public DateTime Date { get; set; }
        public List<PendingCellResponse> Cells { get; set; }

        /// <summary>
        /// This departure has never been on the website, so it has to be created
        /// there before a price has anywhere to go.
        /// </summary>
        /// <remarks>
        /// Derived from LivePrice for the same reason as
        /// <see cref="ChangeSetDepartureResponse.IsNewDeparture"/>: a stored flag
        /// would be a second version of a fact the prices already carry, and the
        /// review screen and the change set it produces must agree.
        /// </remarks>
        public bool IsNewDeparture
        {
            get
            {
                if (Cells == null || Cells.Count == 0)
                {
                    return false;
                }

                foreach (var cell in Cells)
                {
                    if (cell.LivePrice.HasValue)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    public sealed class PendingCellResponse
    {
        public string Occupancy { get; set; }
        public int SortOrder { get; set; }
        public decimal Published { get; set; }

        /// <summary>Null where there is no crossed-out price — see StrikePercent.</summary>
        public decimal? StrikeThrough { get; set; }
        public decimal? LivePrice { get; set; }

        /// <summary>
        /// The figure this change was measured against - what the last set was
        /// submitted with, or failing that what is live.
        /// </summary>
        public decimal? BaselinePrice { get; set; }

        /// <summary>False when this cell's price has not moved.</summary>
        public bool HasChanged { get; set; }

        /// <summary>Conditions apply, and the star has to travel with the price.</summary>
        public bool HasConditions { get; set; }
    }

    public sealed class PendingRemovalResponse
    {
        /// <summary>"hub" or "departure".</summary>
        public string Kind { get; set; }

        public string Hub { get; set; }
        public string Name { get; set; }
        public DateTime? Date { get; set; }
        public int DepartureCount { get; set; }
        public decimal? LivePrice { get; set; }
    }

    public sealed class HubMasterResponse
    {
        public string Hub { get; set; }
        public string Name { get; set; }
        /// <summary>Null until the hub master carries a margin for this hub.</summary>
        public decimal? DefaultMarkupPercent { get; set; }

        /// <summary>False where the customer joins the tour at the destination.</summary>
        /// <remarks>
        /// Carried into the add-hub picker so a hub whose child and infant
        /// boxes will be dead can say so before somebody chooses it.
        /// </remarks>
        public bool HasPassengerAirfare { get; set; }
    }

    // --- Version history ----------------------------------------------------

    /// <summary>One version this tour has been on.</summary>
    public sealed class VersionHistoryEntryResponse
    {
        public string Version { get; set; }
        public string ChangeSetRef { get; set; }
        public int Changes { get; set; }
        public string Summary { get; set; }
        public DateTime AppliedUtc { get; set; }

        /// <summary>
        /// Whether the whole product page was photographed at this version.
        /// False for every version raised before the snapshot existed, which
        /// is why the history card can offer a link on some rows and not
        /// others.
        /// </summary>
        public bool HasSnapshot { get; set; }
    }

    /// <summary>The product page as it stood when a version went live.</summary>
    public sealed class VersionSnapshotResponse
    {
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }

        public string Version { get; set; }
        public string ChangeSetRef { get; set; }
        public DateTime CapturedUtc { get; set; }

        /// <summary>The tour's version today, so the page can say how far back this is.</summary>
        public string CurrentVersion { get; set; }

        public decimal? FxRate { get; set; }
        public decimal? StrikePercent { get; set; }
        public int? PaxSlab { get; set; }
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }

        public IReadOnlyList<VersionSnapshotOccupancyResponse> Occupancies { get; set; }
        public IReadOnlyList<VersionSnapshotHubResponse> Hubs { get; set; }
    }

    public sealed class VersionSnapshotOccupancyResponse
    {
        public string Occupancy { get; set; }
        public string Label { get; set; }
        public int SortOrder { get; set; }
        public decimal LandCostFx { get; set; }
        public decimal PerPersonInr { get; set; }
    }

    public sealed class VersionSnapshotHubResponse
    {
        public string Hub { get; set; }
        public string Name { get; set; }

        /// <summary>Null where the hub carried no margin when this version was taken.</summary>
        public decimal? MarkupPercent { get; set; }
        public bool IsActive { get; set; }
        public IReadOnlyList<VersionSnapshotDepartureResponse> Departures { get; set; }
    }

    public sealed class VersionSnapshotDepartureResponse
    {
        public DateTime Date { get; set; }
        public bool IsActive { get; set; }

        /// <summary>Band code to amount, as air-ticketing had it.</summary>
        public IReadOnlyDictionary<string, decimal> Fares { get; set; }

        public IReadOnlyList<VersionSnapshotCellResponse> Cells { get; set; }
    }

    public sealed class VersionSnapshotCellResponse
    {
        public string Occupancy { get; set; }
        public decimal Published { get; set; }
        public decimal StrikeThrough { get; set; }
        public bool HasConditions { get; set; }
    }

    // --- The airfare summary sheet ------------------------------------------

    /// <summary>
    /// What air-ticketing has handed over on one tour, submission by
    /// submission. Selling prices are deliberately absent: this is the airfare
    /// side of the handover on its own.
    /// </summary>
    public sealed class FlightSheetResponse
    {
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }

        /// <summary>Oldest first, so the sheet reads left to right.</summary>
        public IReadOnlyList<FlightSheetColumnResponse> Columns { get; set; }

        /// <summary>Hubs, each carrying its departure dates.</summary>
        public IReadOnlyList<FlightSheetHubResponse> Hubs { get; set; }
    }

    /// <summary>One submission - or, last of all, what stands right now.</summary>
    public sealed class FlightSheetColumnResponse
    {
        /// <summary>Zero for the working column, which is not a submission.</summary>
        public int SubmissionId { get; set; }

        public DateTime? SubmittedUtc { get; set; }
        public string SubmittedBy { get; set; }
        public string Note { get; set; }

        /// <summary>
        /// False for the working column: what air-ticketing has saved but not
        /// yet sent. The sheet marks it IN PROGRESS; everything else is FINAL.
        /// </summary>
        public bool IsSubmitted { get; set; }

        /// <summary>
        /// True when this column differs from the one before it. Lets the
        /// sheet say which hand-overs actually moved anything.
        /// </summary>
        public bool HasChanges { get; set; }
    }

    public sealed class FlightSheetHubResponse
    {
        public string Hub { get; set; }
        public string Name { get; set; }
        public IReadOnlyList<FlightSheetDepartureResponse> Departures { get; set; }
    }

    public sealed class FlightSheetDepartureResponse
    {
        public DateTime Date { get; set; }

        /// <summary>
        /// One cell per column, in the same order. A missing column means that
        /// departure was not on the tour at that submission.
        /// </summary>
        public IReadOnlyList<FlightSheetCellResponse> Cells { get; set; }

        /// <summary>True when any fare on this row differs across the columns.</summary>
        public bool HasMoved { get; set; }

        /// <summary>
        /// The date has been taken off the website. The row stays on the sheet
        /// and is marked - the fares still happened, and deleting the line
        /// would make the history look as though it never had.
        /// </summary>
        public bool IsWithdrawn { get; set; }

        /// <summary>
        /// The date was not on the tour at the first column, so it was added
        /// during the season. Distinguishes "added later" from "withdrawn",
        /// which otherwise both show as gaps.
        /// </summary>
        public bool IsNew { get; set; }
    }

    public sealed class FlightSheetCellResponse
    {
        public int SubmissionId { get; set; }

        /// <summary>Null where the departure was not on the tour then.</summary>
        public bool IsPresent { get; set; }

        public decimal? Adult { get; set; }
        public decimal? Child { get; set; }
        public decimal? Infant { get; set; }

        /// <summary>
        /// Null for every column before the field existed, which is not the
        /// same as air-ticketing leaving it blank. The sheet says so.
        /// </summary>
        public string FlightDetails { get; set; }

        /// <summary>True when a fare here differs from the column before.</summary>
        public bool Changed { get; set; }

        /// <summary>
        /// How far each fare moved from the column before, as a percentage.
        /// Null when it did not move, when there is nothing to compare against,
        /// or when the previous figure was zero - a rise from nothing has no
        /// meaningful percentage and the figures say it better.
        /// </summary>
        public decimal? AdultChangePercent { get; set; }
        public decimal? ChildChangePercent { get; set; }
        public decimal? InfantChangePercent { get; set; }
    }
}
