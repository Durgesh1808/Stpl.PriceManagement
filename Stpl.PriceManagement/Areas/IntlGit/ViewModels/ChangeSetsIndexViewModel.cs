using System;
using System.Collections.Generic;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>The work list: change sets handed to tech support.</summary>
    public sealed class ChangeSetsIndexViewModel
    {
        /// <summary>
        /// Which sets to show: "open", "applied", or "all". Null means nobody
        /// has chosen, and <see cref="DefaultShow"/> decides.
        /// </summary>
        public string Show { get; set; }

        /// <summary>Free text over the reference, tour code and tour name.</summary>
        public string Search { get; set; }

        public string SortBy { get; set; } = "submitted";

        public bool Ascending { get; set; } = true;

        public bool IsTechSupport { get; set; }

        /// <summary>
        /// What "no choice made" means: tech support land on every request,
        /// the product team on the ones that have not landed yet.
        /// </summary>
        public string DefaultShow
        {
            get { return IsTechSupport ? "all" : "open"; }
        }

        /// <summary>The filter actually in force. Read this, never <see cref="Show"/>.</summary>
        public string EffectiveShow
        {
            get { return string.IsNullOrWhiteSpace(Show) ? DefaultShow : Show; }
        }

        /// <summary>Whether anything is narrowed from what they would see on arrival.</summary>
        public bool HasFilters
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Search)
                    || (!string.IsNullOrWhiteSpace(Show) && Show != DefaultShow);
            }
        }

        /// <summary>How many the search is hiding, so the count is never a lie.</summary>
        public int TotalBeforeSearch { get; set; }

        public List<ChangeSetSummaryResponse> Sets { get; set; } = new List<ChangeSetSummaryResponse>();

        public int OpenCount { get; set; }

        public ChangeSetSummaryResponse Nearest { get; set; }

        public List<(string Label, string Href)> Crumbs { get; set; }

        public DateTime Today
        {
            get { return DateTime.Now; }
        }

        public ChangeSetState StateOf(ChangeSetSummaryResponse set)
        {
            return ChangeSetStates.Evaluate(set.TotalRows, set.CompletedRows, set.IsApplied);
        }

        /// <summary>This page's address with some of its state changed and the rest kept.</summary>
        private string Query(string show = null, string sortBy = null, bool? ascending = null)
        {
            var parts = new List<string>();

            // Only the default is left out of the address, and the default is role-dependent.
            var wantedShow = show ?? EffectiveShow;
            if (!string.IsNullOrWhiteSpace(wantedShow) && wantedShow != DefaultShow)
            {
                parts.Add("show=" + Uri.EscapeDataString(wantedShow));
            }

            if (!string.IsNullOrWhiteSpace(Search))
            {
                parts.Add("search=" + Uri.EscapeDataString(Search));
            }

            var wantedSort = sortBy ?? SortBy;
            if (!string.IsNullOrWhiteSpace(wantedSort) && wantedSort != "submitted")
            {
                parts.Add("sortBy=" + Uri.EscapeDataString(wantedSort));
            }

            if (!(ascending ?? Ascending))
            {
                parts.Add("ascending=false");
            }

            return "/IntlGit/ChangeSets" + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
        }

        /// <summary>The address of this page sorted by a different column (same column reverses).</summary>
        public string SortUrl(string column)
        {
            return Query(sortBy: column, ascending: SortBy == column ? !Ascending : true);
        }

        /// <summary>The sprite symbol marking the sorted column, or null for the rest.</summary>
        public string SortIcon(string column)
        {
            if (SortBy != column)
            {
                return null;
            }

            return Ascending ? "arrow-up" : "arrow-down";
        }
    }
}
