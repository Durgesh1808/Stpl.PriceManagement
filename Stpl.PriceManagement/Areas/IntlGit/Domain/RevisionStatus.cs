using Stpl.PriceManagement.Areas.Core.Domain;
namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>Where a tour stands in its revision cycle.</summary>
    public enum RevisionStatus
    {
        /// <summary>Nothing pending - what is live is what is priced.</summary>
        Live,

        /// <summary>Changes made but not yet handed to tech support.</summary>
        DraftOpen,

        /// <summary>Air-ticketing has sent fares; the product team must set a price.</summary>
        FaresReceived,

        /// <summary>With tech support, nothing applied yet.</summary>
        NotStarted,

        /// <summary>With tech support, partly applied.</summary>
        InProgress,

        /// <summary>Everything applied; the new prices are live.</summary>
        UpdatedOnSite,

        /// <summary>
        /// Something on this tour cannot travel: a fare is back with
        /// air-ticketing, or a departure has no agreed price.
        /// </summary>
        Held,

        /// <summary>
        /// The product team has sent the tour to air-ticketing (a fare request
        /// is open, or a departure's fare has been sent back) and air-ticketing
        /// has not answered yet.
        /// </summary>
        /// <remarks>Last in the enum so existing values keep their numbers.</remarks>
        WithAirTicketing
    }

    /// <summary>The colours a status chip is drawn in.</summary>
    public sealed class StatusStyle
    {
        public StatusStyle(string label, string background, string foreground, string border)
        {
            Label = label;
            Background = background;
            Foreground = foreground;
            Border = border;
        }

        public string Label { get; }
        public string Background { get; }
        public string Foreground { get; }
        public string Border { get; }
    }

    public static class RevisionStatuses
    {
        /// <summary>
        /// Works out where a tour stands from the facts available.
        /// </summary>
        /// <param name="pendingChanges">Price rows plus removals not yet live.</param>
        /// <param name="hasOpenChangeSet">A set is with tech support.</param>
        /// <param name="completedRows">Rows tech support has ticked off.</param>
        /// <param name="totalRows">Rows in the open change set.</param>
        /// <param name="awaitingPriceAfterFares">
        /// Air-ticketing has submitted fares and no change set has been raised
        /// since, so the product team still has to set a selling price.
        /// </param>
        /// <param name="hasAppliedChangeSet">A set has been fully applied.</param>
        /// <param name="heldDepartures">
        /// Departures that cannot travel in a change request: a fare under
        /// query with air-ticketing, or no agreed price.
        /// </param>
        /// <param name="withAirTicketing">
        /// The tour is with air-ticketing: a fare request is open, or a
        /// departure's fare has been sent back and not yet answered.
        /// </param>
        /// <remarks>
        /// The hand-over order this follows, and the status each step shows:
        /// <list type="number">
        /// <item>product team sends to air-ticketing → <b>With air-ticketing</b></item>
        /// <item>air-ticketing sends fares back → <b>Fares received</b></item>
        /// <item>product team submits to tech support → <b>Not started</b> / <b>In progress</b></item>
        /// <item>tech support applies it → <b>Updated on site</b></item>
        /// </list>
        /// </remarks>
        public static RevisionStatus Evaluate(
            int pendingChanges,
            bool hasOpenChangeSet,
            int completedRows,
            int totalRows,
            bool awaitingPriceAfterFares,
            bool hasAppliedChangeSet,
            int heldDepartures = 0,
            bool withAirTicketing = false)
        {
            /*
                What tech support is doing outranks anything the product team has
                pending, because it is what is about to reach the website.

                An OPEN set is by definition one that has not been applied, so
                nothing in this branch may claim the prices are live. It used to:
                every row ticked returned UpdatedOnSite, on the reasonable-looking
                grounds that ticking the last row applies the set.

                It does - in the same request, normally within milliseconds. But
                if that request dies between the last tick and the applied stamp,
                the set stays open with every row ticked, Pricing is never told,
                the tour never moves off its old version, and this said the new
                prices were on the website. Indefinitely, and in green.

                So the work being finished is reported as work in progress, which
                is true while the promotion has not landed. It corrects itself
                within a poll: ChangeSets sweeps for exactly this state and
                finishes the job, after which the tour has an APPLIED set and
                reaches UpdatedOnSite by the honest route at the end of this
                method.
            */
            if (hasOpenChangeSet)
            {
                return completedRows > 0 ? RevisionStatus.InProgress : RevisionStatus.NotStarted;
            }

            /*
                Held outranks everything the product team has pending, and for
                the same reason tech support outranks it: these departures are
                not going anywhere, and that is the thing to know first. It is
                the precedence the price grid already uses on a held row.

                It has to sit ABOVE the two tests below, not beside them. A
                queried departure is taken out of the pending count - correctly,
                since it cannot travel - so without this the tour falls through
                to "Live" and advertises itself as settled while a fare dispute
                is open. That was the reported bug.
            */
            /*
                With air-ticketing next: the product team has handed the tour
                over and is waiting. Before this existed, sending a tour to
                air-ticketing changed nothing on the list - the status only
                moved once they answered.
            */
            if (withAirTicketing)
            {
                return RevisionStatus.WithAirTicketing;
            }

            /*
                Fares received outranks Held. When air-ticketing hands fares
                over, every price built on them goes blank until the product
                team agrees to it - so a tour that has just received fares ALWAYS
                has blank prices, and with Held first it read "Held" instead of
                "Fares received" and the hand-over never showed. A departure
                held because its fare is disputed is caught by withAirTicketing
                above, so nothing that is waiting on somebody else lands here.
            */
            if (awaitingPriceAfterFares)
            {
                return RevisionStatus.FaresReceived;
            }

            if (heldDepartures > 0)
            {
                return RevisionStatus.Held;
            }

            if (pendingChanges > 0)
            {
                return RevisionStatus.DraftOpen;
            }

            return hasAppliedChangeSet ? RevisionStatus.UpdatedOnSite : RevisionStatus.Live;
        }

        /// <summary>
        /// The label for a status, as the given role would say it.
        /// </summary>
        /// <remarks>
        /// One state, two names. When air-ticketing has handed fares over, they
        /// have <b>sent</b> them and the product team has <b>received</b> them -
        /// the same fact from either side of the handover. Saying "Fares
        /// received" to the person who just sent them reads as though somebody
        /// else did the work.
        /// </remarks>
        public static string Label(RevisionStatus status, UserRole role)
        {
            if (status == RevisionStatus.FaresReceived
                && role == UserRole.AirTicketingExecutive)
            {
                return "Fare sent";
            }

            // Same idea the other way round: the product team sent it, and
            // air-ticketing has been asked for fares.
            if (status == RevisionStatus.WithAirTicketing
                && role == UserRole.AirTicketingExecutive)
            {
                return "Fares requested";
            }

            return Label(status);
        }

        public static string Label(RevisionStatus status)
        {
            switch (status)
            {
                case RevisionStatus.DraftOpen:
                    return "Draft open";
                case RevisionStatus.FaresReceived:
                    return "Fares received";
                case RevisionStatus.NotStarted:
                    return "Not started";
                case RevisionStatus.InProgress:
                    return "In progress";
                case RevisionStatus.UpdatedOnSite:
                    return "Updated on site";
                case RevisionStatus.Held:
                    return "Held";
                case RevisionStatus.WithAirTicketing:
                    return "With air-ticketing";
                default:
                    return "Live";
            }
        }

        public static StatusStyle Style(RevisionStatus status)
        {
            switch (status)
            {
                case RevisionStatus.DraftOpen:
                    return new StatusStyle("Draft open", "#FDF6E4", "#7A5E12", "#E7CE8E");
                case RevisionStatus.FaresReceived:
                    return new StatusStyle("Fares received", "#EFF6FC", "#14528A", "#9CC4E4");
                case RevisionStatus.NotStarted:
                    return new StatusStyle("Not started", "#FEF1EA", "#A64A17", "#F3C4A6");
                case RevisionStatus.InProgress:
                    return new StatusStyle("In progress", "#EFF6FC", "#14528A", "#9CC4E4");
                case RevisionStatus.UpdatedOnSite:
                    return new StatusStyle("Updated on site", "#EAF6F0", "#14623D", "#AEDCC4");

                /* The app's danger red (--danger-bg / --danger-ink /
                   --danger-rule), which no other chip uses. Not the alert
                   orange the grid draws a held row in: that is "Not started"
                   here, and two states in one list sharing a colour is the
                   confusion the status map was written to stop -
                   docs/status-map.md. */
                case RevisionStatus.Held:
                    return new StatusStyle("Held", "#FDF0F0", "#8E2C2C", "#E3B3B3");

                // Purple: the only status waiting on air-ticketing, and a
                // colour no other chip uses.
                case RevisionStatus.WithAirTicketing:
                    return new StatusStyle("With air-ticketing", "#F4F0FB", "#5B3E96", "#CDBDEB");

                default:
                    return new StatusStyle("Live", "#EAF6F0", "#14623D", "#AEDCC4");
            }
        }
    }

    /// <summary>Version numbering: "v13" becomes "v14", single digits padded.</summary>
    public static class TourVersion
    {
        public static string Next(string current)
        {
            int number;
            var digits = (current ?? string.Empty).TrimStart('v', 'V');
            if (!int.TryParse(digits, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out number))
            {
                number = 0;
            }

            return "v" + (number + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
