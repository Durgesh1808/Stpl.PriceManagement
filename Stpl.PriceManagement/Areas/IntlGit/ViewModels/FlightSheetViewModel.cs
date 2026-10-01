using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>
    /// The airfare summary sheet: what air-ticketing has handed over on one
    /// tour, one column per submission. Read-only.
    /// </summary>
    public sealed class FlightSheetViewModel
    {
        public string Code { get; set; }

        /// <summary>Only departures whose fares have moved. Off by default.</summary>
        public bool ChangedOnly { get; set; }

        public FlightSheetResponse Sheet { get; set; }

        /// <summary>How many departures the filter is holding back, so it can say so.</summary>
        public int HiddenByFilter
        {
            get
            {
                if (!ChangedOnly || Sheet == null)
                {
                    return 0;
                }

                var hidden = 0;
                foreach (var hub in Sheet.Hubs)
                {
                    foreach (var departure in hub.Departures)
                    {
                        if (!departure.HasMoved)
                        {
                            hidden++;
                        }
                    }
                }

                return hidden;
            }
        }
    }
}
