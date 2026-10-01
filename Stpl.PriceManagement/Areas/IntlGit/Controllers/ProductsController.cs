using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.Core.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;
using Stpl.PriceManagement.Infrastructure.Formatting;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>The products list: every tour, and where its price revision stands.</summary>
    [Area("IntlGit")]
    public sealed class ProductsController : Controller
    {
        private readonly TourService _tours;
        private readonly ChangeSetService _changeSets;
        private readonly CurrentUser _user;

        public ProductsController(TourService tours, ChangeSetService changeSets, CurrentUser user)
        {
            _tours = tours;
            _changeSets = changeSets;
            _user = user;
        }

        // GET /IntlGit/Products
        [HttpGet]
        public async Task<IActionResult> Index(
            string search, string region, string status, string sort, bool ascending,
            [FromQuery(Name = "pg")] int pageNumber = 1,
            [FromQuery(Name = "size")] int size = 25,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Tech support works from the change-set queue, never the product list.
            if (_user.IsTechSupport)
            {
                return Redirect(_user.HomePage);
            }

            var model = new ProductsIndexViewModel
            {
                Search = search,
                Region = region,
                Status = status,
                Sort = sort,
                Ascending = ascending,
                PageNumber = pageNumber,
                Size = size
            };

            var tours = await _tours.ListToursAsync(cancellationToken);

            /*
                What tech support is doing with each tour - one call for the
                whole list, and fault-tolerant: if it cannot be read every tour
                falls back to what pricing alone can say.

                usp_GetChangeSetsByTour returns applied AND unapplied sets, so
                "in flight" is filtered here.
            */
            var changeSets = await _changeSets.GetByTourAsync(cancellationToken);

            var inFlight = changeSets
                .Where(s => !s.IsApplied)
                .GroupBy(s => s.TourCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(s => s.SubmittedUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            var everSubmitted = changeSets
                .GroupBy(s => s.TourCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Max(s => s.SubmittedUtc),
                    StringComparer.OrdinalIgnoreCase);

            var everApplied = new HashSet<string>(
                changeSets.Where(s => s.IsApplied).Select(s => s.TourCode),
                StringComparer.OrdinalIgnoreCase);

            // Built from the UNFILTERED list, or picking a region collapses the
            // dropdown to that one region with no way back to the others.
            model.Regions = tours
                .Select(t => t.Region)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var needle = (model.Search ?? string.Empty).Trim();
            if (needle.Length > 0)
            {
                tours = tours
                    .Where(t => (t.Code + " " + t.Name)
                        .IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(model.Region))
            {
                tours = tours
                    .Where(t => string.Equals(t.Region, model.Region, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Mapped before the status filter: a status exists only on the row.
            var rows = tours.Select(tour => Row(
                tour,
                _user.Role,
                inFlight.ContainsKey(tour.Code) ? inFlight[tour.Code] : null,
                everSubmitted.ContainsKey(tour.Code)
                    ? everSubmitted[tour.Code]
                    : (DateTime?)null,
                everApplied.Contains(tour.Code))).ToList();

            RevisionStatus wanted;
            if (!string.IsNullOrWhiteSpace(model.Status)
                && Enum.TryParse(model.Status, true, out wanted))
            {
                rows = rows.Where(r => r.Status == wanted).ToList();
            }

            rows = SortRows(rows, model.Sort, model.Ascending);

            // An allow-list, not a clamp: ?size=0 divides by zero below, and
            // ?size=100000 asks the browser to lay out the whole master.
            if (model.Size != 25 && model.Size != 50 && model.Size != 100)
            {
                model.Size = 25;
            }

            model.RegionTotals = rows
                .GroupBy(r => r.Region ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            model.TotalCount = rows.Count;
            model.PageCount = Math.Max(1, (model.TotalCount + model.Size - 1) / model.Size);

            if (model.PageNumber < 1) { model.PageNumber = 1; }
            if (model.PageNumber > model.PageCount) { model.PageNumber = model.PageCount; }

            model.FirstRow = model.TotalCount == 0 ? 0 : ((model.PageNumber - 1) * model.Size) + 1;
            model.LastRow = Math.Min(model.PageNumber * model.Size, model.TotalCount);

            model.Tours = rows.Skip((model.PageNumber - 1) * model.Size).Take(model.Size).ToList();

            // TotalCount, not Tours.Count - otherwise every page claims 25 tours.
            var noun = model.TotalCount == 1 ? " tour" : " tours";
            model.Subtitle = _user.IsAirTicketing
                ? model.TotalCount + noun + " · open a tour to enter airfare for its departures"
                : model.TotalCount + noun + " · open a tour to run its weekly price revision";

            return View(model);
        }

        /// <summary>
        /// Region first, always; then whatever column the reader chose. Region is
        /// the heading the list is grouped by, not a sort option.
        /// </summary>
        private static List<ProductsIndexViewModel.TourRow> SortRows(
            List<ProductsIndexViewModel.TourRow> rows, string sort, bool ascending)
        {
            var grouped = rows.OrderBy(r => r.Region, StringComparer.OrdinalIgnoreCase);

            switch (sort)
            {
                case "tour":
                    return ascending
                        ? grouped.ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ToList()
                        : grouped.ThenByDescending(r => r.Code, StringComparer.OrdinalIgnoreCase).ToList();
                case "deps":
                    return ascending
                        ? grouped.ThenBy(r => r.DepartureCount).ToList()
                        : grouped.ThenByDescending(r => r.DepartureCount).ToList();
                case "hubs":
                    return ascending
                        ? grouped.ThenBy(r => r.HubCount).ToList()
                        : grouped.ThenByDescending(r => r.HubCount).ToList();
                case "status":
                    return ascending
                        ? grouped.ThenBy(r => r.Status).ToList()
                        : grouped.ThenByDescending(r => r.Status).ToList();
                case "updated":
                    return ascending
                        ? grouped.ThenBy(r => r.ModifiedUtc).ToList()
                        : grouped.ThenByDescending(r => r.ModifiedUtc).ToList();
                default:
                    // Region, then the master's own order within it.
                    return grouped.ToList();
            }
        }

        /// <summary>One tour's row, including where it stands.</summary>
        private static ProductsIndexViewModel.TourRow Row(
            TourSummaryResponse tour,
            UserRole role,
            TourChangeSetStateResponse inFlight,
            DateTime? lastChangeSetUtc,
            bool hasAppliedChangeSet)
        {
            /*
                "Fares received": air-ticketing has sent fares that nobody has
                turned into a change set yet. "There is work" counts held
                departures as well as pending ones - fares arriving blank every
                price built on them, and a blank price is held, not pending.
            */
            var awaitingPrice = tour.FaresSubmittedUtc.HasValue
                && (tour.PendingChanges > 0 || tour.HeldDepartures > 0)
                && (!lastChangeSetUtc.HasValue
                    || tour.FaresSubmittedUtc.Value > lastChangeSetUtc.Value);

            // Sent to air-ticketing and not answered: an open fare request, or
            // a departure's fare sent back.
            var withAirTicketing = tour.FareRequestedUtc.HasValue
                || tour.QueriedDepartures > 0;

            var status = RevisionStatuses.Evaluate(
                pendingChanges: tour.PendingChanges,
                hasOpenChangeSet: inFlight != null,
                completedRows: inFlight == null ? 0 : inFlight.CompletedRows,
                totalRows: inFlight == null ? 0 : inFlight.TotalRows,
                awaitingPriceAfterFares: awaitingPrice,
                hasAppliedChangeSet: hasAppliedChangeSet,
                heldDepartures: tour.HeldDepartures,
                withAirTicketing: withAirTicketing);

            string detail;
            if (inFlight != null)
            {
                // With tech support: which set and how far.
                detail = inFlight.Reference + " · "
                    + inFlight.CompletedRows + " of " + inFlight.TotalRows + " applied";
            }
            else if (status == RevisionStatus.WithAirTicketing)
            {
                detail = tour.FareRequestedUtc.HasValue
                    ? (role == UserRole.AirTicketingExecutive ? "requested · " : "sent · ")
                        + DepartureDateFormat.Stamp(tour.FareRequestedUtc.Value.ToLocalTime())
                    : tour.QueriedDepartures
                        + (tour.QueriedDepartures == 1 ? " departure sent back" : " departures sent back");
            }
            else if (status == RevisionStatus.Held)
            {
                detail = tour.HeldDepartures
                    + (tour.HeldDepartures == 1 ? " departure held" : " departures held");
            }
            else if (awaitingPrice)
            {
                // The same fact from either side of the hand-over.
                detail = (role == UserRole.AirTicketingExecutive ? "sent · " : "from air-ticketing · ")
                    + DepartureDateFormat.Stamp(tour.FaresSubmittedUtc.Value.ToLocalTime());
            }
            else if (tour.PendingChanges > 0)
            {
                detail = tour.PendingChanges + " changes pending";
            }
            else
            {
                detail = tour.Version;
            }

            return new ProductsIndexViewModel.TourRow
            {
                Code = tour.Code,
                Name = tour.Name,
                Region = tour.Region,
                Duration = tour.Duration,
                HubCount = tour.HubCount,
                DepartureCount = tour.DepartureCount,
                Status = status,
                StatusDetail = detail,
                UpdatedLabel = Relative(tour.LastModifiedUtc),
                ModifiedUtc = tour.LastModifiedUtc
            };
        }

        /// <summary>"Today, 11:20" for something changed today; older entries get the date.</summary>
        private static string Relative(DateTime utc)
        {
            var local = utc.ToLocalTime();
            var days = (DateTime.Now.Date - local.Date).Days;

            if (days <= 0)
            {
                return "Today, " + local.ToString("HH:mm");
            }

            if (days == 1)
            {
                return "Yesterday";
            }

            return days < 14 ? days + " days ago" : DepartureDateFormat.Long(local);
        }
    }
}
