using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>
    /// Change sets: the work list tech support applies to the website, one
    /// set in full, and the ticks against each row.
    /// </summary>
    [Area("IntlGit")]
    public sealed class ChangeSetsController : Controller
    {
        private readonly ChangeSetService _changeSets;
        private readonly CurrentUser _user;

        public ChangeSetsController(ChangeSetService changeSets, CurrentUser user)
        {
            _changeSets = changeSets;
            _user = user;
        }

        // GET /IntlGit/ChangeSets?show=open&search=...&sortBy=tour&ascending=false
        [HttpGet]
        public async Task<IActionResult> Index(
            string show, string search, string sortBy = "submitted", bool ascending = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var vm = new ChangeSetsIndexViewModel
            {
                Show = show,
                Search = search,
                SortBy = sortBy,
                Ascending = ascending,
                IsTechSupport = _user.IsTechSupport
            };

            // Always fetch everything and filter here: "open" and "applied" are
            // two views of one list, and the banner needs the open count even
            // when you are looking at the applied ones.
            var list = await _changeSets.GetWorkListAsync(
                openOnly: false, sortBy: vm.SortBy, ascending: vm.Ascending, token: cancellationToken);

            var all = list.Items ?? new List<ChangeSetSummaryResponse>();
            var open = all.Where(s => !s.IsApplied).ToList();

            vm.OpenCount = open.Count;

            // Longest waiting.
            vm.Nearest = open.OrderBy(s => s.SubmittedUtc).FirstOrDefault();

            // Three named cases, so a fourth filter cannot silently fall through.
            var shown = all;
            if (vm.EffectiveShow == "applied")
            {
                shown = all.Where(s => s.IsApplied).ToList();
            }
            else if (vm.EffectiveShow == "open")
            {
                shown = open;
            }

            vm.TotalBeforeSearch = shown.Count;

            var needle = (vm.Search ?? string.Empty).Trim();
            if (needle.Length > 0)
            {
                shown = shown
                    .Where(s => (s.Reference + " " + s.TourCode + " " + s.TourName)
                        .IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            vm.Sets = shown;

            vm.Crumbs = vm.IsTechSupport
                ? new List<(string, string)> { ("Website updates", null) }
                : new List<(string, string)> { ("Pricing", null), ("Change sets", null) };

            return View(vm);
        }

        // GET /IntlGit/ChangeSets/Detail?reference=CS-2026-014
        [HttpGet]
        public async Task<IActionResult> Detail(string reference, CancellationToken cancellationToken)
        {
            // A missing reference means a link or a bookmark lost it.
            if (string.IsNullOrWhiteSpace(reference))
            {
                TempData["Toast"] = "That link did not say which change set to open.";
                return Redirect("/IntlGit/ChangeSets");
            }

            var vm = new ChangeSetDetailViewModel { Reference = reference, IsTechSupport = _user.IsTechSupport };

            vm.ChangeSet = await _changeSets.GetAsync(reference, cancellationToken);
            if (vm.ChangeSet == null)
            {
                TempData["Toast"] = "No change set with reference " + reference + ".";
                return Redirect("/IntlGit/ChangeSets");
            }

            foreach (var hub in vm.ChangeSet.Hubs)
            {
                foreach (var departure in hub.Departures)
                {
                    if (departure.Cells.Count > 0)
                    {
                        vm.Columns = departure.Cells;
                        break;
                    }
                }

                if (vm.Columns.Count > 0)
                {
                    break;
                }
            }

            vm.Crumbs = _user.IsTechSupport
                ? new List<(string, string)>
                {
                    ("Website updates", "/IntlGit/ChangeSets"),
                    (vm.ChangeSet.Summary.Reference, null)
                }
                : new List<(string, string)>
                {
                    ("Pricing", null),
                    ("Change sets", "/IntlGit/ChangeSets"),
                    (vm.ChangeSet.Summary.Reference, null)
                };

            return View(vm);
        }

        // POST /IntlGit/ChangeSets/Tick?reference=...&rowKey=...&completed=true
        [HttpPost]
        public async Task<IActionResult> Tick(
            string reference, string rowKey, bool completed, CancellationToken cancellationToken)
        {
            if (!_user.IsTechSupport)
            {
                return Forbid();
            }

            var result = await _changeSets.CompleteRowAsync(
                reference, rowKey, completed, _user.DisplayName ?? _user.RoleName, cancellationToken);

            TempData["Toast"] = await DescribeAsync(reference, result, cancellationToken);

            // Back to the row that was just ticked, not the top of a 49-row set.
            return Redirect("/IntlGit/ChangeSets/Detail?reference=" + Uri.EscapeDataString(reference ?? string.Empty)
                + "#" + ChangeSetDetailViewModel.RowAnchor(rowKey));
        }

        // POST /IntlGit/ChangeSets/CompleteAll?reference=...
        [HttpPost]
        public async Task<IActionResult> CompleteAll(string reference, CancellationToken cancellationToken)
        {
            if (!_user.IsTechSupport)
            {
                return Forbid();
            }

            var result = await _changeSets.CompleteAllAsync(
                reference, _user.DisplayName ?? _user.RoleName, cancellationToken);

            TempData["Toast"] = await DescribeAsync(reference, result, cancellationToken);
            return Redirect("/IntlGit/ChangeSets/Detail?reference=" + Uri.EscapeDataString(reference ?? string.Empty));
        }

        /// <summary>
        /// Says what happened. Completing the set is the moment worth calling
        /// out, because it is what moves the tour to a new version.
        /// </summary>
        private async Task<string> DescribeAsync(
            string reference, CompleteResult result, CancellationToken cancellationToken)
        {
            if (!result.Succeeded)
            {
                return result.Message;
            }

            var progress = result.Progress;

            if (!progress.JustApplied)
            {
                return progress.CompletedRows + " of " + progress.TotalRows + " rows updated.";
            }

            var applied = await _changeSets.GetAsync(reference, cancellationToken);
            var tour = applied == null ? "The tour" : applied.Summary.TourCode;

            return reference + " is complete — all " + progress.TotalRows
                + " rows applied. " + tour + " is now on " + progress.VersionAfter
                + ", and this list stays available as the record of what went live.";
        }
    }
}
