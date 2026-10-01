using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.IntlGit.Models.Rows
{
    /*
        Shapes returned by, and passed to, the stored procedures. These mirror
        the result sets exactly and nothing else — the API projects its own DTOs
        from them, so a column rename here is not a breaking API change.
    */

    public sealed class CreateChangeSetCommand
    {
        public string TourCode { get; set; }
        public string TourName { get; set; }
        public string Region { get; set; }
        public string VersionBefore { get; set; }
        public string Note { get; set; }
        public string SubmittedBy { get; set; }
        public string SubmittedByEmail { get; set; }
        public IReadOnlyList<CreateRowCommand> Rows { get; set; }
    }

    public sealed class CreateRowCommand
    {
        public string RowKey { get; set; }
        public string Kind { get; set; }
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime? DepartureDate { get; set; }
        public bool IsNewHub { get; set; }
        public string RemovalAction { get; set; }
        public int? RemovalCount { get; set; }
        public decimal? LivePriceAtSubmit { get; set; }
        public int SortOrder { get; set; }
        public IReadOnlyList<CreateCellCommand> Cells { get; set; }

        /// <summary>The website table for this departure, frozen with it.</summary>
        public string HtmlBlock { get; set; }
    }

    public sealed class CreateCellCommand
    {
        public string OccupancyCode { get; set; }
        public string OccupancyLabel { get; set; }
        public int SortOrder { get; set; }
        public decimal PublishedPrice { get; set; }
        /// <summary>Null where there is no crossed-out price. Not zero.</summary>
        public decimal? StrikeThrough { get; set; }

        /// <summary>What the website shows.</summary>
        public decimal? LivePrice { get; set; }

        /// <summary>What the change was measured against - see migration 0015.</summary>
        public decimal? BaselinePrice { get; set; }

        public bool HasChanged { get; set; }
        public bool HasConditions { get; set; }
    }

    public sealed class CreatedChangeSet
    {
        public string Reference { get; set; }
        public int ChangeSetId { get; set; }
        public int TotalRows { get; set; }
    }

    public sealed class WorkListQuery
    {
        public bool OpenOnly { get; set; }
        public string SortBy { get; set; } = "submitted";
        public bool Ascending { get; set; } = true;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public sealed class WorkListRow
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

        /// <summary>Total across all pages, from a window function.</summary>
        public int TotalCount { get; set; }
    }

    public sealed class WorkListPage
    {
        public IReadOnlyList<WorkListRow> Rows { get; set; }
        public int TotalCount { get; set; }
    }

    public sealed class ChangeSetHeaderRow
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
    }

    public sealed class ChangeSetRowRecord
    {
        public string RowKey { get; set; }
        public string Kind { get; set; }
        public string HubCode { get; set; }
        public string HubName { get; set; }
        public DateTime? DepartureDate { get; set; }
        public bool IsNewHub { get; set; }
        public string RemovalAction { get; set; }
        public int? RemovalCount { get; set; }
        public decimal? LivePriceAtSubmit { get; set; }
        public int SortOrder { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public string CompletedBy { get; set; }

        /// <summary>Null on sets raised before the generator existed.</summary>
        public string HtmlBlock { get; set; }
    }

    public sealed class ChangeSetCellRecord
    {
        public string RowKey { get; set; }
        public string OccupancyCode { get; set; }
        public string OccupancyLabel { get; set; }
        public int SortOrder { get; set; }
        public decimal PublishedPrice { get; set; }
        /// <summary>Null where there is no crossed-out price. Not zero.</summary>
        public decimal? StrikeThrough { get; set; }
        public decimal? LivePrice { get; set; }

        /// <summary>
        /// Falls back to LivePrice in the procedure for sets frozen before this
        /// was recorded, so callers have one column to read.
        /// </summary>
        public decimal? BaselinePrice { get; set; }

        public bool HasChanged { get; set; }
        public bool HasConditions { get; set; }
    }

    public sealed class ChangeSetData
    {
        public ChangeSetHeaderRow Header { get; set; }
        public IReadOnlyList<ChangeSetRowRecord> Rows { get; set; }
        public IReadOnlyList<ChangeSetCellRecord> Cells { get; set; }
    }

    public sealed class ProgressRow
    {
        public int TotalRows { get; set; }
        public int CompletedRows { get; set; }
        public bool IsComplete { get; set; }
    }

    public sealed class TourStateRow
    {
        public string TourCode { get; set; }
        public string Reference { get; set; }
        public DateTime SubmittedUtc { get; set; }
        public DateTime? AppliedUtc { get; set; }
        public string VersionAfter { get; set; }
        public int TotalRows { get; set; }
        public int CompletedRows { get; set; }
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// What came back from stamping a change set applied.
    /// </summary>
    /// <remarks>
    /// <see cref="VersionAfter"/> is allocated by the procedure rather than
    /// supplied to it, so this is the only place the caller can learn it — and
    /// it is filled in both cases. When <see cref="AlreadyApplied"/> is true it
    /// carries the version the set was given the first time, because the caller
    /// still has to report one and no longer computes its own.
    /// </remarks>
    public sealed class AppliedStamp
    {
        public bool AlreadyApplied { get; set; }
        public string VersionAfter { get; set; }
    }

    /// <summary>
    /// A change set that was finished but never stamped applied.
    /// </summary>
    /// <remarks>
    /// The reference alone, because that is all the repair needs: the version
    /// is allocated by <c>usp_MarkChangeSetApplied</c> when the stamp happens, not
    /// worked out here beforehand.
    /// </remarks>
    public sealed class UnstampedRow
    {
        public string Reference { get; set; }
    }
}
