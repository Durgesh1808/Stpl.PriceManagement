using System;
using System.Globalization;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// The identity of one unit of work in a change set.
    /// </summary>
    /// <remarks>
    /// A row key has to survive being written to one database, read by a
    /// different service and posted back — so it is a plain string with a
    /// documented shape rather than a set of ids that would only mean something
    /// inside one service.
    ///
    ///   price row:       "blr|2026-10-13"
    ///   departure gone:  "rm-dep|blr|2026-11-24"
    ///   whole hub gone:  "rm-hub|vtz"
    /// </remarks>
    public static class RowKeys
    {
        public const string PriceKind = "price";
        public const string RemovalKind = "removal";

        private const string DateFormat = "yyyy-MM-dd";

        public static string Price(string hubCode, DateTime date)
        {
            return hubCode + "|" + date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static string RemovedDeparture(string hubCode, DateTime date)
        {
            return "rm-dep|" + hubCode + "|" + date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static string RemovedHub(string hubCode)
        {
            return "rm-hub|" + hubCode;
        }

        public static bool IsRemoval(string rowKey)
        {
            return rowKey != null && rowKey.StartsWith("rm-", StringComparison.Ordinal);
        }
    }

    /// <summary>Where a change set has got to.</summary>
    public enum ChangeSetState
    {
        /// <summary>With tech support, nothing applied yet.</summary>
        NotStarted,

        /// <summary>With tech support, partly applied.</summary>
        InProgress,

        /// <summary>Every row ticked, waiting for the tour to be promoted.</summary>
        Complete,

        /// <summary>Applied in full; the tour is on its new version.</summary>
        Applied
    }

    public static class ChangeSetStates
    {
        public static ChangeSetState Evaluate(int totalRows, int completedRows, bool isApplied)
        {
            if (isApplied)
            {
                return ChangeSetState.Applied;
            }

            if (totalRows > 0 && completedRows >= totalRows)
            {
                return ChangeSetState.Complete;
            }

            return completedRows > 0 ? ChangeSetState.InProgress : ChangeSetState.NotStarted;
        }

        public static string Label(ChangeSetState state)
        {
            switch (state)
            {
                case ChangeSetState.InProgress:
                    return "In progress";
                case ChangeSetState.Complete:
                    return "Complete";
                case ChangeSetState.Applied:
                    return "Updated on site";
                default:
                    return "Not started";
            }
        }

        /// <summary>Chip colours, matching the palette used across the app.</summary>
        public static ChipStyle Style(ChangeSetState state)
        {
            switch (state)
            {
                case ChangeSetState.InProgress:
                    return new ChipStyle("#EFF6FC", "#14528A", "#9CC4E4");
                case ChangeSetState.Complete:
                    return new ChipStyle("#EAF6F0", "#14623D", "#AEDCC4");
                case ChangeSetState.Applied:
                    return new ChipStyle("#EAF6F0", "#14623D", "#AEDCC4");
                default:
                    return new ChipStyle("#FEF1EA", "#A64A17", "#F3C4A6");
            }
        }
    }

    public sealed class ChipStyle
    {
        public ChipStyle(string background, string foreground, string border)
        {
            Background = background;
            Foreground = foreground;
            Border = border;
        }

        public string Background { get; }
        public string Foreground { get; }
        public string Border { get; }
    }

    /*
        Urgency lived here, and it is gone with the effective date it was
        computed from - "2 days overdue", "goes live tomorrow", the per-row
        warning and the banner naming the most urgent set.

        It was put plainly that removing the date takes this with it, and that
        was the choice. Tech support works from the list in whatever order they
        sort it, and the default is now the submission date - which is the
        question they were really asking the effective date to answer: what has
        been waiting longest.

        Note there is a SECOND urgency vocabulary in the application, on the
        fare-request queue ("departs in 3 days"), computed from departure dates.
        That one is unaffected and stays.
    */

}
