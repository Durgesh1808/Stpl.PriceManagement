using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// The block of HTML tech support pastes onto the website for one
    /// departure date.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS AT ALL
    /// Tech support were reading figures off a screen and retyping them into a
    /// table by hand, once per departure date, for every hub. The prices are
    /// already frozen on the change set; producing the markup from them removes
    /// a transcription step that nobody can check afterwards.
    ///
    /// THE MARKUP IS NOT OURS
    /// It is the website's existing table, reproduced exactly - the classes,
    /// the inline styles, the rowspan on Infant, the empty spacer row. It is
    /// pasted into a page built by somebody else, so it has to match what is
    /// already there rather than what we would write today.
    ///
    /// COLUMN ORDER IS THE WEBSITE'S, NOT OURS
    /// Our own grids order occupancies by Occupancy.SortOrder - twin, triple,
    /// single, cwb, cnb, infant. The website's table puts SINGLE fifth, after
    /// the two child columns. The order below is therefore hard-coded against
    /// occupancy CODES, deliberately, and does not follow SortOrder. Sorting
    /// this by SortOrder would silently put the single-occupancy price in the
    /// child-with-bed column.
    ///
    /// EVERY OCCUPANCY IS RENDERED, changed or not. The block replaces a whole
    /// table on the site, so a figure left out is a figure erased.
    /// </remarks>
    public static class PriceTableHtml
    {
        /// <summary>
        /// The website's column order, by occupancy code, with the heading each
        /// one carries. Not SortOrder - see the note above.
        /// </summary>
        private static readonly string[][] Columns =
        {
            new[] { "twin",   "TWN" },
            new[] { "triple", "TRPL" },
            new[] { "cwb",    "Child With Bed" },
            new[] { "cnb",    "Child No Bed" },
            new[] { "single", "Per Adult in Single" },
            new[] { "infant", "Infant" }
        };

        /// <summary>
        /// Infant carries one figure spanning both rows rather than a
        /// struck-through price above an offer price, which is how the website
        /// has always shown it.
        /// </summary>
        private const string SpanningColumn = "infant";

        private const string CellStyle = "text-align: center; white-space: nowrap;";

        /// <summary>
        /// One departure's table, with a heading naming the hub and date above
        /// it so it cannot be pasted under the wrong one.
        /// </summary>
        /// <param name="hubName">As the tour knows it, e.g. "Bengaluru".</param>
        /// <param name="date">The departure date this table prices.</param>
        /// <param name="cells">
        /// Every occupancy of that departure. Anything the website has a column
        /// for and this does not is rendered as an em dash rather than skipped,
        /// because a missing cell would shift every figure after it one column
        /// to the left.
        /// </param>
        public static string Build(
            string hubName, DateTime date, IEnumerable<PriceTableCell> cells)
        {
            var byCode = new Dictionary<string, PriceTableCell>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in cells ?? new PriceTableCell[0])
            {
                if (cell != null && !string.IsNullOrEmpty(cell.Occupancy))
                {
                    byCode[cell.Occupancy] = cell;
                }
            }

            var html = new StringBuilder();

            html.Append("<h3>").Append(Escape(hubName)).Append(" &ndash; ")
                .Append(date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))
                .Append("</h3>\n");

            html.Append("<div class=\"tablewrap\">\n");
            html.Append("<table width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" ")
                .Append("class=\"table-bordered tablewidth\">\n<tbody>\n");

            // Heading row. The first column heads the two row labels below it.
            html.Append("<tr>\n<th style=\"text-align: center;\">Offer Price</th>\n");
            foreach (var column in Columns)
            {
                html.Append("<th style=\"text-align: center;\">")
                    .Append(column[1]).Append("</th>\n");
            }
            html.Append("</tr>\n");

            html.Append("<!-- Main Offer Price -->\n");

            // Row one: the struck-through figure. Infant spans into row two.
            html.Append("<tr>\n<th class=\"th2\" style=\"text-align: center;\">Tour in INR</th>\n");
            foreach (var column in Columns)
            {
                var cell = Lookup(byCode, column[0]);

                if (string.Equals(column[0], SpanningColumn, StringComparison.OrdinalIgnoreCase))
                {
                    html.Append("<td rowspan=\"2\" style=\"").Append(CellStyle).Append("\">")
                        .Append(Money(cell == null ? (decimal?)null : cell.Published))
                        .Append("</td>\n");
                }
                else
                {
                    /*
                        No crossed-out price means no <s> at all, not a struck
                        em dash.

                        This table is pasted onto the live website, so whatever
                        is here is what a customer reads. A tour with no
                        strike-through percentage has no MRP - since 0074 that
                        is every tour until somebody sets one - and the honest
                        rendering of "there is no was-price" is an empty cell,
                        not a line drawn through a placeholder.
                    */
                    var strike = cell == null ? (decimal?)null : cell.StrikeThrough;

                    html.Append("<td style=\"").Append(CellStyle).Append("\">");
                    if (strike.HasValue)
                    {
                        html.Append("<s>").Append(Money(strike)).Append("</s>");
                    }
                    html.Append("</td>\n");
                }
            }
            html.Append("</tr>\n");

            // Row two: the price actually being offered. One cell short,
            // because Infant is spanning down from above.
            html.Append("<tr>\n<th class=\"th2\" style=\"text-align: center;\">Offer Price</th>\n");
            foreach (var column in Columns)
            {
                if (string.Equals(column[0], SpanningColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var cell = Lookup(byCode, column[0]);

                html.Append("<td style=\"").Append(CellStyle).Append("\">")
                    .Append(Money(cell == null ? (decimal?)null : cell.Published))
                    .Append("</td>\n");

                /*
                    SEAM: "conditions apply".

                    cell.HasConditions is carried all the way here and is
                    deliberately not rendered yet - where the asterisk goes, and
                    whether it needs a footnote under the table, is the one
                    question still with the user's team. When it is answered
                    this is the only place that changes.
                */
            }
            html.Append("</tr>\n");

            // The spacer row the website's markup ends with. One cell wider
            // than the old six-column version, or it under-runs the table.
            html.Append("<tr>\n<td colspan=\"").Append(Columns.Length + 1)
                .Append("\"></td>\n</tr>\n");

            html.Append("</tbody>\n</table>\n</div>");

            return html.ToString();
        }

        private static PriceTableCell Lookup(
            Dictionary<string, PriceTableCell> byCode, string occupancy)
        {
            PriceTableCell found;
            return byCode.TryGetValue(occupancy, out found) ? found : null;
        }

        /// <summary>
        /// "INR 438700" - no thousands separators, because that is how the
        /// website's own table writes them.
        /// </summary>
        private static string Money(decimal? amount)
        {
            return amount.HasValue
                ? "INR " + decimal.Round(amount.Value, 0).ToString("0", CultureInfo.InvariantCulture)
                : "&mdash;";
        }

        /// <summary>
        /// Hub names come from a database somebody types into, and this output
        /// is pasted into a live web page.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }

    /// <summary>One occupancy's two figures, as frozen on the change set.</summary>
    public sealed class PriceTableCell
    {
        public string Occupancy { get; set; }
        public decimal Published { get; set; }
        /// <summary>Null where there is no crossed-out price. Not zero.</summary>
        public decimal? StrikeThrough { get; set; }

        /// <summary>
        /// Carried for the seam in <see cref="PriceTableHtml"/>. Not rendered
        /// until the user's team says where the asterisk belongs.
        /// </summary>
        public bool HasConditions { get; set; }
    }
}
