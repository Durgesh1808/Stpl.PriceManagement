using System;
using System.Collections.Generic;
using System.Linq;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// Which prices are waiting on somebody's agreement.
    /// </summary>
    /// <remarks>
    /// Written the same way as <see cref="AwaitingFares"/>, and for the same
    /// reason: three screens need this answer and must not disagree. The grid
    /// uses it to empty a cell and show the calculated figure as a ghost, the
    /// review page uses it to refuse the change set, and the departure and hub
    /// headers use it to offer "confirm these". Advice and a block have to be
    /// counting the same thing.
    ///
    /// Everything here reads the revision that is already on screen. The gate
    /// is enforced on the server in Review.cshtml.cs, which re-reads the
    /// revision before submitting, because a disabled button is a courtesy and
    /// a fare can move while this screen is open.
    /// </remarks>
    public static class UnconfirmedPrices
    {
        /// <summary>
        /// Cells nobody has agreed to, ignoring anything that could not go to
        /// the website anyway: a withdrawn hub or date, and a departure with no
        /// airfare - which is a different problem, reported separately, and
        /// would otherwise be counted twice.
        /// </summary>
        public static IReadOnlyList<PriceCellResponse> Cells(TourRevisionResponse revision)
        {
            if (revision == null || revision.Prices == null || revision.Hubs == null)
            {
                return new List<PriceCellResponse>();
            }

            var withdrawn = Withdrawn(revision);

            return revision.Prices
                .Where(p => !p.IsConfirmed && p.HasFare)
                .Where(p => !withdrawn.Contains(Key(p.Hub, p.Date)))
                .ToList();
        }

        public static int Count(TourRevisionResponse revision)
        {
            return Cells(revision).Count;
        }

        /// <summary>How many departures those cells are spread across.</summary>
        public static int DepartureCount(TourRevisionResponse revision)
        {
            return Cells(revision)
                .Select(p => Key(p.Hub, p.Date))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        /// <summary>
        /// The message shown when submission is refused. One sentence, saying
        /// what is wrong and what to do about it.
        /// </summary>
        public static string BlockedMessage(int cells, int departures)
        {
            return cells
                + (cells == 1 ? " price has" : " prices have")
                + " changed and not been confirmed, across "
                + departures + (departures == 1 ? " departure" : " departures")
                + ". Review them in the grid and confirm before sending this to"
                + " tech support.";
        }

        private static HashSet<string> Withdrawn(TourRevisionResponse revision)
        {
            var withdrawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hub in revision.Hubs)
            {
                if (hub.Departures == null)
                {
                    continue;
                }

                foreach (var departure in hub.Departures)
                {
                    if (!hub.IsActive || !departure.IsActive)
                    {
                        withdrawn.Add(Key(hub.Hub, departure.Date));
                    }
                }
            }

            return withdrawn;
        }

        private static string Key(string hub, DateTime date)
        {
            return hub + "|" + date.ToString("yyyy-MM-dd");
        }
    }
}
