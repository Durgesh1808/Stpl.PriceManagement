using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;
using Stpl.PriceManagement.Infrastructure.Formatting;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>
    /// The price revision workspace for one tour: cost build, hubs, departures,
    /// airfare, published prices, the fare hand-over, and who changed what.
    /// </summary>
    /// <remarks>
    /// GET  /IntlGit/Revision?code=FBFE9&amp;edit=true&amp;open=blr,del  - the page
    /// POST /IntlGit/Revision/{action}?code=...                      - one per form
    ///
    /// Every save is POST-redirect-GET back to the page, anchored to the part
    /// of the page that was being worked on. The one exception is a save
    /// refused because somebody else moved the tour: that re-draws the page in
    /// place with what was typed still in the boxes.
    /// </remarks>
    [Area("IntlGit")]
    public sealed class RevisionController : Controller
    {
        private readonly TourService _tours;
        private readonly ChangeSetService _changeSets;
        private readonly CurrentUser _user;

        public RevisionController(TourService tours, ChangeSetService changeSets, CurrentUser user)
        {
            _tours = tours;
            _changeSets = changeSets;
            _user = user;
        }

        /// <summary>The page's own state, as it arrived in the address.</summary>
        private static RevisionViewModel Page(string code, bool edit, string open)
        {
            return new RevisionViewModel { Code = code, Edit = edit, Open = open };
        }

        // GET /IntlGit/Revision?code=...
        [HttpGet]
        public async Task<IActionResult> Index(
            string code, bool edit, string open, CancellationToken cancellationToken)
        {
            if (_user.IsTechSupport)
            {
                return Redirect(_user.HomePage);
            }

            // A missing code means a link or a bookmark lost it. Send them to
            // the list with a word of explanation rather than a 404.
            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["Toast"] = "That link did not say which tour to open.";
                return Redirect("/IntlGit/Products");
            }

            var vm = Page(code, edit, open);
            var loaded = await LoadAsync(vm, cancellationToken);
            return loaded ?? View("Index", vm);
        }

        /// <summary>
        /// Loads everything the page draws. Returns a result to short-circuit
        /// with, or null when the page can render.
        /// </summary>
        private async Task<IActionResult> LoadAsync(RevisionViewModel vm, CancellationToken cancellationToken)
        {
            vm.Revision = await _tours.GetRevisionAsync(vm.Code, cancellationToken);
            if (vm.Revision == null)
            {
                TempData["Toast"] = "No tour with code " + vm.Code + ".";
                return Redirect("/IntlGit/Products");
            }

            vm.Pending = await _tours.GetPendingChangesAsync(vm.Code, cancellationToken);
            vm.HubMaster = await _tours.ListHubsAsync(cancellationToken);
            vm.Permissions = _user.PermissionsFor(vm.Edit);

            // A departure with no airfare has no price, so it cannot go to the
            // website. Counted from the grid the page already loaded.
            vm.AwaitingFares = AwaitingFares.Count(vm.Revision);

            // Prices whose fare or cost has moved and which nobody has agreed to since.
            vm.UnconfirmedPrices = UnconfirmedPrices.Count(vm.Revision);
            vm.UnconfirmedDepartures = UnconfirmedPrices.DepartureCount(vm.Revision);

            // Open queries, for the panel. The grid reads IsQueried off the cells.
            vm.FareQueries = await _tours.GetFareQueriesAsync(vm.Code, cancellationToken);

            vm.History = await _changeSets.GetTourHistoryAsync(vm.Code, cancellationToken);

            // Which of those versions can be opened in full (only versions
            // promoted since snapshots existed have a photograph).
            var versions = await _tours.GetVersionHistoryAsync(vm.Code, cancellationToken);
            var snapshots = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var version in versions)
            {
                snapshots[version.Version] = version.HasSnapshot;
            }

            vm.VersionSnapshots = snapshots;

            vm.Activity = await _tours.GetActivityAsync(vm.Code, cancellationToken);
            vm.Sessions = GroupActivity(vm.Activity);

            // Only the product team acts on an open request, so only they pay
            // for the extra call.
            if (_user.IsProductExecutive)
            {
                var requests = await _tours.ListFareRequestsAsync(cancellationToken);
                var mine = requests.FirstOrDefault(r =>
                    string.Equals(r.TourCode, vm.Code, StringComparison.OrdinalIgnoreCase));

                if (mine != null)
                {
                    vm.FareRequestedUtc = mine.RequestedUtc;
                }
            }

            vm.Crumbs = new List<(string, string)>
            {
                ("Pricing", null),
                ("Products", "/IntlGit/Products"),
                (vm.Revision.Code, null),
                ("Price revision", null)
            };

            return null;
        }

        /// <summary>
        /// Somebody else changed this tour while this page was open.
        /// </summary>
        /// <remarks>
        /// It says who moved the tour and what they moved, old to new, and it
        /// RE-RENDERS rather than redirecting so everything typed is still on
        /// the screen - "type your twenty prices again" would be a worse outcome
        /// than the overwrite this is preventing.
        /// </remarks>
        private async Task<IActionResult> RefuseStaleAsync(
            RevisionViewModel vm, HubGridForm form, DateTime? seenAt, CancellationToken cancellationToken)
        {
            vm.Posted = form;

            var changes = seenAt.HasValue
                ? await _tours.GetChangesSinceAsync(vm.Code, seenAt.Value, cancellationToken)
                : new List<ActivityResponse>();

            vm.StaleChanges = changes;

            // Reload the page's own data so it can draw. LoadAsync redirects if
            // the tour has gone entirely, which has to win.
            var reloaded = await LoadAsync(vm, cancellationToken);
            if (reloaded != null)
            {
                return reloaded;
            }

            var who = changes
                .Select(c => c.ActorName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            vm.StaleMessage = who.Count == 1
                ? who[0] + " changed this tour while you had it open."
                : who.Count > 1
                    ? string.Join(" and ", who) + " changed this tour while you had it open."
                    : "This tour changed while you had it open.";

            return View("Index", vm);
        }

        // --- Cost build ----------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> CostBuild(
            string code, bool edit, string open, [FromForm] CostBuildForm form, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            var permissions = _user.PermissionsFor(true);
            if (!permissions.CanEditCostBuild)
            {
                return Forbid();
            }

            var current = await _tours.GetRevisionAsync(vm.Code, cancellationToken);
            if (current == null)
            {
                return NotFound();
            }

            /* An empty box is allowed and binds to null. Something that is not
               a number at all - "abt 1800", a pasted currency symbol - fails to
               bind, and unchecked that arrived here as though the box had been
               left blank, silently discarding what somebody typed. Say so
               instead, and keep them in edit mode with the work on screen. */
            if (!ModelState.IsValid)
            {
                TempData["Toast"] =
                    "Some figures could not be read. Enter digits only, or leave a box empty.";
                return Redirect(vm.Self(edit: null, anchor: "cost-build"));
            }

            var request = new SaveCostBuildRequest
            {
                FxRate = form.FxRate,
                StrikePercent = form.StrikePercent,
                PaxSlab = form.PaxSlab,
                SharedCost = form.SharedCost,
                Note = form.Note,
                Occupancies = current.CostBuild.Occupancies.Select(o => new OccupancyCostRequest
                {
                    Occupancy = o.Occupancy,
                    LandCostFx = Value(form.Land, o.Occupancy, o.LandCostFx),
                    PerPersonInr = Value(form.PerPerson, o.Occupancy, o.PerPersonInr)
                }).ToList()
            };

            var result = await _tours.SaveCostBuildAsync(vm.Code, request, cancellationToken);
            TempData["Toast"] = result.Succeeded
                ? "Cost build saved. Prices below have been recalculated."
                : result.Message;

            // A successful save leaves edit mode, so the screen shows what was
            // actually stored rather than the boxes it was typed into. A failed
            // one stays in edit mode - the work is still on screen to correct.
            return Redirect(vm.Self(edit: result.Succeeded ? false : (bool?)null, anchor: "cost-build"));
        }

        // --- One hub's grid: airfares and published prices ------------------
        /// <summary>
        /// Saves a hub's fare and price grid.
        /// </summary>
        /// <remarks>
        /// Both live in one form because they are interleaved in one table, and
        /// one Save per hub is what the grid looks like it should do.
        ///
        /// Only figures that actually differ from what was rendered are sent on.
        /// That matters most for the published price: saving one creates a
        /// deliberate override that holds even when a cost changes, so posting
        /// every cell back would silently freeze the entire tour's prices.
        ///
        /// This used to carry confirmHub and confirmDate as well, from the two
        /// "Confirm these" buttons. Both are gone: a cell is blank exactly
        /// while nobody has agreed to it, so a confirm that may not supply a
        /// figure has nothing it can act on.
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> HubGrid(
            string code, bool edit, string open, [FromForm] HubGridForm form, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            var permissions = _user.PermissionsFor(true);
            if (!permissions.CanEditFares)
            {
                return Forbid();
            }

            var current = await _tours.GetRevisionAsync(vm.Code, cancellationToken);
            if (current == null)
            {
                return NotFound();
            }

            var messages = new List<string>();

            /*
                The tour as this page saw it. Every save below carries it, and
                the first one to find the tour has moved refuses - so a screen
                left open cannot overwrite somebody else's work, and a save
                that never saw a star cannot delete it.

                Threaded through all three rather than checked once here:
                checking in the handler and writing afterwards is the gap this
                exists to close.
            */
            DateTime? stamp = form.ExpectedModifiedUtc > 0
                ? new DateTime(form.ExpectedModifiedUtc, DateTimeKind.Utc)
                : (DateTime?)null;

            var fares = ChangedFares(form, current).ToList();
            if (fares.Count > 0)
            {
                var result = await _tours.SaveFaresAsync(
                    vm.Code, new SaveFaresRequest { Fares = fares, ExpectedModifiedUtc = stamp }, cancellationToken);

                if (result.IsStale)
                {
                    return await RefuseStaleAsync(vm, form, stamp, cancellationToken);
                }

                if (!result.Succeeded)
                {
                    TempData["Toast"] = result.Message;
                    return Redirect(vm.Self());
                }

                // That write moved the stamp. Carry the new one forward, or the
                // next write of this same save would be refused by this one.
                stamp = await _tours.GetTourStampAsync(vm.Code, cancellationToken);

                messages.Add(fares.Count + (fares.Count == 1 ? " fare" : " fares"));
            }

            // Flight notes travel with the fares because they are typed on the
            // same row and committed by the same Save. Saved separately, so a
            // note can be corrected on a date whose fares have not moved.
            var flights = ChangedFlightDetails(form, current).ToList();
            if (flights.Count > 0)
            {
                var result = await _tours.SaveFlightDetailsAsync(
                    vm.Code, new SaveFlightDetailsRequest { Details = flights }, cancellationToken);

                if (!result.Succeeded)
                {
                    TempData["Toast"] = result.Message;
                    return Redirect(vm.Self());
                }

                messages.Add(flights.Count
                    + (flights.Count == 1 ? " flight note" : " flight notes"));
            }

            if (permissions.CanEditPublishedPrice)
            {
                var prices = ChangedPrices(form, current).ToList();
                if (prices.Count > 0)
                {
                    var result = await _tours.SavePricesAsync(
                        vm.Code, new SavePricesRequest { Prices = prices, ExpectedModifiedUtc = stamp }, cancellationToken);

                    if (result.IsStale)
                    {
                        return await RefuseStaleAsync(vm, form, stamp, cancellationToken);
                    }

                    if (!result.Succeeded)
                    {
                        TempData["Toast"] = result.Message;
                        return Redirect(vm.Self());
                    }

                    stamp = await _tours.GetTourStampAsync(vm.Code, cancellationToken);

                    messages.Add(prices.Count + (prices.Count == 1 ? " price" : " prices"));
                }

                /*
                    Cells whose box was filled in but whose figure is the one the
                    system calculated. Saving those as prices would be wrong -
                    an override is a decision to hold a price against future cost
                    changes, and agreeing with the calculation is not that. So
                    they are confirmed instead, which is the fact that actually
                    changed.
                */
                var agreed = AgreedCells(form, current, prices).ToList();
                if (agreed.Count > 0)
                {
                    var result = await _tours.ConfirmPricesAsync(
                        vm.Code, new ConfirmPricesRequest { Cells = agreed }, cancellationToken);

                    if (!result.Succeeded)
                    {
                        TempData["Toast"] = result.Message;
                        return Redirect(vm.Self());
                    }
                }

                var confirmedHere = prices.Count + agreed.Count;
                if (confirmedHere > 0)
                {
                    messages.Add(confirmedHere + " confirmed");
                }

                /*
                    Stars. An unticked box posts nothing, so the ticked list only
                    means something against a scope - and the scope is the hubs
                    whose grids were actually rendered, which the form states.
                    Without it, saving with one hub open would clear the stars on
                    every other hub, because their cells were absent from a list
                    that was never describing them.
                */
                var shown = (form.Hubs ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => h.Trim())
                    .Where(h => h.Length > 0)
                    .ToList();

                if (shown.Count > 0)
                {
                    var result = await _tours.SetConditionsAsync(
                        vm.Code,
                        new SetConditionsRequest
                        {
                            Hubs = shown,
                            Cells = StarredCells(form).ToList(),
                            ExpectedModifiedUtc = stamp
                        },
                        cancellationToken);

                    if (result.IsStale)
                    {
                        return await RefuseStaleAsync(vm, form, stamp, cancellationToken);
                    }

                    if (!result.Succeeded)
                    {
                        TempData["Toast"] = result.Message;
                        return Redirect(vm.Self());
                    }
                }
            }

            /*
                There is no bulk confirmation step here any more.

                Agreeing to a figure and having a figure became the same fact:
                a cell is blank exactly while nobody has agreed to it, so a
                confirm that refuses to supply a figure - which is what it was
                asked to be - has nothing left it can act on. It confirmed
                zero cells every time and said it had worked.

                Saving a price confirms it (usp_SavePublishedPrices), and so
                does "same as calculated SP" on the hub header. Those are the
                two ways a blank cell is settled, and both write the figure and
                the agreement together.
            */

            /*
                The blank-price question, for a browser with the script blocked.

                With the script running it is asked BEFORE anything is posted,
                which is the better moment. Without it, the save has already
                happened by the time we know - so the same sentence arrives as
                part of the toast, a second later rather than a second earlier.

                Nothing is lost either way: saving blanks is allowed, and the
                departures are simply held out of the change set. What was
                missing was anybody saying so.

                Counted from the posted form, which is exactly what the script
                counts, so the two paths never quote different numbers.
            */
            var blank = BlankPrices(form);

            var saved = messages.Count == 0
                ? "Nothing changed — no figures were different."
                : string.Join(" and ", messages) + " saved. Prices recalculated.";

            TempData["Toast"] = blank == 0
                ? saved
                : saved + " " + blank + " price " + (blank == 1 ? "cell is" : "cells are")
                    + " still blank, so " + (blank == 1 ? "that departure is" : "those departures are")
                    + " not in the change request yet.";

            return Redirect(vm.Self(edit: false, anchor: HubAnchor(form)));
        }

        // --- Structure -------------------------------------------------------
        /// <summary>
        /// Adds a hub, with or without the tour's existing departure dates.
        /// </summary>
        /// <remarks>
        /// <paramref name="copyDates"/> defaults to true so an older caller and
        /// the scenario suite, neither of which sends it, behave as before.
        ///
        /// The toast says which of the two happened. A message describing work
        /// that was not done is worse than no message: somebody told their hub
        /// carried the tour's dates would not go looking for the date form.
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> AddHub(
            string code, bool edit, string open, string hub, decimal? markup, CancellationToken cancellationToken,
            bool copyDates = true)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanChangeStructure)
            {
                return Forbid();
            }

            var result = await _tours.AddHubAsync(
                vm.Code,
                new AddHubRequest
                {
                    Hub = hub,
                    MarkupPercent = markup,
                    CopyDepartureDates = copyDates
                },
                cancellationToken);

            if (result.Succeeded)
            {
                TempData["Toast"] = copyDates
                    ? "Hub added, carrying this tour's departure dates. Enter its fares below."
                    : "Hub added. Add its departure dates below.";
            }
            else
            {
                TempData["Toast"] = result.Message;
            }

            return Redirect(vm.Self(vm.AppendOpen(hub), anchor: "hub-" + hub));
        }

        /// <summary>
        /// Sets one hub's margin.
        /// </summary>
        /// <remarks>
        /// Nullable, and an empty box writes nothing.
        ///
        /// The box auto-submits on change, so selecting "11.5" to retype it,
        /// deleting it and tabbing away was a complete interaction: the form
        /// posted an empty value, a non-nullable decimal bound it to 0, the
        /// validator accepted 0 as a legitimate percentage, and every price on
        /// the hub dropped to bare cost under a toast that said the markup had
        /// been updated.
        ///
        /// A hub with no margin is a real state - most of them are in it - so
        /// the answer is not to forbid an empty box but to stop reading it as
        /// a nought. Clearing a margin deliberately is a different act from
        /// tabbing out of one, and this handler does neither by accident.
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> HubMarkup(
            string code, bool edit, string open, string hub, decimal? markup, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanChangeStructure)
            {
                return Forbid();
            }

            if (!markup.HasValue)
            {
                TempData["Toast"] =
                    "No markup entered, so nothing was changed. Type a percentage to price this hub.";
                return Redirect(vm.Self(edit: true, anchor: "hub-" + hub));
            }

            var result = await _tours.UpdateHubAsync(
                vm.Code, hub, new UpdateHubRequest { MarkupPercent = markup }, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? "Markup updated — prices recalculated."
                : result.Message;

            return Redirect(vm.Self(anchor: "hub-" + hub));
        }

        /// <summary>
        /// "Same as calculated SP": fills this hub's blank price boxes with the
        /// calculated figure and agrees to them.
        /// </summary>
        /// <remarks>
        /// An act, not a setting, and it takes no state at all - which is why
        /// there is no parameter beyond the hub. Nothing is remembered: the
        /// checkbox on the screen is showing whether the prices currently match
        /// the calculated ones, and the next change that blanks a cell takes
        /// the tick off by itself.
        ///
        /// It was a stored flag until migration 0047. While it was on, the
        /// price cell rendered as read-only text, so ticking a box removed the
        /// ability to price the hub and the way back was to untick something
        /// nobody would connect to the problem.
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> HubSameAsCalculated(
            string code, bool edit, string open, string hub, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanEditPublishedPrice)
            {
                return Forbid();
            }

            var result = await _tours.AdoptCalculatedForHubAsync(vm.Code, hub, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? "Prices on this hub are now the calculated ones, and agreed."
                : result.Message;

            return Redirect(vm.Self(anchor: "hub-" + hub));
        }

        [HttpPost]
        public async Task<IActionResult> HubActive(
            string code, bool edit, string open, string hub, bool active, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanChangeStructure)
            {
                return Forbid();
            }

            var result = await _tours.UpdateHubAsync(
                vm.Code, hub, new UpdateHubRequest { IsActive = active }, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? (active
                    ? "Hub restored — its departures are back on the change list."
                    : "Hub withdrawn. Tech support will be asked to remove it from the website.")
                : result.Message;

            return Redirect(vm.Self(anchor: "hub-" + hub));
        }

        [HttpPost]
        public async Task<IActionResult> AddDeparture(
            string code, bool edit, string open, string hub, string date, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanChangeStructure)
            {
                return Forbid();
            }

            /* Two different failures, and they are not the same message.

               The field is a date picker now, so the ordinary way to get here
               is an empty one - submitting before choosing. Telling that person
               to "enter the day, month and year" describes a field they are not
               looking at. The unreadable case is still reachable by a direct
               post, and still says what a readable date looks like. */
            DateTime parsed;
            if (!DepartureDateFormat.TryParse(date, out parsed))
            {
                TempData["Toast"] = string.IsNullOrWhiteSpace(date)
                    ? "Pick a departure date first."
                    : "Could not read “" + date
                        + "”. A date needs the day, month and year.";
                return Redirect(vm.Self(vm.AppendOpen(hub), anchor: "hub-" + hub));
            }

            var result = await _tours.AddDepartureAsync(vm.Code, hub, parsed, cancellationToken);

            /* It does NOT carry the hub's fares, and used to say it did.

               usp_AddDeparture creates the fare rows empty on purpose - copying
               another departure's fare would look like data and is not, because
               airfare moves by date. So the new departure reads "awaiting
               airfare" until somebody quotes one, and the message now says so.
               A message describing work that did not happen is worse than none:
               somebody told the fares were there would not go and enter them. */
            TempData["Toast"] = result.Succeeded
                ? DepartureDateFormat.Short(parsed)
                    + " added, awaiting airfare. Enter its fares before it can be priced."
                : result.Message;

            return Redirect(vm.Self(vm.AppendOpen(hub), anchor: "hub-" + hub));
        }

        [HttpPost]
        public async Task<IActionResult> DepartureActive(
            string code, bool edit, string open, string hub, DateTime date, bool active, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanChangeStructure)
            {
                return Forbid();
            }

            var result = await _tours.SetDepartureActiveAsync(
                vm.Code, hub, date, active, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? (active
                    ? DepartureDateFormat.Short(date) + " restored."
                    : DepartureDateFormat.Short(date)
                        + " withdrawn. Tech support will be asked to remove it.")
                : result.Message;

            return Redirect(vm.Self(vm.AppendOpen(hub), anchor: "hub-" + hub));
        }

        [HttpPost]
        public async Task<IActionResult> SubmitFares(
            string code, bool edit, string open, string note, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.IsAirTicketing)
            {
                return Forbid();
            }

            var result = await _tours.SubmitFaresAsync(vm.Code, note, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? "Fares submitted to the product team. They will set the published selling price."
                : result.Message;

            return Redirect(vm.Self(edit: false));
        }

        /// <summary>
        /// The product team sending the tour back to air-ticketing for the
        /// departures that have no airfare.
        /// </summary>
        /// <remarks>
        /// Deliberately not blocked when nothing is awaiting a fare: asking for a
        /// re-quote on dates that already have one is a legitimate thing to want,
        /// and the request carries the note that says why.
        /// </remarks>
        /// <summary>
        /// Sends one fare back to air-ticketing with a reason.
        /// </summary>
        /// <remarks>
        /// The fare is left alone. What this does is hold the departure out of
        /// the change set until air-ticketing answers - and because a
        /// change-set row is a departure, that holds all six of the date's
        /// prices, not only the ones this band feeds. The toast says so,
        /// because the grid row alone does not make it obvious.
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> QueryFare(
            string code, bool edit, string open, string hub, DateTime date, string band, string reason,
            CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.IsProductExecutive)
            {
                return Forbid();
            }

            var result = await _tours.RaiseFareQueryAsync(
                vm.Code,
                new RaiseFareQueryRequest
                {
                    Hub = hub,
                    Date = date,
                    Band = band,
                    Reason = reason
                },
                cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? "Sent back to air-ticketing. " + DepartureDateFormat.Short(date)
                    + " on this hub is held out of the change set until they answer."
                : result.Message;

            return Redirect(vm.Self(anchor: "hub-" + hub));
        }

        /// <summary>
        /// Air-ticketing standing by the figure they gave, which releases the
        /// departure without changing it.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResolveQuery(
            string code, bool edit, string open, int id, string note, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.IsAirTicketing)
            {
                return Forbid();
            }

            var result = await _tours.ResolveFareQueryAsync(
                vm.Code, new ResolveFareQueryRequest { Id = id, Note = note }, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? "Marked as re-quoted. That departure can go to tech support again."
                : result.Message;

            return Redirect(vm.Self());
        }

        [HttpPost]
        public async Task<IActionResult> RequestFares(
            string code, bool edit, string open, string note, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.IsProductExecutive)
            {
                return Forbid();
            }

            var result = await _tours.RequestFaresAsync(vm.Code, note, cancellationToken);

            TempData["Toast"] = result.Succeeded
                ? "Sent to air-ticketing. It is now on their fare requests list."
                : result.Message;

            return Redirect(vm.Self());
        }

        /// <summary>
        /// What the prices would become with the fares posted here. Saves nothing.
        /// </summary>
        /// <remarks>
        /// Called by the grid as fares are typed. The answer comes from the same
        /// SQL function that produces the real prices, so the preview cannot
        /// disagree with what saving would give - which is exactly what would
        /// happen if the formula were reimplemented in JavaScript.
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> Preview(
            string code, bool edit, string open, [FromBody] PreviewPricesRequest request, CancellationToken cancellationToken)
        {
            var vm = Page(code, edit, open);

            if (!_user.PermissionsFor(true).CanEditFares)
            {
                return Forbid();
            }

            var cells = await _tours.PreviewPricesAsync(vm.Code, request, cancellationToken);
            return new JsonResult(cells);
        }

        // --- Reading the posted grid ------------------------------------------

        private static List<RevisionViewModel.ActivitySession> GroupActivity(IReadOnlyList<ActivityResponse> entries)
        {
            if (entries == null)
            {
                return new List<RevisionViewModel.ActivitySession>();
            }

            return entries
                .GroupBy(a => new
                {
                    a.ActorName,
                    a.ActorRole,
                    a.Action,
                    Minute = new DateTime(
                        a.OccurredUtc.Year, a.OccurredUtc.Month, a.OccurredUtc.Day,
                        a.OccurredUtc.Hour, a.OccurredUtc.Minute, 0, DateTimeKind.Utc)
                })
                .OrderByDescending(g => g.Key.Minute)
                .Select(g => new RevisionViewModel.ActivitySession
                {
                    ActorName = g.Key.ActorName,
                    ActorRole = g.Key.ActorRole,
                    Action = g.Key.Action,
                    OccurredLocal = g.Key.Minute.ToLocalTime(),
                    Note = g.Select(x => x.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    Changes = g.Where(x => x.NewValue.HasValue).ToList()
                })
                .ToList();
        }

        /// <summary>
        /// What was typed for this occupancy, or what was there before.
        /// </summary>
        /// <remarks>
        /// Absent all the way through. A cost nobody has entered is not zero,
        /// and the three things that used to flatten it into one - this method,
        /// the request contract and the table type behind
        /// <c>usp_SaveCostBuild</c> - now all carry the difference.
        ///
        /// The old version ended <c>?? 0m</c>, on the reasoning that every box
        /// holds a figure by the time a cost build is saved. Migration 0071
        /// made that untrue: blank boxes are an ordinary state now, and a save
        /// that turned them into noughts is what priced Glimpse of Europe at
        /// 85,600 against a real 340,000.
        ///
        /// A key present with a null value means the box was on the form and
        /// left empty. A key missing means the box never came back at all, and
        /// then the stored figure stands.
        /// </remarks>
        private static decimal? Value(
            Dictionary<string, decimal?> values, string key, decimal? fallback)
        {
            decimal? found;
            if (values != null && values.TryGetValue(key, out found))
            {
                return found;
            }

            return fallback;
        }

        private static IEnumerable<FlightDetailRequest> ChangedFlightDetails(
            HubGridForm form, TourRevisionResponse current)
        {
            if (form.Flight == null)
            {
                yield break;
            }

            var live = current.Hubs
                .SelectMany(h => h.Departures.Select(d => new { h.Hub, d.Date, d.FlightDetails }))
                .ToDictionary(
                    x => x.Hub + "|" + x.Date.ToString("yyyy-MM-dd"),
                    x => x.FlightDetails);

            foreach (var entry in form.Flight)
            {
                string hub, band;
                DateTime date;
                if (!TryReadKey(entry.Key + "|x", out hub, out date, out band))
                {
                    continue;
                }

                var key = hub + "|" + date.ToString("yyyy-MM-dd");
                if (!live.ContainsKey(key))
                {
                    continue;
                }

                var typed = (entry.Value ?? string.Empty).Trim();
                var stored = (live[key] ?? string.Empty).Trim();

                if (string.Equals(typed, stored, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return new FlightDetailRequest
                {
                    Hub = hub,
                    Date = date,
                    Details = typed.Length == 0 ? null : typed
                };
            }
        }

        private static IEnumerable<FareRequest> ChangedFares(
            HubGridForm form, TourRevisionResponse current)
        {
            if (form.Fare == null)
            {
                yield break;
            }

            var live = current.Hubs
                .SelectMany(h => h.Departures.Select(d => new { h.Hub, d.Date, d.Fares }))
                .ToDictionary(x => x.Hub + "|" + x.Date.ToString("yyyy-MM-dd"), x => x.Fares);

            /*
                Hubs whose customers join at the destination. There is no flight,
                so the only fare is the tour manager's ticket, entered in the
                adult band - and the grid draws the other two as dead cells.

                A dead cell is a rendering decision, and the form is a plain POST
                of Fare[hub|date|band] keys that anybody can add to. A page
                rendered before this shipped will post them by accident. So the
                keys are dropped here as well.
            */
            var landOnly = new HashSet<string>(
                current.Hubs.Where(h => !h.HasPassengerAirfare).Select(h => h.Hub),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in form.Fare)
            {
                string hub, band;
                DateTime date;
                if (!TryReadKey(entry.Key, out hub, out date, out band))
                {
                    continue;
                }

                // Dropped rather than refused, like an unparseable amount below:
                // one stray key must not fail the save of a whole grid.
                if (landOnly.Contains(hub)
                    && !string.Equals(band, "adult", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                /*
                    An EMPTY box and an UNREADABLE one are different
                    instructions, and treating them alike is what made a fare
                    impossible to take back.

                    Empty means "remove this figure" and travels as a null.
                    Unreadable - "abt 75000", a pasted symbol - is a slip, and
                    is dropped as before rather than failing the whole grid.
                */
                var raw = (entry.Value ?? string.Empty).Trim();
                decimal parsed;
                decimal? amount;

                if (raw.Length == 0)
                {
                    amount = null;
                }
                else if (Inr.TryParse(raw, out parsed))
                {
                    amount = parsed;
                }
                else
                {
                    continue;
                }

                var dayKey = hub + "|" + date.ToString("yyyy-MM-dd");
                Dictionary<string, decimal?> fares;
                if (live.TryGetValue(dayKey, out fares) && fares.ContainsKey(band)
                    && fares[band] == amount)
                {
                    // Unchanged, including blank staying blank: nullable
                    // equality compares absence as well as value.
                    continue;
                }

                yield return new FareRequest { Hub = hub, Date = date, Band = band, Amount = amount };
            }
        }

        /// <summary>
        /// Published prices whose posted value differs from what was shown -
        /// and only those, so an override records a real decision.
        /// </summary>
        private static IEnumerable<PriceRequest> ChangedPrices(
            HubGridForm form, TourRevisionResponse current)
        {
            if (form.Price == null)
            {
                yield break;
            }

            var live = current.Prices.ToDictionary(
                p => p.Hub + "|" + p.Date.ToString("yyyy-MM-dd") + "|" + p.Occupancy,
                p => p.Published);

            foreach (var entry in form.Price)
            {
                string hub, occupancy;
                DateTime date;
                if (!TryReadKey(entry.Key, out hub, out date, out occupancy))
                {
                    continue;
                }

                /*
                    Empty, unreadable, and a figure are three things, not two.

                    Empty means "take this price back" and travels as a null:
                    the override goes, the agreement goes, and the cell reads
                    as unpriced until somebody prices it again. Unreadable is a
                    slip and is dropped, as before.
                */
                var raw = (entry.Value ?? string.Empty).Trim();
                decimal parsed;
                decimal? price;

                if (raw.Length == 0)
                {
                    price = null;
                }
                else if (Inr.TryParse(raw, out parsed))
                {
                    price = parsed;
                }
                else
                {
                    continue;
                }

                var cellKey = hub + "|" + date.ToString("yyyy-MM-dd") + "|" + occupancy;
                decimal? shown;
                if (live.TryGetValue(cellKey, out shown) && shown == price)
                {
                    // Unchanged, and that includes a blank cell left blank -
                    // which is most of the grid on a tour being priced, so it
                    // matters that this does not send them all as clears.
                    continue;
                }

                yield return new PriceRequest
                {
                    Hub = hub,
                    Date = date,
                    Occupancy = occupancy,
                    Price = price
                };
            }
        }

        /// <summary>
        /// Price boxes posted with nothing in them.
        /// </summary>
        /// <remarks>
        /// The same count the script makes before it asks the question, taken
        /// the same way - off the form, not the database - so the two paths
        /// cannot quote different numbers for the same save.
        /// </remarks>
        private static int BlankPrices(HubGridForm form)
        {
            if (form.Price == null)
            {
                return 0;
            }

            var blank = 0;

            foreach (var entry in form.Price)
            {
                if (string.IsNullOrWhiteSpace(entry.Value))
                {
                    blank++;
                }
            }

            return blank;
        }

        /// <summary>
        /// Cells somebody filled in at the figure the system had already worked
        /// out, and which were waiting to be confirmed.
        /// </summary>
        /// <remarks>
        /// These are deliberately NOT saved as prices. An override means "hold
        /// this figure whatever the costs do next", and agreeing with the
        /// calculation is the opposite of that - it should keep moving with the
        /// costs. What changed is that somebody looked at it, so that is what
        /// gets recorded.
        ///
        /// Anything already in <paramref name="saved"/> is left out only to keep
        /// the count honest; confirming the same cell twice is harmless, because
        /// saving a price confirms it too.
        /// </remarks>
        private static IEnumerable<PriceCellRequest> AgreedCells(
            HubGridForm form, TourRevisionResponse current, IReadOnlyList<PriceRequest> saved)
        {
            if (form.Price == null)
            {
                yield break;
            }

            var unconfirmed = new HashSet<string>(
                current.Prices.Where(p => !p.IsConfirmed).Select(CellKey),
                StringComparer.OrdinalIgnoreCase);

            var alreadySaved = new HashSet<string>(
                saved.Select(p => p.Hub + "|" + p.Date.ToString("yyyy-MM-dd") + "|" + p.Occupancy),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in form.Price)
            {
                string hub, occupancy;
                DateTime date;
                if (!TryReadKey(entry.Key, out hub, out date, out occupancy))
                {
                    continue;
                }

                // An empty box is somebody who has not decided yet. It is not an
                // agreement, and the cell stays open.
                decimal typed;
                if (!Inr.TryParse(entry.Value, out typed))
                {
                    continue;
                }

                var cellKey = hub + "|" + date.ToString("yyyy-MM-dd") + "|" + occupancy;
                if (!unconfirmed.Contains(cellKey) || alreadySaved.Contains(cellKey))
                {
                    continue;
                }

                yield return new PriceCellRequest { Hub = hub, Date = date, Occupancy = occupancy };
            }
        }

        /// <summary>
        /// The cells whose "conditions apply" box is ticked, which is the whole
        /// answer for the hub this form covers - an unticked checkbox posts
        /// nothing, so anything absent is a star that was cleared.
        /// </summary>
        private static IEnumerable<PriceCellRequest> StarredCells(HubGridForm form)
        {
            if (form.Star == null)
            {
                yield break;
            }

            foreach (var entry in form.Star)
            {
                string hub, occupancy;
                DateTime date;
                if (!TryReadKey(entry.Key, out hub, out date, out occupancy))
                {
                    continue;
                }

                yield return new PriceCellRequest { Hub = hub, Date = date, Occupancy = occupancy };
            }
        }

        private static string CellKey(PriceCellResponse cell)
        {
            return cell.Hub + "|" + cell.Date.ToString("yyyy-MM-dd") + "|" + cell.Occupancy;
        }

        /// <summary>
        /// The hub this grid belongs to, so the redirect can anchor back to it.
        /// </summary>
        /// <remarks>
        /// The first key posted, which is the first grid on the page, because
        /// the hub list covers every open hub and cannot say where somebody was
        /// working.
        /// </remarks>
        private static string HubAnchor(HubGridForm form)
        {
            var key = form.Fare != null && form.Fare.Count > 0
                ? form.Fare.Keys.First()
                : (form.Price != null && form.Price.Count > 0 ? form.Price.Keys.First() : null);

            if (key == null)
            {
                return null;
            }

            var parts = key.Split('|');
            return parts.Length > 0 ? "hub-" + parts[0] : null;
        }

        /// <summary>Reads a "hub|yyyy-MM-dd|part" input name.</summary>
        private static bool TryReadKey(string key, out string hub, out DateTime date, out string part)
        {
            hub = null;
            part = null;
            date = default(DateTime);

            var parts = (key ?? string.Empty).Split('|');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!DateTime.TryParseExact(
                    parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
            {
                return false;
            }

            hub = parts[0];
            part = parts[2];
            return true;
        }
    }
}
