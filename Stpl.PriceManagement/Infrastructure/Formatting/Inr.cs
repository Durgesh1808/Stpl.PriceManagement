using System;
using System.Globalization;

namespace Stpl.PriceManagement.Infrastructure.Formatting
{
    /// <summary>
    /// Formatting and reading of rupee amounts for the screen.
    /// </summary>
    /// <remarks>
    /// Rounding of prices is a pricing rule and lives in the database
    /// (intlgit.fn_PriceMatrix). What is here is presentation, plus the reading
    /// of figures people type.
    ///
    /// Every rounding call passes <see cref="MidpointRounding.AwayFromZero"/> on
    /// purpose: .NET rounds midpoints to even by default, so Math.Round(2.5m)
    /// is 2, while SQL Server's ROUND gives 3. Left alone that produces silent
    /// disagreement between a figure on screen and the same figure in the
    /// database. Amounts here are always positive, so away-from-zero and
    /// round-half-up are the same thing.
    /// </remarks>
    public static class Inr
    {
        /// <summary>
        /// Indian digit grouping: 1,58,900 rather than 158,900. This is how the
        /// team reads prices, and it is not what the invariant culture produces.
        /// </summary>
        private static readonly CultureInfo Indian = new CultureInfo("en-IN");

        /// <summary>Formats a whole-rupee amount, e.g. "1,58,900".</summary>
        public static string Format(decimal value)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", Indian);
        }

        /// <summary>
        /// A figure nobody has entered renders as nothing, not as zero.
        /// </summary>
        /// <remarks>
        /// The difference is the whole reason costs became blank-able. Zero is
        /// a real cost and prints as "0"; absent is not a cost at all and must
        /// not read like one, because a price built on it would be a price
        /// built on nothing.
        /// </remarks>
        public static string Format(decimal? value)
        {
            return value.HasValue ? Format(value.Value) : string.Empty;
        }

        /// <summary>
        /// Formats with a sign, for a difference against a calculated price -
        /// "+1,200" or "-800".
        /// </summary>
        public static string FormatSigned(decimal value)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return (rounded > 0 ? "+" : string.Empty) + rounded.ToString("N0", Indian);
        }

        /// <summary>
        /// Formats a rate or percentage as typed - "97", "800.62", "11" - with
        /// no trailing zeros.
        /// </summary>
        public static string Plain(decimal value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Empty for a figure nobody has entered - see Format.</summary>
        /// <remarks>
        /// This one fills input boxes, so the empty string is what puts the
        /// placeholder back and lets somebody type into a blank rather than
        /// clear a zero first.
        /// </remarks>
        public static string Plain(decimal? value)
        {
            return value.HasValue ? Plain(value.Value) : string.Empty;
        }

        /// <summary>
        /// Reads a figure somebody typed, tolerating grouping separators and
        /// surrounding space. Returns false rather than throwing, so a bad entry
        /// becomes a validation message instead of a 500.
        /// </summary>
        public static bool TryParse(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var cleaned = text.Replace(",", string.Empty).Replace("₹", string.Empty).Trim();
            return decimal.TryParse(
                cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Reads a typed figure, falling back when it cannot be read.</summary>
        public static decimal ParseOr(string text, decimal fallback)
        {
            decimal parsed;
            return TryParse(text, out parsed) ? parsed : fallback;
        }
    }
}
