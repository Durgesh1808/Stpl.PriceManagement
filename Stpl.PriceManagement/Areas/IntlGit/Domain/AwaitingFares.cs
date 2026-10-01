using System;
using System.Collections.Generic;
using System.Linq;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// How many departures still have no airfare.
    /// </summary>
    /// <remarks>
    /// Two screens need this answer and they must not disagree: the revision
    /// page uses it to offer "send back to air-ticketing", and the review page
    /// uses it to refuse the change set. One of those is advice and the other
    /// is a block, so they have to be counting the same thing.
    /// </remarks>
    public static class AwaitingFares
    {
        /// <summary>
        /// Counts departures with no airfare, ignoring any that are already off
        /// the website - there is no point chasing a fare for a date nobody can
        /// book. A price cell reports HasFare, and a departure is one whatever
        /// its six occupancy cells say, so distinct hub-and-date pairs are the
        /// answer.
        /// </summary>
        public static int Count(TourRevisionResponse revision)
        {
            if (revision == null || revision.Prices == null || revision.Hubs == null)
            {
                return 0;
            }

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

                    /*
                        And a date that has already gone.

                        Same reasoning as a withdrawn one and the same
                        exclusion: there is no point chasing a fare for a
                        departure nobody can book. Left in, it would count as
                        owed for ever - and because the review screen refuses
                        to submit while anything is owed, one passed date with
                        no fare blocks that tour permanently.

                        Read off the date at the moment the page is drawn, so
                        there is no job to run and nothing stored that can fall
                        out of step with the calendar.
                    */
                    if (departure.Date.Date < DateTime.Now.Date)
                    {
                        withdrawn.Add(Key(hub.Hub, departure.Date));
                    }
                }
            }

            return revision.Prices
                .Where(p => !p.HasFare)
                .Select(p => Key(p.Hub, p.Date))
                .Where(k => !withdrawn.Contains(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private static string Key(string hub, DateTime date)
        {
            return hub + "|" + date.ToString("yyyy-MM-dd");
        }
    }
}
