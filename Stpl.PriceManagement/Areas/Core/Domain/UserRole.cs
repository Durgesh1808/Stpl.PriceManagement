using System;

namespace Stpl.PriceManagement.Areas.Core.Domain
{
    /// <summary>Who is using the system. Each role sees a different set of screens.</summary>
    public enum UserRole
    {
        ProductExecutive,
        AirTicketingExecutive,
        TechSupport
    }

    public static class UserRoles
    {
        public const string ProductExecutiveClaim = "product.executive";
        public const string AirTicketingClaim = "airticketing.executive";
        public const string TechSupportClaim = "techsupport";

        public static string DisplayName(UserRole role)
        {
            switch (role)
            {
                case UserRole.ProductExecutive:
                    return "Product executive";
                case UserRole.AirTicketingExecutive:
                    return "Air-ticketing executive";
                case UserRole.TechSupport:
                    return "Tech support";
                default:
                    return role.ToString();
            }
        }

        /// <summary>
        /// The short form stored against an activity log entry: "product",
        /// "airticketing", "techsupport".
        /// </summary>
        /// <remarks>
        /// Deliberately not the claim value. A claim is an authentication
        /// detail that will change when the identity provider is chosen, and
        /// rows already written must not stop meaning what they meant.
        /// </remarks>
        public static string ShortCode(UserRole role)
        {
            switch (role)
            {
                case UserRole.ProductExecutive:
                    return "product";
                case UserRole.AirTicketingExecutive:
                    return "airticketing";
                case UserRole.TechSupport:
                    return "techsupport";
                default:
                    return "system";
            }
        }

        public static string ClaimValue(UserRole role)
        {
            switch (role)
            {
                case UserRole.ProductExecutive:
                    return ProductExecutiveClaim;
                case UserRole.AirTicketingExecutive:
                    return AirTicketingClaim;
                case UserRole.TechSupport:
                    return TechSupportClaim;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Reads a role back from any spelling the system writes.
        /// </summary>
        /// <remarks>
        /// Three spellings are in circulation and all three are legitimate: the
        /// claim value on a signed-in person, the enum name, and the short code
        /// that goes into the activity log and travels on X-Acting-Role.
        ///
        /// The short codes were missing until the Pricing service started
        /// authorising against this. It parsed the header, got nothing back,
        /// and refused air-ticketing every write - including the fares they are
        /// the only ones allowed to enter.
        /// </remarks>
        public static bool TryParse(string value, out UserRole role)
        {
            role = UserRole.ProductExecutive;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (Is(value, ProductExecutiveClaim, "ProductExecutive", "product"))
            {
                role = UserRole.ProductExecutive;
                return true;
            }

            if (Is(value, AirTicketingClaim, "AirTicketingExecutive", "airticketing"))
            {
                role = UserRole.AirTicketingExecutive;
                return true;
            }

            if (Is(value, TechSupportClaim, "TechSupport", "techsupport"))
            {
                role = UserRole.TechSupport;
                return true;
            }

            return false;
        }

        private static bool Is(string value, params string[] spellings)
        {
            foreach (var spelling in spellings)
            {
                if (string.Equals(value, spelling, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
