namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>
    /// Where one price cell stands.
    /// </summary>
    /// <remarks>
    /// The rule that decides this lives in intlgit.fn_PriceMatrix and nowhere
    /// else. These names exist so C# can ask "is this one still with tech
    /// support?" without a string literal in a Razor file, and so a typo in the
    /// comparison is a compile error rather than a cell that silently never
    /// matches.
    ///
    /// The distinction that matters to the people using the screen is between
    /// Draft and Sent. Before this existed, both were one dot, and a set that
    /// had gone to tech support looked exactly like one that had not.
    /// </remarks>
    public static class PriceStates
    {
        /// <summary>
        /// No airfare yet, so there is no price to be in any state. Since the
        /// tour manager's seat entered the formula this covers a missing ADULT
        /// fare too, whatever the occupancy: every price now depends on it.
        /// </summary>
        public const string NoFare = "nofare";

        /// <summary>
        /// There is a fare, but no figure anybody has agreed to since it last
        /// moved - so the box is empty and the calculated price stands beside
        /// it waiting to be taken or typed over.
        /// </summary>
        public const string NoPrice = "noprice";

        /// <summary>Never been on the website.</summary>
        public const string New = "new";

        /// <summary>Changed here, not yet handed to tech support.</summary>
        public const string Draft = "draft";

        /// <summary>Handed to tech support, not yet live.</summary>
        public const string Sent = "sent";

        /// <summary>What the website shows.</summary>
        public const string Live = "live";

        /// <summary>
        /// What to call it on screen. Empty for a settled price: a label on
        /// every one of six hundred cells is noise, and "live" is the default
        /// people can assume.
        /// </summary>
        public static string Label(string state)
        {
            switch (state)
            {
                case New:     return "New";
                case Draft:   return "Changed";
                case Sent:    return "With tech support";
                case NoFare:  return "Awaiting airfare";
                case NoPrice: return "Needs a price";
                default:     return null;
            }
        }

        /// <summary>
        /// Whether this cell is part of what a change set would carry: either
        /// unsent work or a departure that has never been published.
        /// </summary>
        /// <remarks>
        /// NoPrice is deliberately absent. A cell with no agreed figure is not
        /// unsent work waiting to travel - it cannot travel at all, and its
        /// whole departure is held back until somebody prices it. Counting it
        /// here would put it in a total that promises to reach the website.
        /// </remarks>
        public static bool IsUnsent(string state)
        {
            return state == Draft || state == New;
        }
    }
}
