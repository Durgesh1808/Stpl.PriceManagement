using System.Globalization;
using Stpl.PriceManagement.Infrastructure.Formatting;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// Turns an activity row into something a person reads.
    /// </summary>
    /// <remarks>
    /// The database stores facts - an action, a field, an old and a new value -
    /// and never a sentence. What those facts are called on screen is a
    /// presentation decision, so it lives here, where it can change without a
    /// migration and without rewriting rows already logged.
    /// </remarks>
    public static class ActivityPhrasing
    {
        /// <summary>What kind of change this was, for a grouping heading.</summary>
        public static string ActionLabel(string action)
        {
            switch (action)
            {
                case "fare":
                    return "Airfare";
                case "price":
                    return "Selling price";
                case "costbuild":
                    return "Cost build";
                case "conditions":
                    return "Conditions";
                case "hub":
                    return "Hub";
                case "departure":
                    return "Departure date";
                case "fares-sent":
                    return "Fares sent to product";
                case "fares-requested":
                    return "Fares requested from air-ticketing";
                case "fare-queried":
                    return "Fare sent back for a re-quote";
                case "fare-requoted":
                    return "Fare re-quoted";
                case "changeset":
                    return "Sent to tech support";
                case "promoted":
                    return "Live on the website";
                default:
                    return "Change";
            }
        }

        /// <summary>
        /// The figure that moved, named as the screen names it: "Adult + TM",
        /// "Twin", "FX rate".
        /// </summary>
        public static string FieldLabel(string action, string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return null;
            }

            if (action == "costbuild")
            {
                switch (field)
                {
                    case "fx":
                        return "FX rate";
                    case "strike":
                        return "Strike-through %";
                    case "slab":
                        return "Launch pax slab";
                    case "shared":
                        return "Shared cost";
                }

                // "land:twin" and "perperson:twin" carry the occupancy after the colon.
                var split = field.IndexOf(':');
                if (split > 0)
                {
                    var kind = field.Substring(0, split);
                    var occupancy = OccupancyLabel(field.Substring(split + 1));

                    return kind == "land"
                        ? "Land in FX · " + occupancy
                        : "Per-person INR · " + occupancy;
                }

                return field;
            }

            // A send-back and a re-quote both name a fare band in this column,
            // exactly as a fare change does, so they take the same labels. Left
            // out, they fell through to the occupancy names below and a queried
            // adult fare read as a bare "adult".
            if (action == "fare" || action == "fare-queried" || action == "fare-requoted")
            {
                switch (field)
                {
                    case "adult":
                        return "Adult + TM";
                    case "child":
                        return "Child";
                    case "infant":
                        return "Infant";
                }
            }

            return OccupancyLabel(field);
        }

        private static string OccupancyLabel(string occupancy)
        {
            switch (occupancy)
            {
                case "twin":
                    return "Twin";
                case "triple":
                    return "Triple";
                case "single":
                    return "Single";
                case "cwb":
                    return "CWB";
                case "cnb":
                    return "CNB";
                case "infant":
                    return "Infant";
                default:
                    return occupancy;
            }
        }

        /// <summary>
        /// How a value is written. A rate or a percentage is not money and must
        /// not be shown with rupee grouping - "1,20,000" for an FX rate of
        /// 120000 would be nonsense, and 97 is just 97.
        /// </summary>
        public static string Value(string action, string field, decimal? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            if (action == "costbuild"
                && (field == "fx" || field == "strike" || field == "slab"))
            {
                return Inr.Plain(value.Value);
            }

            return Inr.Format(value.Value);
        }

        /// <summary>
        /// Which side of the handover this actor is on, for colouring the entry.
        /// </summary>
        public static string RoleLabel(string actorRole)
        {
            switch (actorRole)
            {
                case "product":
                    return "Product";
                case "airticketing":
                    return "Air-ticketing";
                case "techsupport":
                    return "Tech support";
                default:
                    return "System";
            }
        }

        /// <summary>
        /// Where the change landed: "Bengaluru · 13 Oct 26", or just the hub, or
        /// nothing at all for a tour-wide change like the FX rate.
        /// </summary>
        public static string Where(string hubName, System.DateTime? departureDate)
        {
            var hasHub = !string.IsNullOrEmpty(hubName);

            if (hasHub && departureDate.HasValue)
            {
                return hubName + " · " + DepartureDateFormat.Short(departureDate.Value);
            }

            if (hasHub)
            {
                return hubName;
            }

            return departureDate.HasValue
                ? DepartureDateFormat.Short(departureDate.Value)
                : null;
        }

        /// <summary>
        /// "3 hours ago", "Yesterday", "16 Sep" - how long ago something
        /// happened matters more than the exact minute when scanning a list.
        /// </summary>
        public static string Ago(System.DateTime occurredLocal, System.DateTime now)
        {
            var elapsed = now - occurredLocal;

            if (elapsed.TotalMinutes < 1)
            {
                return "just now";
            }

            if (elapsed.TotalMinutes < 60)
            {
                var minutes = (int)elapsed.TotalMinutes;
                return minutes + (minutes == 1 ? " minute ago" : " minutes ago");
            }

            if (elapsed.TotalHours < 24 && occurredLocal.Date == now.Date)
            {
                var hours = (int)elapsed.TotalHours;
                return hours + (hours == 1 ? " hour ago" : " hours ago");
            }

            if (occurredLocal.Date == now.Date.AddDays(-1))
            {
                return "yesterday";
            }

            return DepartureDateFormat.Short(occurredLocal);
        }

        /// <summary>The clock time, for the detail line under a grouped entry.</summary>
        public static string Time(System.DateTime occurredLocal)
        {
            return occurredLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
