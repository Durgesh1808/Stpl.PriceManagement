using System;
using System.Collections.Generic;
using System.Linq;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>
    /// The change list this revision would hand to tech support, and the point
    /// at which it is handed over.
    /// </summary>
    public sealed class ReviewViewModel
    {
        public string Code { get; set; }

        /// <summary>Whether the person looking is on the product team (only they submit).</summary>
        public bool IsProductExecutive { get; set; }

        public TourRevisionResponse Revision { get; set; }
        public PendingChangesResponse Pending { get; set; }

        /// <summary>The newest set on this tour that has NOT been applied, if any.</summary>
        public TourChangeSetStateResponse InFlight { get; set; }

        /// <summary>How many change sets on this tour are still with tech support.</summary>
        public int OutstandingSets { get; set; }

        public List<(string Label, string Href)> Crumbs { get; set; }

        /// <summary>Departures on an active hub with no airfare entered.</summary>
        public int AwaitingFares { get; set; }

        /// <summary>Prices whose figure has moved and which nobody has agreed to since.</summary>
        public int Unconfirmed { get; set; }

        /// <summary>How many departures those are spread across.</summary>
        public int UnconfirmedDepartures { get; set; }

        /// <summary>
        /// Fares sent back to air-ticketing. Their departures are deliberately
        /// absent from <see cref="Pending"/>, so this screen has to name them.
        /// </summary>
        public IReadOnlyList<FareQueryResponse> Held
        {
            get
            {
                return Pending == null || Pending.Held == null
                    ? new List<FareQueryResponse>()
                    : Pending.Held;
            }
        }

        /// <summary>
        /// Departures left out because at least one of their prices has not
        /// been agreed. A separate list from <see cref="Held"/> on purpose:
        /// same consequence, different remedy.
        /// </summary>
        public IReadOnlyList<UnpricedDepartureResponse> Unpriced
        {
            get
            {
                return Pending == null || Pending.Unpriced == null
                    ? new List<UnpricedDepartureResponse>()
                    : Pending.Unpriced;
            }
        }

        /// <summary>
        /// How many prices one departure carries, so the banner can say "3 of 6"
        /// rather than a bare count. Read off the data, not written as a six.
        /// </summary>
        public int OccupancyCount
        {
            get
            {
                var first = Pending == null || Pending.Hubs == null
                    ? null
                    : Pending.Hubs
                        .SelectMany(h => h.Departures ?? new List<PendingDepartureResponse>())
                        .FirstOrDefault();

                return first != null && first.Cells != null && first.Cells.Count > 0
                    ? first.Cells.Count
                    : 6;
            }
        }

        /// <summary>How many departures are held (one date can have more than one query).</summary>
        public int HeldDepartures
        {
            get
            {
                return Held
                    .Select(q => q.Hub + "|" + q.Date.ToString("yyyy-MM-dd"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }
        }

        /// <summary>
        /// Only the product team submits, and only when there is something to
        /// submit. Missing fares no longer block the set: a departure with no
        /// airfare has no price, and usp_GetPendingChanges already leaves it out.
        /// </summary>
        public bool CanSubmit
        {
            get
            {
                return IsProductExecutive
                    && Pending != null
                    && Pending.TotalChanges > 0;
            }
        }

        /// <summary>
        /// Whether there is a revision under way at all - which counts what is
        /// held back too, or it goes quiet exactly when it is most needed.
        /// </summary>
        public bool HasWork
        {
            get
            {
                return Pending != null
                    && (Pending.TotalChanges > 0
                        || Unpriced.Count > 0
                        || Held.Count > 0
                        || AwaitingFares > 0);
            }
        }

        /// <summary>
        /// Set when some departures have no airfare. A notice, not a block:
        /// those departures are left out of this change set.
        /// </summary>
        public bool BlockedByMissingFares
        {
            get
            {
                return IsProductExecutive
                    && HasWork
                    && AwaitingFares > 0;
            }
        }

        /// <summary>
        /// Always false now ("unconfirmed" and "blank" became the same fact),
        /// kept only so the view has one place to lose.
        /// </summary>
        public bool BlockedByUnconfirmed
        {
            get { return false; }
        }
    }
}
