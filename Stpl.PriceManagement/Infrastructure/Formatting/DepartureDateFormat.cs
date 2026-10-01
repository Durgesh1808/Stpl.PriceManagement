using System;
using System.Globalization;

namespace Stpl.PriceManagement.Infrastructure.Formatting
{
    /// <summary>
    /// Departure dates are stored as dates and shown as "13 Oct 26" - the form
    /// the product team types and reads. Every date this application shows is
    /// formatted here.
    ///
    /// One deliberate exception: PriceTableHtml formats its own heading date as
    /// "dd MMM yyyy". That block is pasted onto the public website, so it is
    /// read by customers rather than by the team, and it is not this class's to
    /// change. The summary here used to claim sole responsibility, which was
    /// never true.
    /// </summary>
    public static class DepartureDateFormat
    {
        private static readonly string[] Months =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        /// <summary>Renders a date as "13 Oct 26".</summary>
        public static string Short(DateTime value)
        {
            return value.Day.ToString("00", CultureInfo.InvariantCulture)
                + " " + Months[value.Month - 1]
                + " " + (value.Year % 100).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Renders a date as "21 Sep 2026", used for effective dates.</summary>
        public static string Long(DateTime value)
        {
            return value.Day.ToString("00", CultureInfo.InvariantCulture)
                + " " + Months[value.Month - 1]
                + " " + value.Year.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Renders a timestamp as "16 Sep 2026, 11:20".</summary>
        public static string Stamp(DateTime value)
        {
            return Long(value) + ", " + value.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Returns the 0-based month for "Oct", "october", "OCT"; -1 if unreadable.</summary>
        public static int MonthIndex(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return -1;
            }

            var probe = token.Trim();
            if (probe.Length > 3)
            {
                probe = probe.Substring(0, 3);
            }

            for (var i = 0; i < Months.Length; i++)
            {
                if (string.Equals(Months[i], probe, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Reads a typed departure date: "8 Dec 26", "08 Dec 2026", "8 december 2026".
        /// </summary>
        /// <remarks>
        /// Day, month AND year, every time. There used to be an AssumedYear of
        /// 2026 standing in for a year nobody typed, which was right for
        /// exactly as long as 2026 was the season being planned. On 1 January
        /// 2027 somebody entering next winter's departures types "08 Dec" and
        /// gets 8 December 2026 - eleven months past, accepted without a word,
        /// and then folded out of the grid by the past-departure rule so that
        /// nobody sees it either. A date is what a customer is sold and what
        /// air-ticketing quotes against; four keystrokes are cheap and a guess
        /// never is.
        ///
        /// A year is either two digits or four. "8 Dec 6", a slip for 26, used
        /// to pass as 2006 - it cleared the year > 2000 guard, created a
        /// departure two decades past, and disappeared the same way.
        ///
        /// The date field is an &lt;input type="date"&gt;, which submits
        /// yyyy-MM-dd whatever the browser shows the person. That shape is read
        /// first and exactly, below. Typed entry still takes every shape it
        /// always did - the picker is how the date is chosen, not a new format
        /// for the rest of the system, which still reads and writes "08 Dec 26".
        /// </remarks>
        public static bool TryParse(string text, out DateTime value)
        {
            value = default(DateTime);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // The picker's shape, and only exactly that. TryParseExact rejects
            // a month of 13 or a 30th of February on its own, so the guards
            // below do not need to see it. Nothing the typed path accepts looks
            // like this - "08-Dec-26" has a month name where this has digits -
            // so reading it first cannot take a date away from that path.
            if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return true;
            }

            var parts = text.Replace(",", string.Empty)
                .Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);

            // Three parts, not two. A date without a year is not a date.
            if (parts.Length < 3)
            {
                return false;
            }

            int day;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out day))
            {
                return false;
            }

            var month = MonthIndex(parts[1]);
            if (month < 0)
            {
                return false;
            }

            var typed = parts[2].Trim();
            if (typed.Length != 2 && typed.Length != 4)
            {
                return false;
            }

            int typedYear;
            if (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out typedYear))
            {
                return false;
            }

            var year = typed.Length == 4 ? typedYear : 2000 + typedYear;

            if (year < 2000 || year > 2999 || day < 1 || day > DateTime.DaysInMonth(year, month + 1))
            {
                return false;
            }

            value = new DateTime(year, month + 1, day);
            return true;
        }

        /// <summary>
        /// Tidies a typed date into the canonical "08 Dec 26" shape. Returns the
        /// input untouched when it cannot be read, so the caller can show the
        /// user what they actually typed.
        /// </summary>
        public static string Normalize(string text)
        {
            DateTime parsed;
            return TryParse(text, out parsed) ? Short(parsed) : text;
        }
    }
}
