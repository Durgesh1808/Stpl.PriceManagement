using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Areas.IntlGit.Models.Rows;
using Stpl.PriceManagement.Areas.IntlGit.Repositories;
using Stpl.PriceManagement.Infrastructure;
using Stpl.PriceManagement.Infrastructure.Data;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Services
{
    /// <summary>
    /// The change-set workflow: create, read, tick, and close.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change set is the frozen hand-over to tech support. Raising one is the
    /// product team's act; ticking rows is tech support's; reading is open.
    /// </para>
    /// <para>
    /// Ticking the last row applies the set: usp_MarkChangeSetApplied stamps
    /// it, allocates the next version and promotes the tour in ONE transaction.
    /// (This used to be an outbox event delivered to a separate Pricing web
    /// service; with one database there is nothing to deliver.)
    /// </para>
    /// <para>
    /// This class merges the old ChangeSets API's service, its role check and
    /// validation, and the web app's ChangeSetsApiClient - same rules, same
    /// messages.
    /// </para>
    /// </remarks>
    public sealed class ChangeSetService
    {
        /// <summary>How many sets one call to the work-list procedure returns (it caps at 200).</summary>
        private const int WorkListPageSize = 200;

        /// <summary>A ceiling on the whole list, so a runaway table cannot be pulled into one page.</summary>
        private const int WorkListCeiling = 10000;

        private readonly IChangeSetRepository _repository;
        private readonly CurrentUser _user;
        private readonly ILogger<ChangeSetService> _logger;

        public ChangeSetService(IChangeSetRepository repository, CurrentUser user, ILogger<ChangeSetService> logger)
        {
            _repository = repository;
            _user = user;
            _logger = logger;
        }

        // ==================================================================
        // What the screens call
        // ==================================================================

        /// <summary>Freezes a change list and hands it to tech support. The product team's act.</summary>
        public async Task<SubmitResult> CreateAsync(CreateChangeSetRequest request, CancellationToken token)
        {
            if (!_user.IsProductExecutive)
            {
                return SubmitResult.Failed(ServiceResult.ForbiddenMessage);
            }

            var invalid = RequestValidators.Validate(request);
            if (invalid.HasErrors)
            {
                return SubmitResult.Failed(invalid.Message);
            }

            try
            {
                return SubmitResult.Ok(await CreateCoreAsync(request, token));
            }
            catch (SqlException ex) when (ProcedureErrors.IsBusinessRule(ex))
            {
                _logger.LogWarning("Creating a change set was refused ({Number}): {Message}", ex.Number, ex.Message);
                return SubmitResult.Failed(ex.Message);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Creating a change set for {Tour} failed.", request.TourCode);
                return SubmitResult.Failed(ServiceResult.UnexpectedMessage);
            }
        }

        /// <summary>
        /// The whole work list in one answer, every page fetched. "Open" and
        /// "applied" are two views of one list, so the screen filters it.
        /// </summary>
        public async Task<ChangeSetListResponse> GetWorkListAsync(
            bool openOnly, string sortBy, bool ascending, CancellationToken token)
        {
            var items = new List<ChangeSetSummaryResponse>();
            var total = 0;
            var page = 1;

            while (true)
            {
                var result = await GetWorkListPageCoreAsync(
                    openOnly, sortBy ?? "submitted", ascending, page, WorkListPageSize, token);

                if (result == null || result.Items == null || result.Items.Count == 0)
                {
                    break;
                }

                items.AddRange(result.Items);
                total = result.TotalCount;

                if (result.Items.Count < WorkListPageSize
                    || items.Count >= total
                    || items.Count >= WorkListCeiling)
                {
                    break;
                }

                page++;
            }

            return new ChangeSetListResponse
            {
                Items = items,
                TotalCount = total > items.Count ? total : items.Count,
                Page = 1,
                PageSize = items.Count
            };
        }

        /// <summary>Every change set raised for one tour, newest first.</summary>
        public Task<IReadOnlyList<ChangeSetSummaryResponse>> GetTourHistoryAsync(string tourCode, CancellationToken token)
        {
            return GetTourHistoryCoreAsync(tourCode, token);
        }

        /// <summary>One change set in full; null when there is no set with that reference.</summary>
        public async Task<ChangeSetDetailResponse> GetAsync(string reference, CancellationToken token)
        {
            try
            {
                return await GetCoreAsync(reference, token);
            }
            catch (SqlException ex) when (ProcedureErrors.IsNotFound(ex))
            {
                return null;
            }
        }

        /// <summary>
        /// What is in flight per tour, for the products list. Empty rather than
        /// failing: the list falls back to what pricing alone can say.
        /// </summary>
        public async Task<IReadOnlyList<TourChangeSetStateResponse>> GetByTourAsync(CancellationToken token)
        {
            try
            {
                return await GetByTourCoreAsync(token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogWarning(ex, "Change-set state could not be read; showing the products list without it.");
                return new List<TourChangeSetStateResponse>();
            }
        }

        /// <summary>Ticks or unticks one row. Ticking the last one applies the set. Tech support's act.</summary>
        public Task<CompleteResult> CompleteRowAsync(
            string reference, string rowKey, bool completed, string completedBy, CancellationToken token)
        {
            return ProgressAsync(completedBy, by => CompleteRowCoreAsync(reference, rowKey, completed, by, token), reference);
        }

        /// <summary>Ticks everything still outstanding. Tech support's act.</summary>
        public Task<CompleteResult> CompleteAllAsync(string reference, string completedBy, CancellationToken token)
        {
            return ProgressAsync(completedBy, by => CompleteAllCoreAsync(reference, by, token), reference);
        }

        private async Task<CompleteResult> ProgressAsync(
            string completedBy, Func<string, Task<ChangeSetProgressResponse>> act, string reference)
        {
            if (!_user.IsTechSupport)
            {
                return CompleteResult.Failed(ServiceResult.ForbiddenMessage);
            }

            var invalid = RequestValidators.Validate(new CompleteRowRequest { Completed = true, CompletedBy = completedBy });
            if (invalid.HasErrors)
            {
                return CompleteResult.Failed(invalid.Message);
            }

            // Who is acting: the name supplied, else the signed-in person.
            var by = !string.IsNullOrWhiteSpace(completedBy)
                ? completedBy
                : (_user.DisplayName ?? "Tech support");

            try
            {
                return CompleteResult.Ok(await act(by));
            }
            catch (SqlException ex) when (ProcedureErrors.IsBusinessRule(ex))
            {
                _logger.LogWarning("Ticking {Reference} was refused ({Number}): {Message}", reference, ex.Number, ex.Message);
                return CompleteResult.Failed(ex.Message);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Ticking {Reference} failed.", reference);
                return CompleteResult.Failed(ServiceResult.UnexpectedMessage);
            }
        }

        // ==================================================================
        // The work itself (unchanged from the ChangeSets API's service)
        // ==================================================================

        private async Task<ChangeSetProgressResponse> CompleteRowCoreAsync(
            string reference, string rowKey, bool completed, string completedBy,
            CancellationToken cancellationToken)
        {
            var progress = await _repository.CompleteRowAsync(
                reference, rowKey, completed, completedBy, cancellationToken);

            return await CloseIfCompleteAsync(reference, progress, cancellationToken);
        }

        private async Task<ChangeSetProgressResponse> CompleteAllCoreAsync(
            string reference, string completedBy, CancellationToken cancellationToken)
        {
            var progress = await _repository.CompleteAllAsync(reference, completedBy, cancellationToken);
            return await CloseIfCompleteAsync(reference, progress, cancellationToken);
        }

        private async Task<ChangeSetCreatedResponse> CreateCoreAsync(
            CreateChangeSetRequest request, CancellationToken cancellationToken)
        {
            var created = await _repository.CreateAsync(new CreateChangeSetCommand
            {
                TourCode = request.TourCode,
                TourName = request.TourName,
                Region = request.Region,
                VersionBefore = request.VersionBefore,
                Note = request.Note,
                SubmittedBy = request.SubmittedBy,
                SubmittedByEmail = request.SubmittedByEmail,
                Rows = request.Rows.Select(row => new CreateRowCommand
                {
                    RowKey = row.RowKey,
                    Kind = row.Kind,
                    HubCode = row.Hub,
                    HubName = row.HubName,
                    DepartureDate = row.Date,
                    IsNewHub = row.IsNewHub,
                    RemovalAction = row.RemovalAction,
                    RemovalCount = row.RemovalCount,
                    LivePriceAtSubmit = row.LivePriceAtSubmit,
                    SortOrder = row.SortOrder,

                    /*
                        Generated HERE, not by the caller.

                        The web app posts figures; what those figures look like
                        as markup on the website is this service's business,
                        because this service is what freezes them. Generating it
                        at the caller would mean every future caller had to
                        remember to, and the one that forgot would store a set
                        with no instruction in it.

                        Only price rows get one - there is no table to paste for
                        a departure being taken down.
                    */
                    HtmlBlock = BuildHtmlBlock(row),
                    Cells = (row.Cells ?? new List<ChangeSetCellRequest>())
                        .Select(cell => new CreateCellCommand
                        {
                            OccupancyCode = cell.Occupancy,
                            OccupancyLabel = cell.Label,
                            SortOrder = cell.SortOrder,
                            PublishedPrice = cell.Published,
                            StrikeThrough = cell.StrikeThrough,
                            LivePrice = cell.LivePrice,
                            BaselinePrice = cell.BaselinePrice,
                            HasChanged = cell.HasChanged,
                            HasConditions = cell.HasConditions
                        }).ToList()
                }).ToList()
            }, cancellationToken);

            return new ChangeSetCreatedResponse
            {
                Reference = created.Reference,
                TotalRows = created.TotalRows
            };
        }

        /// <summary>
        /// The website table for one departure, or null when there is nothing
        /// to paste.
        /// </summary>
        /// <remarks>
        /// A removal row has no table: it is an instruction to take a date
        /// down, not to price one. A price row with no cells would produce a
        /// table of dashes, which is worse than saying nothing.
        /// </remarks>
        private static string BuildHtmlBlock(ChangeSetRowRequest row)
        {
            if (row.Kind != RowKeys.PriceKind || row.Date == null
                || row.Cells == null || row.Cells.Count == 0)
            {
                return null;
            }

            return PriceTableHtml.Build(
                row.HubName,
                row.Date.Value,
                row.Cells.Select(c => new PriceTableCell
                {
                    Occupancy = c.Occupancy,
                    Published = c.Published,
                    StrikeThrough = c.StrikeThrough,
                    HasConditions = c.HasConditions
                }));
        }

        private async Task<ChangeSetListResponse> GetWorkListPageCoreAsync(
            bool openOnly, string sortBy, bool ascending, int page, int pageSize,
            CancellationToken cancellationToken)
        {
            var result = await _repository.GetWorkListAsync(new WorkListQuery
            {
                OpenOnly = openOnly,
                SortBy = sortBy,
                Ascending = ascending,
                Page = page,
                PageSize = pageSize
            }, cancellationToken);

            return new ChangeSetListResponse
            {
                Items = result.Rows.Select(Summary).ToList(),
                TotalCount = result.TotalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        /// <summary>Every change set raised for one tour, newest first.</summary>
        private async Task<IReadOnlyList<ChangeSetSummaryResponse>> GetTourHistoryCoreAsync(
            string tourCode, CancellationToken cancellationToken)
        {
            var rows = await _repository.GetTourHistoryAsync(tourCode, cancellationToken);
            return rows.Select(Summary).ToList();
        }

        /// <summary>Returns null when there is no set with that reference.</summary>
        private async Task<ChangeSetDetailResponse> GetCoreAsync(
            string reference, CancellationToken cancellationToken)
        {
            var data = await _repository.GetAsync(reference, cancellationToken);
            if (data == null)
            {
                return null;
            }

            var cellsByRow = data.Cells
                .GroupBy(c => c.RowKey)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.SortOrder).ToList());

            var priceRows = data.Rows
                .Where(r => r.Kind == RowKeys.PriceKind)
                .OrderBy(r => r.SortOrder)
                .ToList();

            var hubs = priceRows
                .GroupBy(r => new { r.HubCode, r.HubName, r.IsNewHub })
                .Select(group => new ChangeSetHubResponse
                {
                    Hub = group.Key.HubCode,
                    Name = group.Key.HubName,
                    IsNewHub = group.Key.IsNewHub,
                    Departures = group.Select(row => new ChangeSetDepartureResponse
                    {
                        RowKey = row.RowKey,
                        Date = row.DepartureDate ?? DateTime.MinValue,
                        IsCompleted = row.CompletedUtc.HasValue,
                        CompletedUtc = row.CompletedUtc,
                        CompletedBy = row.CompletedBy,
                        Html = row.HtmlBlock,
                        Cells = (cellsByRow.ContainsKey(row.RowKey)
                                ? cellsByRow[row.RowKey]
                                : new List<ChangeSetCellRecord>())
                            .Select(cell => new ChangeSetCellResponse
                            {
                                Occupancy = cell.OccupancyCode,
                                Label = cell.OccupancyLabel,
                                SortOrder = cell.SortOrder,
                                Published = cell.PublishedPrice,
                                StrikeThrough = cell.StrikeThrough,
                                LivePrice = cell.LivePrice,
                                BaselinePrice = cell.BaselinePrice,
                                HasChanged = cell.HasChanged,
                                HasConditions = cell.HasConditions
                            }).ToList()
                    }).OrderBy(d => d.Date).ToList()
                }).ToList();

            var removals = data.Rows
                .Where(r => r.Kind == RowKeys.RemovalKind)
                .OrderBy(r => r.SortOrder)
                .Select(row => new ChangeSetRemovalResponse
                {
                    RowKey = row.RowKey,
                    Hub = row.HubCode,
                    Name = row.HubName,
                    Date = row.DepartureDate,
                    Action = row.RemovalAction,
                    DepartureCount = row.RemovalCount,
                    LivePrice = row.LivePriceAtSubmit,
                    IsCompleted = row.CompletedUtc.HasValue
                }).ToList();

            return new ChangeSetDetailResponse
            {
                Summary = Summary(data.Header),
                Hubs = hubs,
                Removals = removals
            };
        }

        /// <summary>
        /// Finishes any change set whose rows are all ticked but which was
        /// never stamped applied. Returns how many it finished.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Ticking the last row applies the set in the same request. If that
        /// request dies in between, or the promotion inside the stamp fails,
        /// the rows stay ticked and nothing is stamped - the tour sits on its
        /// old version while every row says the work is done.
        /// </para>
        /// <para>
        /// <see cref="UnstampedChangeSetSweeper"/> calls this every few seconds.
        /// Safe to run as often as it likes: usp_MarkChangeSetApplied returns
        /// early on a set already applied and promotes nothing twice.
        /// </para>
        /// </remarks>
        public async Task<int> FinishUnstampedSetsAsync(CancellationToken cancellationToken)
        {
            /* A narrow query, not the work list. This runs every few seconds
               for ever to answer a question whose answer is almost always
               "none", so it asks only what it needs - and gets VersionBefore
               back with it, rather than fetching each set in full to read one
               string. */
            var stuck = await _repository.ListUnstampedAsync(cancellationToken);
            if (stuck.Count == 0)
            {
                return 0;
            }

            var finished = 0;
            foreach (var row in stuck)
            {
                // The version is allocated by the stamp, not decided here -
                // the same path an ordinary completion takes, so a repair
                // cannot number a tour differently from the way it would have
                // been numbered had nothing gone wrong.
                try
                {
                    var stamp = await _repository.MarkAppliedAsync(row.Reference, cancellationToken);
                    if (!stamp.AlreadyApplied)
                    {
                        finished++;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // One set that cannot be promoted must not hold up the others.
                    _logger.LogError(ex, "{Reference} could not be applied; will try again.", row.Reference);
                }
            }

            return finished;
        }

        private async Task<ChangeSetProgressResponse> CloseIfCompleteAsync(
            string reference, ProgressRow progress, CancellationToken cancellationToken)
        {
            var response = new ChangeSetProgressResponse
            {
                TotalRows = progress.TotalRows,
                CompletedRows = progress.CompletedRows,
                IsComplete = progress.IsComplete
            };

            if (!progress.IsComplete)
            {
                return response;
            }

            /*
                The version is allocated by the stamp, inside its transaction,
                and comes back with it.

                It used to be worked out here, from the version this set was
                built against and frozen onto it at submission. That reads as
                careful - this service does not know the tour's current state
                and must not ask for it - but the frozen version is the version
                the set was RAISED against, and two sets raised while a tour sat
                on v1 therefore both claimed v02. Pricing refused the second on
                its unique index, after this service had already stamped it
                applied, so tech support was told it was live and those prices
                were never recorded as live at all.

                Allocating at the stamp keeps the boundary - the procedure reads
                this service's own applied records, not Pricing's - while making
                the number follow the order things actually land in.
            */
            AppliedStamp stamp;
            try
            {
                stamp = await _repository.MarkAppliedAsync(reference, cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                /*
                    The stamp promotes the tour in the same transaction, so a
                    failure here leaves the set fully ticked but not applied -
                    and nothing is half-done. The tick itself is already saved,
                    so it is reported as saved; UnstampedChangeSetSweeper tries
                    the stamp again a few seconds later, which is what the
                    outbox used to do between the two old services.
                */
                _logger.LogError(
                    ex, "{Reference} is fully ticked but could not be applied yet; it will be retried.",
                    reference);
                return response;
            }

            response.JustApplied = !stamp.AlreadyApplied;
            response.VersionAfter = stamp.VersionAfter;

            return response;
        }

        private async Task<IReadOnlyList<TourChangeSetStateResponse>> GetByTourCoreAsync(
            CancellationToken cancellationToken)
        {
            var rows = await _repository.GetByTourAsync(cancellationToken);

            return rows.Select(row => new TourChangeSetStateResponse
            {
                TourCode = row.TourCode,
                Reference = row.Reference,
                SubmittedUtc = row.SubmittedUtc,
                AppliedUtc = row.AppliedUtc,
                VersionAfter = row.VersionAfter,
                TotalRows = row.TotalRows,
                CompletedRows = row.CompletedRows,
                IsComplete = row.IsComplete
            }).ToList();
        }

        private static ChangeSetSummaryResponse Summary(WorkListRow row)
        {
            return new ChangeSetSummaryResponse
            {
                Reference = row.Reference,
                TourCode = row.TourCode,
                TourName = row.TourName,
                Region = row.Region,
                VersionBefore = row.VersionBefore,
                VersionAfter = row.VersionAfter,
                Note = row.Note,
                SubmittedBy = row.SubmittedBy,
                SubmittedUtc = row.SubmittedUtc,
                AppliedUtc = row.AppliedUtc,
                TotalRows = row.TotalRows,
                CompletedRows = row.CompletedRows,
                IsComplete = row.IsComplete
            };
        }

        private static ChangeSetSummaryResponse Summary(ChangeSetHeaderRow row)
        {
            return new ChangeSetSummaryResponse
            {
                Reference = row.Reference,
                TourCode = row.TourCode,
                TourName = row.TourName,
                Region = row.Region,
                VersionBefore = row.VersionBefore,
                VersionAfter = row.VersionAfter,
                Note = row.Note,
                SubmittedBy = row.SubmittedBy,
                SubmittedUtc = row.SubmittedUtc,
                AppliedUtc = row.AppliedUtc,
                TotalRows = row.TotalRows,
                CompletedRows = row.CompletedRows,
                IsComplete = row.IsComplete
            };
        }
    }

    /// <summary>Outcome of raising a change set, with a message fit to show the person.</summary>
    public sealed class SubmitResult
    {
        private SubmitResult(bool succeeded, ChangeSetCreatedResponse created, string message)
        {
            Succeeded = succeeded;
            Created = created;
            Message = message;
        }

        public bool Succeeded { get; }
        public ChangeSetCreatedResponse Created { get; }
        public string Message { get; }

        public static SubmitResult Ok(ChangeSetCreatedResponse created)
        {
            return new SubmitResult(true, created, null);
        }

        public static SubmitResult Failed(string message)
        {
            return new SubmitResult(false, null, message);
        }
    }

    /// <summary>Outcome of ticking a row or completing a set.</summary>
    public sealed class CompleteResult
    {
        private CompleteResult(bool succeeded, ChangeSetProgressResponse progress, string message)
        {
            Succeeded = succeeded;
            Progress = progress;
            Message = message;
        }

        public bool Succeeded { get; }
        public ChangeSetProgressResponse Progress { get; }
        public string Message { get; }

        public static CompleteResult Ok(ChangeSetProgressResponse progress)
        {
            return new CompleteResult(true, progress, null);
        }

        public static CompleteResult Failed(string message)
        {
            return new CompleteResult(false, null, message);
        }
    }
}
