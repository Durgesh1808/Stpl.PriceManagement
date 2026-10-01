using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stpl.PriceManagement.Areas.IntlGit.Models.Rows;
using Stpl.PriceManagement.Infrastructure.Data;

namespace Stpl.PriceManagement.Areas.IntlGit.Repositories
{
    public interface IChangeSetRepository
    {
        Task<CreatedChangeSet> CreateAsync(CreateChangeSetCommand command, CancellationToken cancellationToken);

        Task<WorkListPage> GetWorkListAsync(WorkListQuery query, CancellationToken cancellationToken);

        /// <summary>Returns null when there is no set with that reference.</summary>
        Task<ChangeSetData> GetAsync(string reference, CancellationToken cancellationToken);

        Task<ProgressRow> CompleteRowAsync(
            string reference, string rowKey, bool completed, string completedBy, CancellationToken cancellationToken);

        Task<ProgressRow> CompleteAllAsync(
            string reference, string completedBy, CancellationToken cancellationToken);

        /// <summary>
        /// Stamps a finished change set as applied, allocates the version the
        /// tour moves to, and promotes the tour - all in one transaction inside
        /// intlgit.usp_MarkChangeSetApplied.
        /// </summary>
        Task<AppliedStamp> MarkAppliedAsync(string reference, CancellationToken cancellationToken);

        Task<IReadOnlyList<TourStateRow>> GetByTourAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Sets whose rows are all ticked but which were never stamped applied.
        /// Almost always empty.
        /// </summary>
        Task<IReadOnlyList<UnstampedRow>> ListUnstampedAsync(CancellationToken cancellationToken);

        /// <summary>Every change set raised for one tour, newest first.</summary>
        Task<IReadOnlyList<WorkListRow>> GetTourHistoryAsync(string tourCode, CancellationToken cancellationToken);
    }

    /// <summary>
    /// All database access for International GIT change sets (the hand-over to
    /// tech support). ADO.NET, stored procedures only.
    /// </summary>
    public sealed class ChangeSetRepository : IChangeSetRepository
    {
        private readonly SqlDatabase _db;

        public ChangeSetRepository(SqlDatabase db)
        {
            _db = db;
        }

        public Task<CreatedChangeSet> CreateAsync(CreateChangeSetCommand command, CancellationToken cancellationToken)
        {
            // Column order must match intlgit.ChangeRowList / intlgit.ChangeCellList.
            var rows = new DataTable();
            rows.Columns.Add("RowKey", typeof(string));
            rows.Columns.Add("Kind", typeof(string));
            rows.Columns.Add("HubCode", typeof(string));
            rows.Columns.Add("HubName", typeof(string));
            rows.Columns.Add("DepartureDate", typeof(DateTime));
            rows.Columns.Add("IsNewHub", typeof(bool));
            rows.Columns.Add("RemovalAction", typeof(string));
            rows.Columns.Add("RemovalCount", typeof(int));
            rows.Columns.Add("LivePriceAtSubmit", typeof(decimal));
            rows.Columns.Add("SortOrder", typeof(int));
            rows.Columns.Add("HtmlBlock", typeof(string));

            var cells = new DataTable();
            cells.Columns.Add("RowKey", typeof(string));
            cells.Columns.Add("OccupancyCode", typeof(string));
            cells.Columns.Add("OccupancyLabel", typeof(string));
            cells.Columns.Add("SortOrder", typeof(byte));
            cells.Columns.Add("PublishedPrice", typeof(decimal));
            cells.Columns.Add("StrikeThrough", typeof(decimal));
            cells.Columns.Add("LivePrice", typeof(decimal));
            cells.Columns.Add("BaselinePrice", typeof(decimal));
            cells.Columns.Add("HasChanged", typeof(bool));
            cells.Columns.Add("HasConditions", typeof(bool));

            foreach (var row in command.Rows)
            {
                rows.Rows.Add(
                    row.RowKey,
                    row.Kind,
                    row.HubCode,
                    row.HubName,
                    (object)row.DepartureDate ?? DBNull.Value,
                    row.IsNewHub,
                    (object)row.RemovalAction ?? DBNull.Value,
                    (object)row.RemovalCount ?? DBNull.Value,
                    (object)row.LivePriceAtSubmit ?? DBNull.Value,
                    row.SortOrder,
                    (object)row.HtmlBlock ?? DBNull.Value);

                foreach (var cell in row.Cells)
                {
                    cells.Rows.Add(
                        row.RowKey,
                        cell.OccupancyCode,
                        cell.OccupancyLabel,
                        (byte)cell.SortOrder,
                        cell.PublishedPrice,
                        (object)cell.StrikeThrough ?? DBNull.Value,
                        (object)cell.LivePrice ?? DBNull.Value,
                        (object)cell.BaselinePrice ?? DBNull.Value,
                        cell.HasChanged,
                        cell.HasConditions);
                }
            }

            return _db.QuerySingleAsync<CreatedChangeSet>("intlgit.usp_CreateChangeSet", p =>
            {
                p.Value("TourCode", command.TourCode);
                p.Value("TourName", command.TourName);
                p.Value("Region", command.Region);
                p.Value("VersionBefore", command.VersionBefore);
                p.Value("Note", command.Note);
                p.Value("SubmittedBy", command.SubmittedBy);
                p.Value("SubmittedByEmail", command.SubmittedByEmail);
                p.Table("Rows", "intlgit.ChangeRowList", rows);
                p.Table("Cells", "intlgit.ChangeCellList", cells);
            }, cancellationToken);
        }

        public async Task<WorkListPage> GetWorkListAsync(WorkListQuery query, CancellationToken cancellationToken)
        {
            var rows = await _db.QueryAsync<WorkListRow>("intlgit.usp_GetChangeSetWorkList", p =>
            {
                p.Value("OpenOnly", query.OpenOnly);
                p.Value("SortBy", query.SortBy);
                p.Value("Ascending", query.Ascending);
                p.Value("Page", query.Page);
                p.Value("PageSize", query.PageSize);
            }, cancellationToken);

            return new WorkListPage
            {
                Rows = rows,
                // The window function returns the same total on every row.
                TotalCount = rows.Count == 0 ? 0 : rows[0].TotalCount
            };
        }

        public Task<ChangeSetData> GetAsync(string reference, CancellationToken cancellationToken)
        {
            return _db.QueryMultipleAsync(
                "intlgit.usp_GetChangeSet",
                p => p.Value("Reference", reference),
                async sets =>
                {
                    // An unknown reference raises 60003, so reaching here means the set exists.
                    var header = (await sets.ReadAsync<ChangeSetHeaderRow>()).FirstOrDefault();
                    if (header == null)
                    {
                        return null;
                    }

                    return new ChangeSetData
                    {
                        Header = header,
                        Rows = await sets.ReadAsync<ChangeSetRowRecord>(),
                        Cells = await sets.ReadAsync<ChangeSetCellRecord>()
                    };
                },
                cancellationToken);
        }

        public Task<ProgressRow> CompleteRowAsync(
            string reference, string rowKey, bool completed, string completedBy, CancellationToken cancellationToken)
        {
            return _db.QuerySingleAsync<ProgressRow>("intlgit.usp_CompleteChangeSetRow", p =>
            {
                p.Value("Reference", reference);
                p.Value("RowKey", rowKey);
                p.Value("Completed", completed);
                p.Value("CompletedBy", completedBy);
            }, cancellationToken);
        }

        public Task<ProgressRow> CompleteAllAsync(
            string reference, string completedBy, CancellationToken cancellationToken)
        {
            return _db.QuerySingleAsync<ProgressRow>(
                "intlgit.usp_CompleteAllChangeSetRows",
                p => p.Value("Reference", reference).Value("CompletedBy", completedBy),
                cancellationToken);
        }

        public Task<AppliedStamp> MarkAppliedAsync(string reference, CancellationToken cancellationToken)
        {
            return _db.QuerySingleAsync<AppliedStamp>(
                "intlgit.usp_MarkChangeSetApplied", p => p.Value("Reference", reference), cancellationToken);
        }

        public async Task<IReadOnlyList<TourStateRow>> GetByTourAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<TourStateRow>("intlgit.usp_GetChangeSetsByTour", null, cancellationToken);
        }

        public async Task<IReadOnlyList<UnstampedRow>> ListUnstampedAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<UnstampedRow>("intlgit.usp_ListUnstampedChangeSets", null, cancellationToken);
        }

        public async Task<IReadOnlyList<WorkListRow>> GetTourHistoryAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<WorkListRow>(
                "intlgit.usp_GetChangeSetHistory", p => p.Value("TourCode", tourCode), cancellationToken);
        }
    }
}
