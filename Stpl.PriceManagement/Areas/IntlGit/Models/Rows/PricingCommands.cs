using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.IntlGit.Models.Rows
{
    public sealed class SaveCostBuildCommand
    {
        public string TourCode { get; set; }

        /// <summary>Null when nobody has entered one. Not zero.</summary>
        public decimal? FxRate { get; set; }

        /// <summary>Null when nobody has set one. Not zero.</summary>
        public decimal? StrikePct { get; set; }
        public int PaxSlab { get; set; }

        /// <summary>Null when nobody has entered one. Not zero.</summary>
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }
        public IReadOnlyList<OccupancyCostEntry> Costs { get; set; }
    }

    public sealed class OccupancyCostEntry
    {
        public string OccupancyCode { get; set; }

        /// <summary>Null when nobody has entered this cost. Not zero.</summary>
        public decimal? LandCostFx { get; set; }

        /// <summary>Null when nobody has entered this cost. Not zero.</summary>
        public decimal? PerPersonInr { get; set; }
    }

    public sealed class PriceEntry
    {
        public string HubCode { get; set; }
        public DateTime DepartureDate { get; set; }
        public string OccupancyCode { get; set; }

        /// <summary>Null takes the price back. Not zero, and not "unchanged".</summary>
        public decimal? Price { get; set; }
    }

    /// <summary>One price cell, addressed the way the screen knows it.</summary>
    public sealed class PriceCellRef
    {
        public string HubCode { get; set; }
        public DateTime DepartureDate { get; set; }
        public string OccupancyCode { get; set; }
    }

    public sealed class FareEntry
    {
        public string HubCode { get; set; }
        public DateTime DepartureDate { get; set; }
        public string FareBandCode { get; set; }

        /// <summary>Null takes the fare back — "awaiting airfare" again.</summary>
        public decimal? Amount { get; set; }
    }
}
