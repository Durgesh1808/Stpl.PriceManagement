using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Infrastructure.Data;

namespace Stpl.PriceManagement.Areas.Core.Repositories
{
    public interface IUserRepository
    {
        /// <summary>The account for sign-in, including its hash. Null when there is none.</summary>
        Task<UserSignInRow> GetForSignInAsync(string email, CancellationToken cancellationToken);

        Task RecordSignInAsync(int userId, CancellationToken cancellationToken);

        Task RecordFailedSignInAsync(
            int userId, int maxAttempts, int lockoutMinutes, CancellationToken cancellationToken);

        Task<UserRow> UpsertAsync(
            string email, string displayName, string role, string passwordHash,
            CancellationToken cancellationToken);

        Task SetPasswordAsync(int userId, string passwordHash, CancellationToken cancellationToken);

        /// <summary>Null when there is no account with that address.</summary>
        Task<UserRow> SetActiveAsync(string email, bool isActive, CancellationToken cancellationToken);

        Task UnlockAsync(string email, CancellationToken cancellationToken);

        Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken);
    }

    /// <summary>People and sign-in (schema "core"). ADO.NET, stored procedures only.</summary>
    public sealed class UserRepository : IUserRepository
    {
        private readonly SqlDatabase _db;

        public UserRepository(SqlDatabase db)
        {
            _db = db;
        }

        public async Task<UserSignInRow> GetForSignInAsync(string email, CancellationToken cancellationToken)
        {
            var rows = await _db.QueryAsync<UserSignInRow>(
                "core.usp_GetUserForSignIn", p => p.Value("Email", email), cancellationToken);
            return rows.FirstOrDefault();
        }

        public Task RecordSignInAsync(int userId, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("core.usp_RecordSignIn", p => p.Value("UserId", userId), cancellationToken);
        }

        public Task RecordFailedSignInAsync(
            int userId, int maxAttempts, int lockoutMinutes, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("core.usp_RecordFailedSignIn", p =>
            {
                p.Value("UserId", userId);
                p.Value("MaxAttempts", maxAttempts);
                p.Value("LockoutMinutes", lockoutMinutes);
            }, cancellationToken);
        }

        public async Task<UserRow> UpsertAsync(
            string email, string displayName, string role, string passwordHash,
            CancellationToken cancellationToken)
        {
            var rows = await _db.QueryAsync<UserRow>("core.usp_UpsertUser", p =>
            {
                p.Value("Email", email);
                p.Value("DisplayName", displayName);
                p.Value("Role", role);
                p.Value("PasswordHash", passwordHash);
            }, cancellationToken);

            return rows.FirstOrDefault();
        }

        public Task SetPasswordAsync(int userId, string passwordHash, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync(
                "core.usp_SetPassword",
                p => p.Value("UserId", userId).Value("PasswordHash", passwordHash),
                cancellationToken);
        }

        public Task<UserRow> SetActiveAsync(string email, bool isActive, CancellationToken cancellationToken)
        {
            return _db.QuerySingleOrDefaultAsync<UserRow>(
                "core.usp_SetUserActive",
                p => p.Value("Email", email).Value("IsActive", isActive),
                cancellationToken);
        }

        public Task UnlockAsync(string email, CancellationToken cancellationToken)
        {
            return _db.ExecuteAsync("core.usp_UnlockUser", p => p.Value("Email", email), cancellationToken);
        }

        public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken)
        {
            return await _db.QueryAsync<UserRow>("core.usp_ListUsers", null, cancellationToken);
        }
    }
}
