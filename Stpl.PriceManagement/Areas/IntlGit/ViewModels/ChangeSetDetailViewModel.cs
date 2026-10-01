using System;
using System.Collections.Generic;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>
    /// One change set: every price to put on the website, and every listing to
    /// take off it, with a tick against each departure.
    /// </summary>
    public sealed class ChangeSetDetailViewModel
    {
        public string Reference { get; set; }

        public bool IsTechSupport { get; set; }

        public ChangeSetDetailResponse ChangeSet { get; set; }

        public List<(string Label, string Href)> Crumbs { get; set; }

        /// <summary>Only tech support ticks rows; everyone else is reading.</summary>
        public bool CanTick
        {
            get
            {
                return IsTechSupport
                    && ChangeSet != null
                    && !ChangeSet.Summary.IsApplied;
            }
        }

        public ChangeSetState State
        {
            get
            {
                var summary = ChangeSet.Summary;
                return ChangeSetStates.Evaluate(
                    summary.TotalRows, summary.CompletedRows, summary.IsApplied);
            }
        }

        public DateTime Today
        {
            get { return DateTime.Now; }
        }

        /// <summary>Occupancy columns, taken from the first row that has any.</summary>
        public List<ChangeSetCellResponse> Columns { get; set; } = new List<ChangeSetCellResponse>();

        /// <summary>A row key ("blr|2026-10-13") as an HTML id and URL fragment.</summary>
        public static string RowAnchor(string rowKey)
        {
            return "row-" + (rowKey ?? string.Empty).Replace("|", "-");
        }
    }
}
