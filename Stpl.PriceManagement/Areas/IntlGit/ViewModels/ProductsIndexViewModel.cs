using System;
using System.Collections.Generic;
using Stpl.PriceManagement.Areas.IntlGit.Domain;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>The products list: filters, sort, page, and the rows to draw.</summary>
    public sealed class ProductsIndexViewModel
    {
        public string Search { get; set; }
        public string Region { get; set; }
        public string Status { get; set; }
        public string Sort { get; set; }
        public bool Ascending { get; set; }

        /// <summary>Which page of the list, as ?pg=2.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Rows per page, as ?size=50 (25, 50 or 100).</summary>
        public int Size { get; set; } = 25;

        public List<TourRow> Tours { get; set; } = new List<TourRow>();

        /// <summary>Every region on the master, for the filter - not just the shown ones.</summary>
        public List<string> Regions { get; set; } = new List<string>();

        public int TotalCount { get; set; }
        public int PageCount { get; set; }
        public int FirstRow { get; set; }
        public int LastRow { get; set; }

        public string Subtitle { get; set; }

        public bool HasFilters
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Search)
                    || !string.IsNullOrWhiteSpace(Region)
                    || !string.IsNullOrWhiteSpace(Status);
            }
        }

        /// <summary>The statuses worth offering, in the order they occur in a week.</summary>
        public static IReadOnlyList<RevisionStatus> StatusOptions
        {
            get
            {
                return new[]
                {
                    RevisionStatus.Live,
                    RevisionStatus.Held,
                    RevisionStatus.DraftOpen,
                    RevisionStatus.WithAirTicketing,
                    RevisionStatus.FaresReceived,
                    RevisionStatus.NotStarted,
                    RevisionStatus.InProgress,
                    RevisionStatus.UpdatedOnSite
                };
            }
        }

        public List<(string Label, string Href)> Crumbs { get; } = new List<(string, string)>
        {
            ("Pricing", null),
            ("Products", null)
        };

        /// <summary>
        /// How many tours this region has in the WHOLE filtered list, not on
        /// this page - a region can straddle a page break.
        /// </summary>
        public int RegionCount(string region)
        {
            return RegionTotals.ContainsKey(region) ? RegionTotals[region] : 0;
        }

        public Dictionary<string, int> RegionTotals { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// This page's address with some of its state changed and the rest kept.
        /// Every link on the screen goes through here, so no filter can be lost.
        /// </summary>
        private string Query(
            string region = null, string status = null, string sort = null,
            bool? ascending = null, int? page = null, int? size = null)
        {
            var parts = new List<string>();

            Add(parts, "search", Search);
            Add(parts, "region", region ?? Region);
            Add(parts, "status", status ?? Status);
            Add(parts, "sort", sort ?? Sort);

            if (ascending ?? Ascending) { parts.Add("ascending=true"); }

            var wantedPage = page ?? PageNumber;
            if (wantedPage > 1) { parts.Add("pg=" + wantedPage); }

            var wantedSize = size ?? Size;
            if (wantedSize != 25) { parts.Add("size=" + wantedSize); }

            return "/IntlGit/Products" + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
        }

        private static void Add(List<string> parts, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(key + "=" + Uri.EscapeDataString(value));
            }
        }

        /// <summary>This page sorted by a column. Sorting always returns to page one.</summary>
        public string SortUrl(string column)
        {
            return Query(sort: column, ascending: Sort == column ? !Ascending : true, page: 1);
        }

        /// <summary>The sprite symbol for a sorted column, or null when it is not sorted.</summary>
        public string SortIcon(string column)
        {
            if (Sort != column)
            {
                return null;
            }

            return Ascending ? "arrow-up" : "arrow-down";
        }

        public string PageUrl(int page)
        {
            return Query(page: page);
        }

        /// <summary>The page numbers to draw: a window around the current one, with 0 for a gap.</summary>
        public List<int> PageNumbers()
        {
            var numbers = new List<int>();
            if (PageCount <= 7)
            {
                for (var i = 1; i <= PageCount; i++) { numbers.Add(i); }
                return numbers;
            }

            var from = Math.Max(2, PageNumber - 1);
            var to = Math.Min(PageCount - 1, PageNumber + 1);

            numbers.Add(1);
            if (from > 2) { numbers.Add(0); }
            for (var i = from; i <= to; i++) { numbers.Add(i); }
            if (to < PageCount - 1) { numbers.Add(0); }
            numbers.Add(PageCount);

            return numbers;
        }

        public sealed class TourRow
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Region { get; set; }
            public string Duration { get; set; }
            public int HubCount { get; set; }
            public int DepartureCount { get; set; }
            public RevisionStatus Status { get; set; }
            public string StatusDetail { get; set; }
            public string UpdatedLabel { get; set; }

            /// <summary>The raw time behind UpdatedLabel, so the column can sort.</summary>
            public DateTime ModifiedUtc { get; set; }
        }
    }
}
