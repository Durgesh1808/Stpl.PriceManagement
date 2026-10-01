using System;
using System.Collections.Generic;

namespace Stpl.PriceManagement.Areas.Core.Models
{
    // --- People -------------------------------------------------------------

    /// <summary>
    /// Everything needed to decide whether a sign-in succeeds.
    /// </summary>
    /// <remarks>
    /// Carries the hash, because the comparison happens in .NET where the
    /// hasher is. It never leaves this assembly: the API answers with an
    /// identity or with nothing.
    /// </remarks>
    public sealed class UserSignInRow
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public string PasswordHash { get; set; }
        public bool IsActive { get; set; }
        public int FailedAttempts { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
    }

    public sealed class UserRow
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastSignInUtc { get; set; }
        public bool HasPassword { get; set; }
        public bool IsLockedOut { get; set; }
    }
}
