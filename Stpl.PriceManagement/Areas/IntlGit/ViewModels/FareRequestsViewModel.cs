using System;
using System.Collections.Generic;
using System.Linq;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>Air-ticketing's queue: every tour the product team is waiting on.</summary>
    public sealed class FareRequestsViewModel
    {
        public IReadOnlyList<FareRequestResponse> Requests { get; set; }

        public List<(string Label, string Href)> Crumbs { get; set; }

        public string Subtitle
        {
            get
            {
                if (Requests == null || Requests.Count == 0)
                {
                    return "Nothing waiting — the product team has not asked for any fares.";
                }

                var departures = Requests.Sum(r => r.AwaitingFares);
                return Requests.Count + (Requests.Count == 1 ? " tour · " : " tours · ")
                    + departures + (departures == 1 ? " departure" : " departures")
                    + " with no fare yet";
            }
        }

        /// <summary>
        /// How close the earliest unpriced departure is. A fare wanted for next
        /// month is not the same job as one wanted for next week.
        /// </summary>
        public static string Urgency(DateTime? earliest)
        {
            if (!earliest.HasValue)
            {
                return null;
            }

            var days = (earliest.Value.Date - DateTime.Now.Date).Days;

            if (days < 0)
            {
                return "departed";
            }

            if (days == 0)
            {
                return "departs today";
            }

            if (days == 1)
            {
                return "departs tomorrow";
            }

            return "departs in " + days + " days";
        }
    }
}
