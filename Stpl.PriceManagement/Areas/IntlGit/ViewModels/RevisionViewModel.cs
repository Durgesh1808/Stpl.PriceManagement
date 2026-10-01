using System;
using System.Collections.Generic;
using System.Linq;
using Stpl.PriceManagement.Areas.IntlGit.Domain;
using Stpl.PriceManagement.Areas.IntlGit.Models;
using Stpl.PriceManagement.Infrastructure.Formatting;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>
    /// Everything the price revision workspace draws for one tour.
    /// </summary>
    /// <remarks>
    /// Code, Edit and Open travel in the address (?code=...&amp;edit=true&amp;open=blr,del)
    /// rather than in session state, so the page can be reloaded or shared
    /// without losing them and the back button behaves.
    /// </remarks>
    public sealed class RevisionViewModel
    {
        /// <summary>
        /// "Collapse all" needs to be told apart from arriving fresh, and an
        /// empty query value cannot do it ("?open=" binds to null), hence a sentinel.
        /// </summary>
        public const string NoneOpen = "none";

        public string Code { get; set; }

        /// <summary>Edit mode.</summary>
        public bool Edit { get; set; }

        /// <summary>Which hubs are expanded, as a comma-separated list of hub codes.</summary>
        public string Open { get; set; }

        public TourRevisionResponse Revision { get; set; }
        public PendingChangesResponse Pending { get; set; }
        public IReadOnlyList<HubMasterResponse> HubMaster { get; set; }
        public RevisionPermissions Permissions { get; set; }

        public List<(string Label, string Href)> Crumbs { get; set; }

        /// <summary>Departures on an active hub with no airfare entered.</summary>
        public int AwaitingFares { get; set; }

        /// <summary>Prices whose figure has moved and which nobody has agreed to since.</summary>
        public int UnconfirmedPrices { get; set; }

        /// <summary>How many departures those unconfirmed prices are spread across.</summary>
        public int UnconfirmedDepartures { get; set; }

        /// <summary>Fares sent back to air-ticketing, each holding its departure.</summary>
        public IReadOnlyList<FareQueryResponse> FareQueries { get; set; } = new List<FareQueryResponse>();

        /// <summary>This tour's past change sets, newest first - its version history.</summary>
        public IReadOnlyList<ChangeSetSummaryResponse> History { get; set; }

        /// <summary>Which versions can be opened as a full page, keyed by version.</summary>
        public IReadOnlyDictionary<string, bool> VersionSnapshots { get; set; } = new Dictionary<string, bool>();

        /// <summary>Who changed what on this tour, newest first.</summary>
        public IReadOnlyList<ActivityResponse> Activity { get; set; }

        /// <summary>The activity log grouped into one entry per person per save.</summary>
        public IReadOnlyList<ActivitySession> Sessions { get; set; }

        /// <summary>When fares were last asked for and not yet sent. Null if none.</summary>
        public DateTime? FareRequestedUtc { get; set; }

        /// <summary>
        /// Set when a save was refused because the tour moved underneath it.
        /// The page shows what changed and keeps what was typed.
        /// </summary>
        public string StaleMessage { get; set; }

        /// <summary>What the refused save posted, so the grid can redraw it.</summary>
        public HubGridForm Posted { get; set; }

        /// <summary>What moved since this page was drawn, newest first.</summary>
        public IReadOnlyList<ActivityResponse> StaleChanges { get; set; } = new List<ActivityResponse>();

        public sealed class ActivitySession
        {
            public string ActorName { get; set; }
            public string ActorRole { get; set; }
            public string Action { get; set; }

            /// <summary>Local time of the save this entry describes.</summary>
            public DateTime OccurredLocal { get; set; }

            public string Note { get; set; }

            /// <summary>The figures that moved, in the order they were logged.</summary>
            public List<ActivityResponse> Changes { get; set; }
        }

        // ------------------------------------------------------------------
        // Hubs: which are open, and links that open / close them
        // ------------------------------------------------------------------

        /// <summary>Hubs in the master that are not yet on this tour.</summary>
        public IReadOnlyList<HubMasterResponse> AvailableHubs
        {
            get
            {
                if (HubMaster == null || Revision == null)
                {
                    return new List<HubMasterResponse>();
                }

                var onTour = Revision.Hubs.Select(h => h.Hub).ToList();
                return HubMaster.Where(h => !onTour.Contains(h.Hub)).ToList();
            }
        }

        public bool IsHubOpen(string hub)
        {
            return ExpandedHubs().Contains(hub);
        }

        /// <summary>
        /// Which hubs are open. Everything is open on arrival - the grid is the
        /// point of the screen.
        /// </summary>
        public HashSet<string> ExpandedHubs()
        {
            if (Open == NoneOpen)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (string.IsNullOrEmpty(Open))
            {
                return Revision == null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(
                        Revision.Hubs.Select(h => h.Hub), StringComparer.OrdinalIgnoreCase);
            }

            var open = Open
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim());

            return new HashSet<string>(open, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The address of this page with one hub expanded or collapsed, anchored
        /// to that hub so toggling does not lose your place.
        /// </summary>
        public string ToggleHubUrl(string hub)
        {
            var expanded = ExpandedHubs();
            if (!expanded.Remove(hub))
            {
                expanded.Add(hub);
            }

            var joined = string.Join(",", expanded);
            return Self(joined.Length == 0 ? NoneOpen : joined, anchor: "hub-" + hub);
        }

        /// <summary>
        /// The hubs whose grids are actually rendered, for the grid form to
        /// declare what it is speaking for (the scope of the star replace).
        /// </summary>
        public string RenderedHubs
        {
            get
            {
                if (Revision == null)
                {
                    return string.Empty;
                }

                return string.Join(
                    ",", Revision.Hubs.Where(h => IsHubOpen(h.Hub)).Select(h => h.Hub));
            }
        }

        public string ExpandAllUrl
        {
            get { return Self(string.Join(",", Revision.Hubs.Select(h => h.Hub))); }
        }

        public string CollapseAllUrl
        {
            get { return Self(NoneOpen); }
        }

        /// <summary>The hub list with one more hub open.</summary>
        public string AppendOpen(string hub)
        {
            var expanded = ExpandedHubs();
            expanded.Add(hub);
            return string.Join(",", expanded);
        }

        /// <summary>
        /// The address of this page, optionally anchored to a section. Every save
        /// is a POST-redirect-GET; the anchor returns you to where you were.
        /// </summary>
        public string Self(string open = null, bool? edit = null, string anchor = null)
        {
            return "/IntlGit/Revision" + Query(open, edit, anchor);
        }

        /// <summary>Where the grid asks what prices a typed fare would give (writes nothing).</summary>
        public string PreviewUrl
        {
            get { return "/IntlGit/Revision/Preview" + Query(null, null, null); }
        }

        private string Query(string open, bool? edit, string anchor)
        {
            var query = "?code=" + Uri.EscapeDataString(Code ?? string.Empty);

            if (edit ?? Edit)
            {
                query += "&edit=true";
            }

            var openHubs = open ?? Open;
            if (openHubs != null)
            {
                query += "&open=" + Uri.EscapeDataString(openHubs);
            }

            if (!string.IsNullOrEmpty(anchor))
            {
                query += "#" + anchor;
            }

            return query;
        }

        // ------------------------------------------------------------------
        // What a grid box shows: what was typed (after a refused save) or what is stored
        // ------------------------------------------------------------------

        /// <summary>The fare to draw.</summary>
        public string FareValue(string hub, string key, string band, decimal? stored)
        {
            var typed = PostedValue(Posted == null ? null : Posted.Fare, hub + "|" + key + "|" + band);
            return typed ?? (stored.HasValue ? Inr.Plain(stored.Value) : string.Empty);
        }

        /// <summary>The published price to draw, same rule.</summary>
        public string PriceValue(string hub, string key, string occupancy, decimal? stored)
        {
            var typed = PostedValue(Posted == null ? null : Posted.Price, hub + "|" + key + "|" + occupancy);
            return typed ?? (stored.HasValue ? Inr.Format(stored.Value) : string.Empty);
        }

        /// <summary>The flight note to draw, same rule.</summary>
        public string FlightValue(string hub, string key, string stored)
        {
            var typed = PostedValue(Posted == null ? null : Posted.Flight, hub + "|" + key);
            return typed ?? stored;
        }

        /// <summary>
        /// Whether the star box is ticked. After a refusal this reads the post,
        /// where an absent key means unticked - which is how checkboxes travel.
        /// </summary>
        public bool IsStarred(string hub, string key, string occupancy, bool stored)
        {
            if (Posted == null || Posted.Hubs == null)
            {
                return stored;
            }

            var scope = Posted.Hubs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (!scope.Contains(hub, StringComparer.OrdinalIgnoreCase))
            {
                return stored;
            }

            return Posted.Star != null
                && Posted.Star.ContainsKey(hub + "|" + key + "|" + occupancy);
        }

        private static string PostedValue(Dictionary<string, string> posted, string key)
        {
            string value;
            return posted != null && posted.TryGetValue(key, out value) ? value : null;
        }
    }

    // ----------------------------------------------------------------------
    // The forms the revision workspace posts
    // ----------------------------------------------------------------------

    /// <summary>The cost build form.</summary>
    public sealed class CostBuildForm
    {
        public decimal? FxRate { get; set; }
        public decimal? StrikePercent { get; set; }
        public int PaxSlab { get; set; }

        /* Nullable, and so are the two dictionaries below, because an empty
           box means nobody has entered that cost. Bound as decimal they came
           back as 0, and a nought prices the tour. */
        public decimal? SharedCost { get; set; }
        public string Note { get; set; }
        public Dictionary<string, decimal?> Land { get; set; }
        public Dictionary<string, decimal?> PerPerson { get; set; }
    }

    /// <summary>
    /// The whole tour's grid. Values bind as text, not decimal, because the
    /// boxes show Indian grouping - "2,18,100" - which decimal binding rejects;
    /// <see cref="Inr.TryParse"/> reads them.
    /// </summary>
    public sealed class HubGridForm
    {
        /// <summary>The hubs whose grids this form covers, comma-separated.</summary>
        public string Hubs { get; set; }

        /// <summary>Keyed "hub|yyyy-MM-dd|band".</summary>
        public Dictionary<string, string> Fare { get; set; }

        /// <summary>Keyed "hub|yyyy-MM-dd|occupancy".</summary>
        public Dictionary<string, string> Price { get; set; }

        /// <summary>
        /// Air-ticketing's flight notes, keyed "hub|yyyy-MM-dd". An empty box
        /// means "clear it" rather than "leave it alone".
        /// </summary>
        public Dictionary<string, string> Flight { get; set; }

        /// <summary>
        /// The tour as this page saw it, in ticks, so the save can be refused if
        /// somebody else moved the tour in the meantime.
        /// </summary>
        public long ExpectedModifiedUtc { get; set; }

        /// <summary>The "conditions apply" boxes that are ticked (unticked ones are absent).</summary>
        public Dictionary<string, string> Star { get; set; }
    }
}
