using Stpl.PriceManagement.Areas.Core.Domain;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Domain
{
    /// <summary>What a write needs the person to be allowed to do.</summary>
    /// <remarks>
    /// Each value maps onto one property of <see cref="RevisionPermissions"/>,
    /// which is the single statement of the rule. Nothing here decides anything
    /// itself - it names a question and the domain answers it, so the service
    /// and the screens cannot drift apart. (This was the [RequiresPermission]
    /// filter on the old Pricing API.)
    /// </remarks>
    public enum TourPermission
    {
        SeeCostAndMargin,
        EditCostBuild,
        EditFares,
        EditPublishedPrice,
        ChangeStructure,
        SubmitChangeSet,
        RequestFares,
        RaiseFareQuery,
        ResolveFareQuery,
        SubmitFares
    }

    public static class TourPermissions
    {
        /// <summary>
        /// Whether this role may do this.
        /// </summary>
        /// <remarks>
        /// isEditingRequested is true: edit mode is a screen state, not a
        /// permission - somebody reaching a write is asking to edit by
        /// definition, and the question is only whether their role may.
        /// </remarks>
        public static bool IsAllowed(UserRole role, TourPermission permission)
        {
            var permissions = RevisionPermissions.For(role, true);

            switch (permission)
            {
                case TourPermission.SeeCostAndMargin:
                    return permissions.CanSeeCostAndMargin;
                case TourPermission.EditCostBuild:
                    return permissions.CanEditCostBuild;
                case TourPermission.EditFares:
                    return permissions.CanEditFares;
                case TourPermission.EditPublishedPrice:
                    return permissions.CanEditPublishedPrice;
                case TourPermission.ChangeStructure:
                    return permissions.CanChangeStructure;
                case TourPermission.SubmitChangeSet:
                    return permissions.CanSubmitChangeSet;
                case TourPermission.RequestFares:
                    return permissions.CanRequestFares;
                case TourPermission.RaiseFareQuery:
                    return permissions.CanRaiseFareQuery;
                case TourPermission.ResolveFareQuery:
                    return permissions.CanResolveFareQuery;
                case TourPermission.SubmitFares:
                    return permissions.CanSubmitFares;
                default:
                    // A permission nobody has taught this method about is a
                    // refusal, not an allowance.
                    return false;
            }
        }

        /// <summary>The revision workspace permissions for the signed-in person.</summary>
        public static RevisionPermissions PermissionsFor(this CurrentUser user, bool isEditingRequested)
        {
            return RevisionPermissions.For(user.Role, isEditingRequested);
        }
    }
}
