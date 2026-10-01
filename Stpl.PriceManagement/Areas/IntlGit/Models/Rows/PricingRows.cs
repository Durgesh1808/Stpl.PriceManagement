using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.IntlGit.Models.Rows
{
    /// <summary>
    /// Shapes returned by the stored procedures.
    /// </summary>
    /// <remarks>
    /// These mirror the procedures' result sets exactly and nothing else. They
    /// are not the API contract - the API projects its own DTOs from these, so
    /// a column rename in the database does not become a breaking API change.
    /// </remarks>
    public sealed class TourListRow
    {
        public int TourId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }
        public string CurrentVersion { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public DateTime? SavedUtc { get; set; }
        public int HubCount { get; set; }
        public int DepartureCount { get; set; }

        /// <summary>Departure rows whose prices differ from what is live.</summary>
        public int PendingPriceRows { get; set; }

        /// <summary>Hubs or departures to be taken off the website.</summary>
        public int PendingRemovals { get; set; }

        public DateTime? FareSubmittedUtc { get; set; }

        /// <summary>Departures with no airfare entered yet, so they have no price.</summary>
        public int AwaitingFares { get; set; }

        /// <summary>
        /// Departures that cannot travel yet: a fare under query with
        /// air-ticketing, or no agreed price. usp_GetTourList unions the two.
        /// </summary>
        public int HeldDepartures { get; set; }

        /// <summary>Open fare request, if any (0086).</summary>
        public DateTime? FareRequestedUtc { get; set; }

        /// <summary>Departures with an unresolved fare query (0086).</summary>
        public int QueriedDepartures { get; set; }

        public int PendingChanges
        {
            get { return PendingPriceRows + PendingRemovals; }
        }
    }

    public sealed class CostBuildHeaderRow
    {
        public int TourId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }
        public string CurrentVersion { get; set; }
        public DateTime ModifiedUtc { get; set; }
        // Nullable for the same reason SharedCost is: nobody has entered one.
        public decimal? FxRate { get; set; }
        public decimal? PreviousFxRate { get; set; }
        public decimal? StrikePct { get; set; }
        public int PaxSlab { get; set; }
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }
        public DateTime? SavedUtc { get; set; }
        public DateTime? FareSubmittedUtc { get; set; }
        public string FareSubmittedBy { get; set; }
        public string FareNote { get; set; }
    }

    public sealed class OccupancyCostRow
    {
        public string OccupancyCode { get; set; }
        public string OccupancyLabel { get; set; }
        public byte SortOrder { get; set; }
        public decimal? LandCostFx { get; set; }
        public decimal? PerPersonInr { get; set; }
    }

    public sealed class HubRow
    {
        public int TourHubId { get; set; }
        public string HubCode { get; set; }
        public string HubName { get; set; }

        /// <summary>Null when nobody has set this hub's margin. No markup, no price.</summary>
        public decimal? MarkupPct { get; set; }
        public bool IsActive { get; set; }
        public int DepartureCount { get; set; }

        /// <summary>No live prices at all - added during this revision.</summary>
        public bool IsNewHub { get; set; }

        /// <summary>False where the customer joins at the destination.</summary>
        public bool HasPassengerAirfare { get; set; }
    }

    public sealed class DepartureFareRow
    {
        public int TourDepartureId { get; set; }
        public string HubCode { get; set; }
        public DateTime DepartureDate { get; set; }
        public bool IsActive { get; set; }
        public string FareBandCode { get; set; }

        /// <summary>Null when air-ticketing has not entered a fare yet.</summary>
        public decimal? Airfare { get; set; }

        /// <summary>What it was before the last change, for the product team.</summary>
        public decimal? PreviousAirfare { get; set; }

        /// <summary>
        /// Free text from air-ticketing about the flight. Belongs to the
        /// departure, so it repeats across that departure's three band rows.
        /// </summary>
        public string FlightDetails { get; set; }

        /// <summary>
        /// This band on this departure has been sent back to air-ticketing.
        /// </summary>
        public bool IsQueried { get; set; }
    }

    public sealed class PriceCellRow
    {
        public string HubCode { get; set; }
        public DateTime DepartureDate { get; set; }
        public string OccupancyCode { get; set; }
        public byte OccupancySort { get; set; }

        /// <summary>Null when the departure has no airfare yet - no fare, no price.</summary>
        public decimal? CalculatedPrice { get; set; }

        public decimal? PublishedPrice { get; set; }
        public decimal? StrikeThrough { get; set; }
        public bool IsOverridden { get; set; }

        /// <summary>Null when this departure has never been on the website.</summary>
        public decimal? LivePrice { get; set; }

        /// <summary>What the next change is measured against: submitted, else live.</summary>
        public decimal? BaselinePrice { get; set; }

        public bool IsNewToSite { get; set; }

        /// <summary>False when no airfare has been entered for this departure.</summary>
        public bool HasFare { get; set; }

        /// <summary>
        /// Where this figure stands: nofare, new, draft, sent or live. Computed
        /// in fn_PriceMatrix so the grid and the change list cannot disagree.
        /// </summary>
        public string ChangeState { get; set; }

        /// <summary>Conditions apply - the star the website renders beside the price.</summary>
        public bool HasCondition { get; set; }

        /// <summary>Somebody has agreed to this figure since the last time it moved.</summary>
        public bool IsConfirmed { get; set; }

        public string ConfirmedBy { get; set; }
        public DateTime? ConfirmedUtc { get; set; }

        /// <summary>
        /// This departure's fare has been sent back to air-ticketing, so the
        /// whole departure is held out of the change set until they answer.
        /// </summary>
        public bool IsQueried { get; set; }
    }

    /// <summary>Everything the price revision workspace draws, from one call.</summary>
    public sealed class TourRevisionData
    {
        public CostBuildHeaderRow Header { get; set; }
        public IReadOnlyList<OccupancyCostRow> Costs { get; set; }
        public IReadOnlyList<HubRow> Hubs { get; set; }
        public IReadOnlyList<DepartureFareRow> Fares { get; set; }
        public IReadOnlyList<PriceCellRow> Prices { get; set; }
    }

    public sealed class PendingPriceRow
    {
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime DepartureDate { get; set; }
        public string OccupancyCode { get; set; }
        public byte OccupancySort { get; set; }
        public decimal PublishedPrice { get; set; }

        /// <summary>Null where the tour has no strike-through percentage set.</summary>
        public decimal? StrikeThrough { get; set; }
        public decimal? LivePrice { get; set; }

        /// <summary>
        /// The figure this change was measured against: the price the last set
        /// was submitted with, or failing that what is live. On a second set
        /// raised while the first is still with tech support, this and LivePrice
        /// are different numbers, and this is the one the change was made from.
        /// </summary>
        public decimal? BaselinePrice { get; set; }

        public bool IsNewToSite { get; set; }

        /// <summary>
        /// False for a cell whose price has not moved. Such cells are still
        /// returned, so tech support can see which figures to leave alone.
        /// </summary>
        public bool HasChanged { get; set; }

        /// <summary>Conditions apply. Travels with the price to whoever publishes it.</summary>
        public bool HasCondition { get; set; }

        public bool IsNewHub { get; set; }
    }

    /// <summary>
    /// A departure held out of the change list because nobody has priced it.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="FareQueryRow"/> rather than folded in behind
    /// a reason column. They read alike and are not alike: a queried departure
    /// is waiting on air-ticketing and the remedy is a phone call; this one is
    /// waiting on the person reading the screen and the remedy is two minutes
    /// on the grid. One list would send people chasing fares already in.
    /// </remarks>
    public sealed class UnpricedDepartureRow
    {
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime DepartureDate { get; set; }

        /// <summary>How many of the occupancies have no figure.</summary>
        public int BlankCount { get; set; }

        /// <summary>What the six would come to if the calculated prices were taken.</summary>
        public decimal? CalculatedTotal { get; set; }
    }

    public sealed class PendingRemovalRow
    {
        /// <summary>"hub" or "departure".</summary>
        public string Kind { get; set; }

        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime? DepartureDate { get; set; }
        public int DepartureCount { get; set; }
        public decimal? LivePrice { get; set; }
    }

    public sealed class PendingChangesData
    {
        public IReadOnlyList<PendingPriceRow> Prices { get; set; }
        public IReadOnlyList<PendingRemovalRow> Removals { get; set; }

        /// <summary>
        /// Departures deliberately left out of <see cref="Prices"/> because
        /// their fare is under query. Carried alongside so a screen can say
        /// what it is not showing.
        /// </summary>
        public IReadOnlyList<FareQueryRow> Held { get; set; }

        /// <summary>
        /// Departures left out of <see cref="Prices"/> because at least one of
        /// their prices has not been agreed. Same obligation as
        /// <see cref="Held"/>: a screen that shows the change list has to be
        /// able to say what is missing from it.
        /// </summary>
        public IReadOnlyList<UnpricedDepartureRow> Unpriced { get; set; }
    }

    /// <summary>
    /// A fare the product team has sent back, and the departure it holds.
    /// </summary>
    public sealed class FareQueryRow
    {
        public int FareQueryId { get; set; }
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime DepartureDate { get; set; }

        /// <summary>Null when the whole departure was queried, not one band.</summary>
        public string FareBandCode { get; set; }
        public string FareBandLabel { get; set; }

        /// <summary>The figure being disputed. Null for a whole-departure query.</summary>
        public decimal? Airfare { get; set; }

        public DateTime RaisedUtc { get; set; }
        public string RaisedBy { get; set; }
        public string Reason { get; set; }

        public DateTime? ResolvedUtc { get; set; }
        public string ResolvedBy { get; set; }
        public string ResolutionNote { get; set; }
    }

    public sealed class PreviewCellRow
    {
        public string HubCode { get; set; }
        public DateTime DepartureDate { get; set; }
        public string OccupancyCode { get; set; }
        public byte OccupancySort { get; set; }
        public decimal? CalculatedPrice { get; set; }
        public decimal? PublishedPrice { get; set; }
        public decimal? StrikeThrough { get; set; }
        public bool IsOverridden { get; set; }
        public bool HasFare { get; set; }

        /// <summary>What is on the website now. Unmoved by a preview.</summary>
        public decimal? LivePrice { get; set; }
    }

    /// <summary>
    /// What "same as calculated SP" did to a hub. CellsInScope is how many
    /// blank cells were filled - it is the number worth telling somebody,
    /// because it is the work they just avoided doing by hand.
    /// </summary>
    public sealed class AdoptOutcome
    {
        public int CellsInScope { get; set; }
    }

    public sealed class HubMasterRow

    {
        public string Code { get; set; }
        public string Name { get; set; }
        /// <summary>Null until the hub master carries a margin for this hub.</summary>
        public decimal? DefaultMarkupPct { get; set; }
        public bool HasPassengerAirfare { get; set; }
    }

    /// <summary>One entry in a tour's activity log.</summary>
    public sealed class ActivityRow
    {
        public long ActivityId { get; set; }
        public System.DateTime OccurredUtc { get; set; }
        public string ActorName { get; set; }
        public string ActorRole { get; set; }
        public string Action { get; set; }
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public System.DateTime? DepartureDate { get; set; }
        public string Field { get; set; }
        public decimal? OldValue { get; set; }
        public decimal? NewValue { get; set; }
        public string Note { get; set; }
    }

    /// <summary>One tour the product team is waiting on air-ticketing for.</summary>
    public sealed class FareRequestRow
    {
        public int FareRequestId { get; set; }
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public System.DateTime RequestedUtc { get; set; }
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
        public System.DateTime? EarliestDeparture { get; set; }
    }

    // --- Version snapshots -------------------------------------------------
    //
    // The photograph of a product page taken when a version went live. Codes
    // rather than ids throughout, because a snapshot has to keep reading
    // correctly even if a hub or an occupancy is renumbered later.

    public sealed class VersionHistoryRow
    {
        public string Version { get; set; }
        public string ChangeSetRef { get; set; }
        public int Changes { get; set; }
        public string Summary { get; set; }
        public System.DateTime AppliedUtc { get; set; }

        /// <summary>False for the versions that predate migration 0051.</summary>
        public bool HasSnapshot { get; set; }
    }

    public sealed class VersionSnapshotHeaderRow
    {
        public string Version { get; set; }
        public string ChangeSetRef { get; set; }
        public System.DateTime CapturedUtc { get; set; }
        public decimal? FxRate { get; set; }
        public decimal? StrikePct { get; set; }
        public int? PaxSlab { get; set; }
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }
        public string CurrentVersion { get; set; }
    }

    public sealed class VersionSnapshotOccupancyRow
    {
        public string OccupancyCode { get; set; }
        public string OccupancyLabel { get; set; }
        public byte SortOrder { get; set; }
        public decimal LandCostFx { get; set; }
        public decimal PerPersonInr { get; set; }
    }

    public sealed class VersionSnapshotHubRow
    {
        public string HubCode { get; set; }
        public string HubName { get; set; }

        /// <summary>Null where the hub carried no margin when this version was taken.</summary>
        public decimal? MarkupPct { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class VersionSnapshotDepartureRow
    {
        public string HubCode { get; set; }
        public System.DateTime DepartureDate { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class VersionSnapshotFareRow
    {
        public string HubCode { get; set; }
        public System.DateTime DepartureDate { get; set; }
        public string BandCode { get; set; }
        public decimal Amount { get; set; }
    }

    public sealed class VersionSnapshotPriceRow
    {
        public string HubCode { get; set; }
        public System.DateTime DepartureDate { get; set; }
        public string OccupancyCode { get; set; }
        public decimal PublishedPrice { get; set; }
        public decimal StrikeThrough { get; set; }
        public bool HasConditions { get; set; }
    }

    /// <summary>All six result sets. Null when that version has no photograph.</summary>
    public sealed class VersionSnapshotData
    {
        public VersionSnapshotHeaderRow Header { get; set; }
        public IReadOnlyList<VersionSnapshotOccupancyRow> Occupancies { get; set; }
        public IReadOnlyList<VersionSnapshotHubRow> Hubs { get; set; }
        public IReadOnlyList<VersionSnapshotDepartureRow> Departures { get; set; }
        public IReadOnlyList<VersionSnapshotFareRow> Fares { get; set; }
        public IReadOnlyList<VersionSnapshotPriceRow> Prices { get; set; }
    }

    // --- The airfare summary sheet -------------------------------------------

    public sealed class FlightSheetTourRow
    {
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public string Duration { get; set; }
    }

    public sealed class FlightSheetSubmissionRow
    {
        public int FareSubmissionId { get; set; }
        public System.DateTime SubmittedUtc { get; set; }
        public string SubmittedBy { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// One departure as it stood at one submission. FareSubmissionId 0 is the
    /// working column - what air-ticketing has saved but not yet sent.
    /// </summary>
    public sealed class FlightSheetLineRow
    {
        public int FareSubmissionId { get; set; }
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public System.DateTime DepartureDate { get; set; }
        public decimal? AdultFare { get; set; }
        public decimal? ChildFare { get; set; }
        public decimal? InfantFare { get; set; }
        public string FlightDetails { get; set; }

        /// <summary>
        /// Only meaningful on the live column. Submitted lines are always
        /// reported active: what a hand-over carried is a matter of record,
        /// and marking a past one withdrawn would rewrite it.
        /// </summary>
        public bool IsActive { get; set; }
    }

    public sealed class FlightSheetData
    {
        public FlightSheetTourRow Tour { get; set; }
        public IReadOnlyList<FlightSheetSubmissionRow> Submissions { get; set; }
        public IReadOnlyList<FlightSheetLineRow> Lines { get; set; }
    }

    /// <summary>
    /// Who last moved a fare since the last hand-over. Both the product team
    /// and air-ticketing may, so the working column cannot assume.
    /// </summary>
    public sealed class WorkingColumnActorRow
    {
        public string ActorName { get; set; }
        public string ActorRole { get; set; }
        public DateTime OccurredUtc { get; set; }
    }

    /// <summary>One departure's flight note, on its way to the database.</summary>
    public sealed class FlightDetailEntry
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }
        public string Details { get; set; }
    }
}
