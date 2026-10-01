using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>
    /// The change list this revision would hand to tech support, and the point
    /// at which it is handed over (Submit).
    /// </summary>
    [Area("IntlGit")]
    public sealed class ReviewController : Controller
    {
        private readonly TourService _tours;
        private readonly ChangeSetService _changeSets;
        private readonly CurrentUser _user;
        private readonly ILogger<ReviewController> _logger;

        public ReviewController(
            TourService tours, ChangeSetService changeSets, CurrentUser user, ILogger<ReviewController> logger)
        {
            _tours = tours;
            _changeSets = changeSets;
            _user = user;
            _logger = logger;
        }

        // GET /IntlGit/Review?code=...
        [HttpGet]
        public async Task<IActionResult> Index(string code, CancellationToken cancellationToken)
        {
            if (_user.IsTechSupport)
            {
                return Redirect(_user.HomePage);
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["Toast"] = "That link did not say which tour to open.";
                return Redirect("/IntlGit/Products");
            }

            var vm = new ReviewViewModel { Code = code, IsProductExecutive = _user.IsProductExecutive };
            var loaded = await LoadAsync(vm, cancellationToken);
            return loaded ?? View(vm);
        }

        private async Task<IActionResult> LoadAsync(ReviewViewModel vm, CancellationToken cancellationToken)
        {
            vm.Revision = await _tours.GetRevisionAsync(vm.Code, cancellationToken);
            if (vm.Revision == null)
            {
                TempData["Toast"] = "No tour with code " + vm.Code + ".";
                return Redirect("/IntlGit/Products");
            }

            vm.Pending = await _tours.GetPendingChangesAsync(vm.Code, cancellationToken);

            /*
                The view reads Pending directly, so a null here would be a
                NullReferenceException on the way out. Null means the tour went
                between the two calls - not something to paper over with an
                empty list, which would report "nothing pending".
            */
            if (vm.Pending == null)
            {
                TempData["Toast"] =
                    "Could not load what is pending for " + vm.Code + ". Please try again.";
                return Redirect("/IntlGit/Revision?code=" + Uri.EscapeDataString(vm.Code));
            }

            vm.AwaitingFares = AwaitingFares.Count(vm.Revision);
            vm.Unconfirmed = UnconfirmedPrices.Count(vm.Revision);
            vm.UnconfirmedDepartures = UnconfirmedPrices.DepartureCount(vm.Revision);

            // Outstanding means submitted and not yet applied - the most recent
            // thing tech support still has.
            var states = await _changeSets.GetByTourAsync(cancellationToken);
            var outstanding = states
                .Where(s => s.TourCode == vm.Revision.Code && !s.IsApplied)
                .ToList();

            vm.InFlight = outstanding.FirstOrDefault();
            vm.OutstandingSets = outstanding.Count;

            vm.Crumbs = new List<(string, string)>
            {
                ("Pricing", null),
                ("Products", "/IntlGit/Products"),
                (vm.Revision.Code, "/IntlGit/Revision?code=" + vm.Revision.Code),
                ("Change set review", null)
            };

            return null;
        }

        /// <summary>
        /// Freezes the change list and hands it to tech support.
        /// </summary>
        /// <remarks>
        /// The list is re-read here rather than taken from the page that was
        /// rendered: between loading the screen and pressing the button,
        /// somebody may have edited the tour. What gets frozen must be what is
        /// true at the moment of submission.
        /// </remarks>
        // POST /IntlGit/Review/Submit?code=...
        [HttpPost]
        public async Task<IActionResult> Submit(string code, CancellationToken cancellationToken)
        {
            if (!_user.IsProductExecutive)
            {
                return Forbid();
            }

            var revision = await _tours.GetRevisionAsync(code, cancellationToken);
            if (revision == null)
            {
                return NotFound();
            }

            var pending = await _tours.GetPendingChangesAsync(code, cancellationToken);
            if (pending == null || pending.TotalChanges == 0)
            {
                TempData["Toast"] = "Nothing to submit — no prices have changed since "
                    + revision.Version + ".";
                return RedirectToAction("Index", new { code = code });
            }

            // Departures with no airfare have no price, so they are already
            // out of `pending`; they are mentioned so nobody thinks they went.
            var missing = AwaitingFares.Count(revision);

            // The person who submitted it, not their role.
            var request = BuildRequest(
                revision, pending, _user.DisplayName ?? _user.RoleName, _user.Email);
            var result = await _changeSets.CreateAsync(request, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["Toast"] = result.Message;
                return RedirectToAction("Index", new { code = code });
            }

            /*
                Record what was just submitted, so that any further edit to this
                tour produces a NEW request containing only what changed since.

                Done after the set exists, so a failure here never loses the set.
                The cost of it failing is a later request repeating some rows,
                which is visible and correctable.
            */
            var baseline = new RecordSubmittedRequest
            {
                Reference = result.Created.Reference,
                Prices = pending.Hubs
                    .SelectMany(h => h.Departures.Select(d => new { h.Hub, d.Date, d.Cells }))
                    .SelectMany(d => d.Cells.Select(c => new PromotedPrice
                    {
                        Hub = d.Hub,
                        Date = d.Date,
                        Occupancy = c.Occupancy,
                        Price = c.Published
                    }))
                    .ToList()
            };

            var recorded = await _tours.MarkSubmittedAsync(code, baseline, cancellationToken);
            if (!recorded.Succeeded)
            {
                _logger.LogError(
                    "Change set {Reference} was created but its submitted baseline was not recorded: {Message}",
                    result.Created.Reference, recorded.Message);
            }

            TempData["Toast"] = result.Created.Reference + " submitted · "
                + result.Created.TotalRows + " changes. Tech support can now apply it."
                + (missing > 0
                    ? " " + missing + (missing == 1 ? " departure" : " departures")
                        + " with no airfare stayed behind."
                    : "");

            return Redirect("/IntlGit/ChangeSets/Detail?reference="
                + Uri.EscapeDataString(result.Created.Reference));
        }

        /// <summary>
        /// Turns Pricing's change list into the frozen set ChangeSets stores.
        /// </summary>
        /// <remarks>
        /// Every cell travels, including the unchanged ones, so tech support can
        /// see which figures to leave alone. The row key is what they tick, and
        /// has to be stable across both services — see
        /// <see cref="RowKeys"/> for its shape.
        /// </remarks>
        private static CreateChangeSetRequest BuildRequest(
            TourRevisionResponse revision, PendingChangesResponse pending,
            string submittedBy, string submittedByEmail)
        {
            var rows = new List<ChangeSetRowRequest>();
            var order = 0;

            foreach (var hub in pending.Hubs)
            {
                foreach (var departure in hub.Departures)
                {
                    rows.Add(new ChangeSetRowRequest
                    {
                        RowKey = RowKeys.Price(hub.Hub, departure.Date),
                        Kind = RowKeys.PriceKind,
                        Hub = hub.Hub,
                        HubName = hub.Name,
                        Date = departure.Date,
                        IsNewHub = hub.IsNewHub,
                        SortOrder = order++,
                        Cells = departure.Cells.Select(cell => new ChangeSetCellRequest
                        {
                            Occupancy = cell.Occupancy,
                            Label = LabelFor(revision, cell.Occupancy),
                            SortOrder = cell.SortOrder,
                            Published = cell.Published,
                            StrikeThrough = cell.StrikeThrough,
                            LivePrice = cell.LivePrice,
                            BaselinePrice = cell.BaselinePrice,
                            HasChanged = cell.HasChanged,
                            HasConditions = cell.HasConditions
                        }).ToList()
                    });
                }
            }

            foreach (var removal in pending.Removals)
            {
                var isHub = removal.Kind == "hub";

                rows.Add(new ChangeSetRowRequest
                {
                    RowKey = isHub
                        ? RowKeys.RemovedHub(removal.Hub)
                        : RowKeys.RemovedDeparture(removal.Hub, removal.Date.Value),
                    Kind = RowKeys.RemovalKind,
                    Hub = removal.Hub,
                    HubName = removal.Name,
                    Date = removal.Date,
                    IsNewHub = false,
                    RemovalAction = isHub ? "Hub removed" : "Departure removed",
                    RemovalCount = removal.DepartureCount,
                    LivePriceAtSubmit = removal.LivePrice,
                    SortOrder = order++,
                    Cells = new List<ChangeSetCellRequest>()
                });
            }

            return new CreateChangeSetRequest
            {
                TourCode = revision.Code,
                TourName = revision.Name,
                Region = revision.Region,
                VersionBefore = revision.Version,
                Note = revision.CostBuild.Note,
                SubmittedBy = submittedBy,

                // The account, beside the name. This is what the "it is live"
                // notification is addressed to when tech support finish.
                SubmittedByEmail = submittedByEmail,
                Rows = rows
            };
        }
        /// <summary>
        /// The occupancy's display label. Pricing knows it; ChangeSets stores a
        /// copy so the set still reads correctly on its own.
        /// </summary>
        private static string LabelFor(TourRevisionResponse revision, string occupancy)
        {
            var match = revision.CostBuild.Occupancies
                .FirstOrDefault(o => o.Occupancy == occupancy);

            return match == null ? occupancy : match.Label;
        }
    }
}
