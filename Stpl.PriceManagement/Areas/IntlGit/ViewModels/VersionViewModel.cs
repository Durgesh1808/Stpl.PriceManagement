using System;
using System.Collections.Generic;
using Stpl.PriceManagement.Areas.IntlGit.Models;

namespace Stpl.PriceManagement.Areas.IntlGit.ViewModels
{
    /// <summary>
    /// A past version of a product page, read-only, rendered from the
    /// photograph taken when that version went live. Nothing here recalculates.
    /// </summary>
    public sealed class VersionViewModel
    {
        public string Code { get; set; }
        public string Version { get; set; }

        /// <summary>Null when this version has no photograph.</summary>
        public VersionSnapshotResponse Snapshot { get; set; }

        /// <summary>Every version of this tour, so the page can offer the neighbouring ones.</summary>
        public IReadOnlyList<VersionHistoryEntryResponse> History { get; set; }
            = new List<VersionHistoryEntryResponse>();

        /// <summary>What this version changed, from the change set behind it.</summary>
        public ChangeSetDetailResponse ChangeSet { get; set; }

        /// <summary>The version one step older than this one, or null.</summary>
        public string PreviousVersion
        {
            get { return Neighbour(1); }
        }

        /// <summary>The version one step newer than this one, or null.</summary>
        public string NextVersion
        {
            get { return Neighbour(-1); }
        }

        // History is newest first, so +1 is older and -1 is newer. Only versions
        // that HAVE a photograph are offered.
        private string Neighbour(int step)
        {
            for (var i = 0; i < History.Count; i++)
            {
                if (!string.Equals(History[i].Version, Version, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var j = i + step;
                return j >= 0 && j < History.Count && History[j].HasSnapshot
                    ? History[j].Version
                    : null;
            }

            return null;
        }

        /// <summary>The entry for the version being shown, if it is on record.</summary>
        public VersionHistoryEntryResponse Entry
        {
            get
            {
                foreach (var entry in History)
                {
                    if (string.Equals(entry.Version, Version, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }

                return null;
            }
        }

        /// <summary>The change set behind a version, or null.</summary>
        public string ReferenceFor(string version)
        {
            foreach (var entry in History)
            {
                if (string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.ChangeSetRef;
                }
            }

            return null;
        }
    }
}
