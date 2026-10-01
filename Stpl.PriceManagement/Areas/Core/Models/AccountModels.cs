using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.Core.Models
{
    /// <summary>
    /// Which prices carry conditions, within a stated scope.
    /// </summary>
    /// <remarks>
    /// An unticked checkbox posts nothing, so a list of starred cells only means
    /// anything against a scope. <see cref="Hubs"/> is that scope: the grids
    /// that were on screen. Within them, anything absent from
    /// <see cref="Cells"/> loses its star; hubs outside them are untouched.
    /// </remarks>
    /// <summary>Somebody signing in.</summary>
    public sealed class SignInRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    /// <summary>Somebody changing their own password.</summary>
    /// <remarks>
    /// The email is not typed by the person - the web app fills it from the
    /// signed-in session, so this form can only ever change the password of
    /// whoever is using it.
    /// </remarks>
    public sealed class ChangePasswordRequest
    {
        public string Email { get; set; }
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }

    /// <summary>An operator resetting a password that is lost.</summary>
    public sealed class ResetPasswordRequest
    {
        public string Email { get; set; }
        public string NewPassword { get; set; }
    }

    /// <summary>
    /// The answer to a sign-in attempt.
    /// </summary>
    /// <remarks>
    /// Carries an identity or a refusal and nothing else. There is no token
    /// here: the web app turns this into its own cookie, and a second
    /// credential travelling back would be a second thing to leak.
    /// </remarks>
    public sealed class SignInResponse
    {
        public bool Succeeded { get; set; }

        /// <summary>Said plainly, because waiting is the answer.</summary>
        public bool IsLockedOut { get; set; }

        /// <summary>The same sentence for every refusal but a lockout.</summary>
        public string Message { get; set; }

        public int UserId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }

        /// <summary>The stored hash was made with an older cost.</summary>
        public bool NeedsRehash { get; set; }
    }

    public sealed class ChangePasswordResponse
    {
        public bool Succeeded { get; set; }

        /// <summary>Why not, when it was not. Null on success.</summary>
        public string Message { get; set; }
    }

    public sealed class UserSummaryResponse
    {
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }

        /// <summary>False means the account cannot be signed into yet.</summary>
        public bool HasPassword { get; set; }

        public bool IsLockedOut { get; set; }
        public DateTime? LastSignInUtc { get; set; }
    }
}
