using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Repositories;

namespace Stpl.PriceManagement.Areas.Core.Services
{
    /// <summary>
    /// Deciding whether somebody is who they say they are.
    /// </summary>
    /// <remarks>
    /// The verification happens here and nowhere else: a password hash never
    /// leaves this class - what leaves is an identity or a refusal.
    ///
    /// The hasher is ASP.NET Core's own. It is PBKDF2 with a per-user salt and
    /// the iteration count recorded in the hash itself, which is what lets the
    /// cost be raised later without invalidating everybody's password. Nothing
    /// here is hand-rolled, deliberately: a password hash written by hand is
    /// the single most common way an application of this size gets this wrong.
    /// </remarks>
    public sealed class SignInService
    {
        /// <summary>Attempts before an account is locked.</summary>
        private const int MaxAttempts = 5;

        /// <summary>How long it stays locked.</summary>
        private const int LockoutMinutes = 15;

        private readonly IUserRepository _users;
        private readonly IPasswordHasher<UserSignInRow> _hasher;
        private readonly ILogger<SignInService> _logger;

        public SignInService(
            IUserRepository users,
            IPasswordHasher<UserSignInRow> hasher,
            ILogger<SignInService> logger)
        {
            _users = users;
            _hasher = hasher;
            _logger = logger;
        }

        /// <summary>
        /// Signs somebody in, or does not.
        /// </summary>
        /// <remarks>
        /// Every refusal reads the same to the caller. A sign-in screen that
        /// distinguishes "no such address" from "wrong password" is a way to
        /// find out who works here, and one that distinguishes "no password
        /// set" is a way to find out which accounts are worth attacking.
        ///
        /// Being locked out is the one exception, because a person who cannot
        /// get in needs to know that waiting is the answer rather than trying
        /// harder.
        /// </remarks>
        public async Task<SignInResponse> SignInAsync(
            SignInRequest request, CancellationToken cancellationToken)
        {
            var email = (request.Email ?? string.Empty).Trim();
            var password = request.Password ?? string.Empty;

            if (email.Length == 0 || password.Length == 0)
            {
                return Refused();
            }

            var user = await _users.GetForSignInAsync(email, cancellationToken);

            if (user == null)
            {
                /*
                    No such account. Verify against a dummy hash anyway, so the
                    answer takes about as long as a real one - otherwise the
                    time taken says whether the address exists, and the generic
                    message above is undone by a stopwatch.
                */
                _hasher.VerifyHashedPassword(null, DummyHash, password);
                _logger.LogInformation("Sign-in refused: no account for that address.");
                return Refused();
            }

            if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Sign-in refused for user {UserId}: locked until {Until}.",
                    user.UserId, user.LockedUntilUtc.Value);

                return new SignInResponse
                {
                    Succeeded = false,
                    IsLockedOut = true,
                    Message = "Too many attempts. Try again in a few minutes."
                };
            }

            if (!user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
            {
                // Disabled, or created and never given a password. Neither is
                // the caller's business.
                _logger.LogInformation(
                    "Sign-in refused for user {UserId}: inactive or no password set.", user.UserId);
                return Refused();
            }

            var verdict = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

            if (verdict == PasswordVerificationResult.Failed)
            {
                await _users.RecordFailedSignInAsync(
                    user.UserId, MaxAttempts, LockoutMinutes, cancellationToken);

                _logger.LogWarning("Sign-in refused for user {UserId}: wrong password.", user.UserId);
                return Refused();
            }

            await _users.RecordSignInAsync(user.UserId, cancellationToken);
            _logger.LogInformation("User {UserId} signed in.", user.UserId);

            return new SignInResponse
            {
                Succeeded = true,
                UserId = user.UserId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,

                // PasswordVerificationResult.SuccessRehashNeeded means the hash
                // was made with an older cost. Saying so lets the caller ask us
                // to re-hash it later; nothing depends on it today.
                NeedsRehash = verdict == PasswordVerificationResult.SuccessRehashNeeded
            };
        }

        /// <summary>The shortest password this application will accept.</summary>
        /// <remarks>
        /// Length and nothing else. Composition rules - a capital, a digit, a
        /// symbol - push people towards one predictable pattern and towards
        /// writing the result down, and they are why the guidance that used to
        /// recommend them no longer does. A long password nobody else knows
        /// beats a short one that satisfies four rules.
        /// </remarks>
        private const int MinimumPasswordLength = 12;

        /// <summary>
        /// Changes somebody's own password, having checked they know the old one.
        /// </summary>
        /// <remarks>
        /// The current password is required even though the person is already
        /// signed in. A session left open on somebody's desk is the ordinary
        /// case this stops, and it costs the real owner one field.
        ///
        /// A wrong current password counts towards lockout exactly as it would
        /// at the sign-in screen. This form would otherwise be a way to try
        /// passwords without one.
        /// </remarks>
        public async Task<ChangePasswordResponse> ChangePasswordAsync(
            ChangePasswordRequest request, CancellationToken cancellationToken)
        {
            var email = (request.Email ?? string.Empty).Trim();
            var current = request.CurrentPassword ?? string.Empty;
            var replacement = request.NewPassword ?? string.Empty;

            if (replacement.Length < MinimumPasswordLength)
            {
                return ChangeRefused(
                    "A password needs at least " + MinimumPasswordLength + " characters.");
            }

            if (replacement == current)
            {
                return ChangeRefused("That is the password you are already using.");
            }

            var user = await _users.GetForSignInAsync(email, cancellationToken);

            if (user == null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
            {
                // Signed in but no usable account: the session outlived the
                // person's access. Says nothing about which of those it is.
                _logger.LogWarning("Password change refused: no usable account for that address.");
                return ChangeRefused("That password is not right.");
            }

            if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > DateTime.UtcNow)
            {
                return ChangeRefused("Too many attempts. Try again in a few minutes.");
            }

            var verdict = _hasher.VerifyHashedPassword(user, user.PasswordHash, current);

            if (verdict == PasswordVerificationResult.Failed)
            {
                await _users.RecordFailedSignInAsync(
                    user.UserId, MaxAttempts, LockoutMinutes, cancellationToken);

                _logger.LogWarning(
                    "Password change refused for user {UserId}: wrong current password.",
                    user.UserId);

                return ChangeRefused("That password is not right.");
            }

            await _users.SetPasswordAsync(
                user.UserId, _hasher.HashPassword(user, replacement), cancellationToken);

            _logger.LogInformation("User {UserId} changed their password.", user.UserId);

            return new ChangePasswordResponse { Succeeded = true };
        }

        /// <summary>
        /// An operator resetting a password nobody can remember.
        /// </summary>
        /// <remarks>
        /// No current password, because the point is that it is lost. Whoever
        /// calls this has already proved they are entitled to - today by being
        /// on the machine, running the account command (see README).
        /// </remarks>
        public async Task<ChangePasswordResponse> ResetPasswordAsync(
            string email, string newPassword, CancellationToken cancellationToken)
        {
            var replacement = newPassword ?? string.Empty;

            if (replacement.Length < MinimumPasswordLength)
            {
                return ChangeRefused(
                    "A password needs at least " + MinimumPasswordLength + " characters.");
            }

            var user = await _users.GetForSignInAsync(
                (email ?? string.Empty).Trim(), cancellationToken);

            if (user == null)
            {
                return ChangeRefused("No account with that address.");
            }

            await _users.SetPasswordAsync(
                user.UserId, _hasher.HashPassword(user, replacement), cancellationToken);

            _logger.LogWarning(
                "Password reset for user {UserId} by an operator.", user.UserId);

            return new ChangePasswordResponse { Succeeded = true };
        }

        /// <summary>Turns an account off when somebody leaves, or back on.</summary>
        public async Task<UserSummaryResponse> SetActiveAsync(
            string email, bool isActive, CancellationToken cancellationToken)
        {
            var row = await _users.SetActiveAsync(
                (email ?? string.Empty).Trim(), isActive, cancellationToken);

            if (row == null)
            {
                return null;
            }

            _logger.LogWarning(
                "Account {Email} was turned {State}.", row.Email, isActive ? "on" : "off");

            return new UserSummaryResponse
            {
                Email = row.Email,
                DisplayName = row.DisplayName,
                Role = row.Role,
                IsActive = row.IsActive,
                HasPassword = row.HasPassword
            };
        }

        /// <summary>Clears a lockout without changing the password.</summary>
        public async Task UnlockAsync(string email, CancellationToken cancellationToken)
        {
            await _users.UnlockAsync((email ?? string.Empty).Trim(), cancellationToken);
            _logger.LogInformation("A lockout was cleared by an operator.");
        }

        private static ChangePasswordResponse ChangeRefused(string message)
        {
            return new ChangePasswordResponse { Succeeded = false, Message = message };
        }

        public async Task<IReadOnlyList<UserSummaryResponse>> ListAsync(
            CancellationToken cancellationToken)
        {
            var rows = await _users.ListAsync(cancellationToken);

            return rows.Select(row => new UserSummaryResponse
            {
                Email = row.Email,
                DisplayName = row.DisplayName,
                Role = row.Role,
                IsActive = row.IsActive,
                HasPassword = row.HasPassword,
                IsLockedOut = row.IsLockedOut,
                LastSignInUtc = row.LastSignInUtc
            }).ToList();
        }

        /// <summary>
        /// Creates or updates a person, hashing the password if one is given.
        /// </summary>
        public async Task<UserSummaryResponse> UpsertAsync(
            string email, string displayName, string role, string password,
            CancellationToken cancellationToken)
        {
            string hash = null;
            if (!string.IsNullOrEmpty(password))
            {
                hash = _hasher.HashPassword(null, password);
            }

            var row = await _users.UpsertAsync(
                email, displayName, role, hash, cancellationToken);

            return row == null ? null : new UserSummaryResponse
            {
                Email = row.Email,
                DisplayName = row.DisplayName,
                Role = row.Role,
                IsActive = row.IsActive,
                HasPassword = row.HasPassword
            };
        }

        private static SignInResponse Refused()
        {
            return new SignInResponse
            {
                Succeeded = false,
                Message = "That email address and password do not match an account."
            };
        }

        /*
            A hash of a value nobody knows, so verifying a password against it
            costs the same as verifying a real one. Its only job is to take
            time.

            Computed by the hasher rather than written down: a hand-typed
            constant is not a valid hash, and verifying against an invalid one
            THROWS - which turned "no such account" into a 500 and so announced,
            very loudly, exactly the thing the dummy exists to conceal.
        */
        private string DummyHash
        {
            get
            {
                if (_dummyHash == null)
                {
                    _dummyHash = _hasher.HashPassword(null, Guid.NewGuid().ToString("N"));
                }

                return _dummyHash;
            }
        }

        private static string _dummyHash;
    }
}
