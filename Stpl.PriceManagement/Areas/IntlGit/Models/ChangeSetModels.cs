using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.IntlGit.Models
{
    /// <summary>
    /// The ChangeSets service's wire shapes.
    /// </summary>
    /// <remarks>
    /// A change set is a record of what was asked for at a moment in time, so
    /// the tour's name and region travel with it rather than being looked up
    /// later — the set must still read correctly if the tour is renamed.
    /// </remarks>
    public sealed class CreateChangeSetRequest
    {
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }

        /// <summary>The live version this set was built against.</summary>
        public string VersionBefore { get; set; }

        public string Note { get; set; }
        public string SubmittedBy { get; set; }

        /// <summary>The submitter's account, so the promotion can tell them it landed.</summary>
        /// <remarks>
        /// Beside the name rather than instead of it. SubmittedBy is what tech
        /// support reads to know who to ask; this is what a notification is
        /// addressed to. A name does not identify an account - two people can
        /// share one, and a corrected name stops matching from that day on.
        /// </remarks>
        public string SubmittedByEmail { get; set; }

        public List<ChangeSetRowRequest> Rows { get; set; }
    }

    public sealed class ChangeSetRowRequest
    {
        /// <summary>Stable identity of this row: "blr|2026-10-13" or "rm-hub|vtz".</summary>
        public string RowKey { get; set; }

        /// <summary>"price" or "removal".</summary>
        public string Kind { get; set; }

        public string Hub { get; set; }
        public string HubName { get; set; }

        /// <summary>Null for a whole-hub removal.</summary>
        public DateTime? Date { get; set; }

        public bool IsNewHub { get; set; }
        public string RemovalAction { get; set; }
        public int? RemovalCount { get; set; }
        public decimal? LivePriceAtSubmit { get; set; }
        public int SortOrder { get; set; }

        public List<ChangeSetCellRequest> Cells { get; set; }

        /// <summary>
        /// The website table for this departure, generated from the cells above
        /// and frozen with them. Left null on removal rows - there is no table
        /// to paste for a date being taken down.
        /// </summary>
        public string Html { get; set; }
    }

    public sealed class ChangeSetCellRequest
    {
        public string Occupancy { get; set; }
        public string Label { get; set; }
        public int SortOrder { get; set; }
        public decimal Published { get; set; }

        /// <summary>Null where there is no crossed-out price. Not zero.</summary>
        public decimal? StrikeThrough { get; set; }

        /// <summary>Null when this departure has never been on the website.</summary>
        public decimal? LivePrice { get; set; }

        /// <summary>
        /// The figure the product team measured this change against. Differs
        /// from LivePrice when another set was already with tech support when
        /// this one was raised.
        /// </summary>
        public decimal? BaselinePrice { get; set; }

        public bool HasChanged { get; set; }

        /// <summary>Conditions apply - the star goes on the website with the price.</summary>
        public bool HasConditions { get; set; }
    }

    public sealed class ChangeSetCreatedResponse
    {
        public string Reference { get; set; }
        public int TotalRows { get; set; }
    }

    /// <summary>One line of the work list.</summary>
    public sealed class ChangeSetSummaryResponse
    {
        public string Reference { get; set; }
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public string VersionBefore { get; set; }
        public string VersionAfter { get; set; }
        public string Note { get; set; }
        public string SubmittedBy { get; set; }
        public DateTime SubmittedUtc { get; set; }
        public DateTime? AppliedUtc { get; set; }
        public int TotalRows { get; set; }
        public int CompletedRows { get; set; }
        public bool IsComplete { get; set; }

        public bool IsApplied
        {
            get { return AppliedUtc.HasValue; }
        }
    }

    public sealed class ChangeSetListResponse
    {
        public List<ChangeSetSummaryResponse> Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>One change set in full, for the detail screen.</summary>
    public sealed class ChangeSetDetailResponse
    {
        public ChangeSetSummaryResponse Summary { get; set; }

        /// <summary>Price rows grouped by hub, in the order tech support works them.</summary>
        public List<ChangeSetHubResponse> Hubs { get; set; }

        public List<ChangeSetRemovalResponse> Removals { get; set; }
    }

    public sealed class ChangeSetHubResponse
    {
        public string Hub { get; set; }
        public string Name { get; set; }
        public bool IsNewHub { get; set; }
        public List<ChangeSetDepartureResponse> Departures { get; set; }
    }

    public sealed class ChangeSetDepartureResponse
    {
        public string RowKey { get; set; }
        public DateTime Date { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public string CompletedBy { get; set; }
        public List<ChangeSetCellResponse> Cells { get; set; }

        /// <summary>
        /// The table to paste onto the website for this departure, frozen when
        /// the set was submitted. Null on sets raised before this existed -
        /// those are shown as unavailable rather than regenerated, because a
        /// block produced today is not what was approved then.
        /// </summary>
        public string Html { get; set; }

        /// <summary>
        /// This departure has never been on the website, so tech support has to
        /// create the date before there is anywhere to put a price.
        /// </summary>
        /// <remarks>
        /// Derived rather than stored: LivePrice is the price the website shows,
        /// frozen at submission like everything else on the row, and a departure
        /// that was never published has none for any occupancy. A stored flag
        /// would be a second version of the same fact, free to disagree.
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

    public sealed class ChangeSetCellResponse
    {
        public string Occupancy { get; set; }
        public string Label { get; set; }
        public int SortOrder { get; set; }
        public decimal Published { get; set; }

        /// <summary>
        /// Null where the tour has no strike-through percentage, so there is no
        /// crossed-out price. Not zero — which is what this held before
        /// migration 0025, and what tech support would have published.
        /// </summary>
        public decimal? StrikeThrough { get; set; }

        /// <summary>What the website shows right now.</summary>
        public decimal? LivePrice { get; set; }

        /// <summary>
        /// What the product team changed this figure FROM: the same as
        /// LivePrice when one set is in flight, and the earlier set's submitted
        /// price when there are two.
        /// </summary>
        public decimal? BaselinePrice { get; set; }

        public bool HasChanged { get; set; }
        public bool HasConditions { get; set; }
    }

    public sealed class ChangeSetRemovalResponse
    {
        public string RowKey { get; set; }
        public string Hub { get; set; }
        public string Name { get; set; }
        public DateTime? Date { get; set; }
        public string Action { get; set; }
        public int? DepartureCount { get; set; }
        public decimal? LivePrice { get; set; }
        public bool IsCompleted { get; set; }
    }

    public sealed class CompleteRowRequest
    {
        public bool Completed { get; set; }
        public string CompletedBy { get; set; }
    }

    public sealed class ChangeSetProgressResponse
    {
        public int TotalRows { get; set; }
        public int CompletedRows { get; set; }
        public bool IsComplete { get; set; }

        /// <summary>
        /// True when this action completed the set, so the tour has been queued
        /// for promotion.
        /// </summary>
        public bool JustApplied { get; set; }

        public string VersionAfter { get; set; }
    }

    /// <summary>What is in flight for one tour, for the products list.</summary>
    public sealed class TourChangeSetStateResponse
    {
        public string TourCode { get; set; }
        public string Reference { get; set; }
        public DateTime SubmittedUtc { get; set; }
        public DateTime? AppliedUtc { get; set; }
        public string VersionAfter { get; set; }
        public int TotalRows { get; set; }
        public int CompletedRows { get; set; }
        public bool IsComplete { get; set; }

        public bool IsApplied
        {
            get { return AppliedUtc.HasValue; }
        }
    }

    // -----------------------------------------------------------------------
    // Promotion — what ChangeSets sends Pricing once a set is fully applied.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Tells Pricing what a change set was submitted with, so that further edits
    /// are measured against those prices rather than against what is still live.
    /// Without it, the next change set would repeat every row of this one.
    /// </summary>
    public sealed class RecordSubmittedRequest
    {
        public string Reference { get; set; }
        public List<PromotedPrice> Prices { get; set; }
    }

    public sealed class PromotedPrice
    {
        public string Hub { get; set; }
        public DateTime Date { get; set; }
        public string Occupancy { get; set; }
        public decimal Price { get; set; }
    }
}
