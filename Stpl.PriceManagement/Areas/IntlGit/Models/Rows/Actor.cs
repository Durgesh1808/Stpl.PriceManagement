namespace Stpl.PriceManagement.Areas.IntlGit.Models.Rows
{
    /// <summary>
    /// Who is making a change, recorded against it in the activity log.
    /// </summary>
    /// <remarks>
    /// Carried as one value rather than two loose strings so a name cannot be
    /// passed where a role belongs. Caller-supplied until authentication is
    /// wired up; see docs/pricing-service-design.md section 1.
    /// </remarks>
    public sealed class Actor
    {
        public Actor(string name, string role)
        {
            Name = name;
            Role = role;
        }

        /// <summary>Display name, e.g. "Air-ticketing executive".</summary>
        public string Name { get; }

        /// <summary>Short code: product, airticketing, techsupport, system.</summary>
        public string Role { get; }

        /// <summary>
        /// Used where a change is made by the system rather than a person -
        /// a seed, a migration, a background promotion.
        /// </summary>
        public static Actor System
        {
            get { return new Actor("System", "system"); }
        }
    }
}
