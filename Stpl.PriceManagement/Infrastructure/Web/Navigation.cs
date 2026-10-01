using System.Collections.Generic;

namespace Stpl.PriceManagement.Infrastructure.Web
{
    /// <summary>One link in the left navigation rail.</summary>
    public sealed class NavItem
    {
        public NavItem(string label, string icon, string href, params string[] match)
        {
            Label = label;
            Icon = icon;
            Href = href;
            Match = match == null || match.Length == 0 ? new[] { href } : match;
        }

        public string Label { get; }

        /// <summary>A sprite symbol name (Views/Shared/_IconSprite.cshtml), not a glyph.</summary>
        public string Icon { get; }

        public string Href { get; }

        /// <summary>The path prefixes that mark this item as the current one.</summary>
        public IReadOnlyList<string> Match { get; }
    }

    /// <summary>One heading in the rail - one pricing area - and its screens.</summary>
    public sealed class NavSection
    {
        public NavSection(string title, IReadOnlyList<NavItem> items)
        {
            Title = title;
            Items = items;
        }

        public string Title { get; }
        public IReadOnlyList<NavItem> Items { get; }
    }

    /// <summary>
    /// The navigation rail, section by section.
    /// </summary>
    /// <remarks>
    /// Each pricing area contributes its own section. To add one (for example
    /// International FIT): write an IntlFitNavigation.Section(user) beside
    /// Areas/IntlGit/IntlGitNavigation.cs and add it to the list below.
    /// </remarks>
    public static class AppNavigation
    {
        public static IReadOnlyList<NavSection> For(CurrentUser user)
        {
            var sections = new List<NavSection>();

            var intlGit = Areas.IntlGit.IntlGitNavigation.Section(user);
            if (intlGit != null && intlGit.Items.Count > 0)
            {
                sections.Add(intlGit);
            }

            return sections;
        }
    }
}
