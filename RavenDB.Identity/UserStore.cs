using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Documents.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raven.Client.Documents.Operations.Backups;

namespace Raven.Identity
{
    /// <summary>
    /// UserStore for entities in a RavenDB database.
    /// </summary>
    /// <typeparam name="TUser"></typeparam>
	/// <typeparam name="TRole"></typeparam>
    public class UserStore<TUser, TRole> : 
        IUserStore<TUser>, 
        IUserLoginStore<TUser>, 
        IUserClaimStore<TUser>, 
        IUserRoleStore<TUser>,
        IUserPasswordStore<TUser>, 
        IUserSecurityStampStore<TUser>, 
        IUserEmailStore<TUser>, 
        IUserLockoutStore<TUser>,
        IUserTwoFactorStore<TUser>, 
        IUserPhoneNumberStore<TUser>,
        IUserAuthenticatorKeyStore<TUser>,
        IUserAuthenticationTokenStore<TUser>,
        IUserTwoFactorRecoveryCodeStore<TUser>,
        IQueryableUserStore<TUser>
        where TUser : IdentityUser
		where TRole : IdentityRole, new()
    {
        private bool _disposed;
        private readonly Func<IAsyncDocumentSession>? getSessionFunc;
        private IAsyncDocumentSession? session;
        private readonly RavenIdentityOptions options;
        private readonly ILogger logger;

        private const string emailReservationKeyPrefix = "emails/";

        /// <summary>
        /// Creates a new user store that uses the Raven document session returned from the specified session fetcher.
        /// </summary>
        /// <param name="getSession">The function that gets the Raven document session.</param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public UserStore(Func<IAsyncDocumentSession> getSession, IOptions<RavenIdentityOptions> options, ILogger<UserStore<TUser, TRole>> logger)
        {
            this.getSessionFunc = getSession;
            this.logger = logger;
            this.options = options.Value;
        }

        /// <summary>
        /// Creates a new user store that uses the specified Raven document session.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public UserStore(IAsyncDocumentSession session, IOptions<RavenIdentityOptions> options, ILogger<UserStore<TUser, TRole>> logger)
        {
            this.session = session;
            this.logger = logger;
            this.options = options.Value;
        }

        #region IDisposable implementation

        /// <summary>
        /// Disposes the user store.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
        }

        #endregion

        #region IUserStore implementation

        /// <inheritdoc />
        public Task<string?> GetUserIdAsync(TUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

        /// <inheritdoc />
        public Task<string> GetUserNameAsync(TUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        /// <inheritdoc />
        public Task SetUserNameAsync(TUser user, string userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> GetNormalizedUserNameAsync(TUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        /// <inheritdoc />
        public Task SetNormalizedUserNameAsync(TUser user, string normalizedName, CancellationToken cancellationToken)
        {
            user.UserName = normalizedName.ToLowerInvariant();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<IdentityResult> CreateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            // Make sure we have a valid email address, as we use this for uniqueness.
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                throw new ArgumentException("The user's email address can't be null or empty.", nameof(user));
            }

            // If we use UserName as the user's ID, we must have a UserName as well.
            if (options.UserIdType == UserIdType.UserName && string.IsNullOrWhiteSpace(user.UserName))
            {
                throw new ArgumentException("The user's username can't be null or empty.", nameof(user));
            }

            // Normalize the email and user name.
            user.Email = user.Email.ToLowerInvariant();
            if (user.UserName != null)
            {
                user.UserName = user.UserName.ToLowerInvariant();
            }

			// See if the email address is already taken.
			// We do this using Raven's compare/exchange functionality, which works cluster-wide.
			// https://ravendb.net/docs/article-page/4.1/csharp/client-api/operations/compare-exchange/overview#creating-a-key
			//
            // User creation is done in 3 steps:
            // 1. Reserve the email address, pointing to an empty user ID.
            // 2. Store the user and save it.
            // 3. Update the email address reservation to point to the new user's email.

            // 1. Reserve the email address.
            logger.LogDebug("Creating email reservation for {UserEmail}", user.Email);
			var reserveEmailResult = await CreateEmailReservationAsync(user.Email, string.Empty); // Empty string: Just reserve it for now while we create the user and assign the user's ID.
            if (!reserveEmailResult.Successful)
            {
                logger.LogError("Error creating email reservation for {UserEmail}", user.Email);
                return IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.Email));
            }

            // 2. Store the user in the database and save it.
            try
            {
                await DbSession.StoreAsync(user, CreateUserId(user), cancellationToken);
                await DbSession.SaveChangesAsync(cancellationToken);

                // 3. Update the email reservation to point to the saved user.
                var updateReservationResult = await UpdateEmailReservationAsync(user.Email, user.Id!);
                if (!updateReservationResult.Successful)
                {
                    logger.LogError("Error updating email reservation for {email} to {id}", user.Email, user.Id);
                    throw new Exception("Unable to update the email reservation");
                }
            }
            catch (Exception createUserError)
            {
                // The compare/exchange email reservation is cluster-wide, outside of the session scope.
                // We need to manually roll it back.
                logger.LogError("Error during user creation", createUserError);
                DbSession.Delete(user); // It's possible user is already saved to the database. If so, delete him.
                try
                {
                    await this.DeleteEmailReservation(user.Email);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Caught an exception trying to remove user email reservation for {email} after save failed. An admin must manually delete this compare exchange key.", user.Email);
                }

                return IdentityResult.Failed(new IdentityErrorDescriber().DefaultError());
            }

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken)
        {
			ThrowIfNullDisposedCancelled(user, cancellationToken);

			// Make sure we have a valid email address.
			if (string.IsNullOrWhiteSpace(user.Email))
			{
				throw new ArgumentException("The user's email address can't be null or empty.", nameof(user));
			}
            if (string.IsNullOrWhiteSpace(user.Id))
            {
                throw new ArgumentException("The user can't have a null ID.");
            }

            // If nothing changed we have no work to do
            var changes = DbSession.Advanced.WhatChanged();
            if (changes?[user.Id] == null)
            {
                logger.LogWarning("UserStore UpdateAsync called without any changes to the User {UserId}", user.Id);

                // No changes to this document
                return IdentityResult.Success;
            }

            // Check if their changed their email. If not, the rest of the code is unnecessary
            var emailChange = changes[user.Id].FirstOrDefault(x => string.Equals(x.FieldName, nameof(user.Email)));
            if (emailChange == null)
            {
                logger.LogTrace("User {UserId} did not have modified Email, saving normally", user.Id);

                // Email didn't change, so no reservation to update. Just save the user data
                return IdentityResult.Success;
            }

            // Get the previous value for their email
            var oldEmail = emailChange.FieldOldValue.ToString();
            if (string.Equals(user.UserName, oldEmail, StringComparison.InvariantCultureIgnoreCase))
            {
                logger.LogTrace("Updating username to match modified email for {UserId}", user.Id);

                // The username was set to their email so we should update it.
                user.UserName = user.Email;
            }

            if (string.Equals(user.Email, oldEmail, StringComparison.InvariantCultureIgnoreCase))
            {
                // The property was modified but not in a meaningful way (still the same value, probably just a case change)
                // Doing this after the username update just in case they were changing the casing on their username
                return IdentityResult.Success;
            }

            // If user IDs aren't email-based, we're done.
            if (options.UserIdType != UserIdType.Email)
            {
                return IdentityResult.Success;
            }

            // The user changed their email, and the user ID is email-based.
            // We need to do a few things:
            // 1. Create a cloned user under the new ID and new email reservation.
            // 2. Delete the user under the old email.
            // 3. Delete the old email reservation.

            // 1. Create the cloned user under the new ID and email reservation.
            var oldUserId = user.Id;
            DbSession.Advanced.Evict(user); // Don't track this user; we'll create a new user.
            var createResult = await CreateAsync(user, cancellationToken);
            if (!createResult.Succeeded)
            {
                return createResult;
            }

            // 2. Delete the older user.
            DbSession.Delete(oldUserId);

            // 3. Delete the old email reservation.
            var deleteEmailResult = await DeleteEmailReservation(oldEmail);
            if (!deleteEmailResult.Successful)
            {
                // If this happens, it's not critical: the user still changed their email successfully.
                // They just won't be able to register again with their old email. Log a warning.
                logger.LogWarning("When user changed email from {old} to {new}, there was an error removing the old email reservation. The compare exchange key for the old email should be removed manually.", oldEmail, user.Email);
            }

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Delete the user and save it. We must save it because deleting is a cluster-wide operation.
            // Only if the deletion succeeds will we remove the cluster-wide compare/exchange key.
            this.DbSession.Delete(user);
            await this.DbSession.SaveChangesAsync(cancellationToken);

            // Delete was successful, remove the cluster-wide compare/exchange key.
            var deletionResult = await DeleteEmailReservation(user.Email);
            if (!deletionResult.Successful)
            {
                logger.LogWarning("User was deleted, but there was an error deleting email reservation for {email}. The compare/exchange value for this should be manually deleted.", user.Email);
            }

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public Task<TUser> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            this.DbSession.LoadAsync<TUser>(userId, cancellationToken);

        /// <inheritdoc />
        public Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            // If our IDs are configured by user name, look up directly in the storage engine, skipping indexes.
            if (this.options.UserIdType == UserIdType.UserName)
            {
                var userId = CreateUserIdFromSuffix(normalizedUserName);
                return FindByIdAsync(userId, cancellationToken);
            }

            // Best we can do is an index lookup, which is possibly stale.
            return DbSession.Query<TUser>()
                .SingleOrDefaultAsync(u => u.UserName == normalizedUserName, cancellationToken);
        }

        #endregion

        #region IUserLoginStore implementation

        /// <inheritdoc />
        public Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            if (login == null)
            {
                throw new ArgumentNullException(nameof(login));
            }

            user.Logins.Add(login);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveLoginAsync(TUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            user.Logins.RemoveAll(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.Logins as IList<UserLoginInfo>);
        }

        /// <inheritdoc />
        public Task<TUser> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            return DbSession.Query<TUser>()
                .FirstOrDefaultAsync(u => u.Logins.Any(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey));
        }

        #endregion

        #region IUserClaimStore implementation

        /// <inheritdoc />
        public Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            IList<Claim> result = user.Claims
                .Select(c => new Claim(c.ClaimType, c.ClaimValue))
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.Claims.AddRange(claims.Select(c => new IdentityUserClaim { ClaimType = c.Type, ClaimValue = c.Value }));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task ReplaceClaimAsync(TUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            
            var indexOfClaim = user.Claims.FindIndex(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value);
            if (indexOfClaim != -1)
            {
                user.Claims.RemoveAt(indexOfClaim);
                await this.AddClaimsAsync(user, new[] { newClaim }, cancellationToken);
            }
        }

        /// <inheritdoc />
        public Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.Claims.RemoveAll(identityClaim => claims.Any(c => c.Type == identityClaim.ClaimType && c.Value == identityClaim.ClaimValue));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            if (claim == null)
            {
                throw new ArgumentNullException(nameof(claim));
            }

            var list = await DbSession.Query<TUser>()
                .Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value))
                .ToListAsync();
            return list;
        }

        #endregion

        #region IUserRoleStore implementation

        /// <inheritdoc />
        public async Task AddToRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            // See if we have an IdentityRole with that name.
            var roleId = CreateRoleIdFromName(roleName);
            var existingRoleOrNull = await this.DbSession.LoadAsync<IdentityRole>(roleId, cancellationToken);
            if (existingRoleOrNull == null)
            {
                ThrowIfDisposedOrCancelled(cancellationToken);
                existingRoleOrNull = new TRole
                {
                    Name = roleName.ToLowerInvariant()
                };
                await this.DbSession.StoreAsync(existingRoleOrNull, roleId, cancellationToken);
            }

            // Use the real name (not normalized/uppered/lowered) of the role, as specified by the user.
            var roleRealName = existingRoleOrNull.Name;
            if (!user.Roles.Contains(roleRealName, StringComparer.InvariantCultureIgnoreCase))
            {
                user.GetRolesList().Add(roleRealName);
            }

            if (user.Id != null && !existingRoleOrNull.Users.Contains(user.Id, StringComparer.InvariantCultureIgnoreCase))
            {
                existingRoleOrNull.Users.Add(user.Id);
            }
        }

        /// <inheritdoc />
        public async Task RemoveFromRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.GetRolesList().RemoveAll(r => string.Equals(r, roleName, StringComparison.InvariantCultureIgnoreCase));

            var roleId = RoleStore<TRole>.GetRavenIdFromRoleName(roleName, DbSession.Advanced.DocumentStore);
            var roleOrNull = await DbSession.LoadAsync<IdentityRole>(roleId, cancellationToken);
            if (roleOrNull != null && user.Id != null)
            {
                roleOrNull.Users.Remove(user.Id);
            }
        }

        /// <inheritdoc />
        public Task<IList<string>> GetRolesAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult<IList<string>>(new List<string>(user.Roles));
        }

        /// <inheritdoc />
        public Task<bool> IsInRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(roleName))
            {
                throw new ArgumentNullException(nameof(roleName));
            }

            return Task.FromResult(user.Roles.Contains(roleName, StringComparer.InvariantCultureIgnoreCase));
        }

        /// <inheritdoc />
        public async Task<IList<TUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            if (string.IsNullOrEmpty(roleName))
            {
                throw new ArgumentNullException(nameof(roleName));
            }

            var users = await DbSession.Query<TUser>()
                .Where(u => u.Roles.Contains(roleName, StringComparer.InvariantCultureIgnoreCase))
                .Take(1024)
                .ToListAsync();
            return users;
        }

        #endregion

        #region IUserPasswordStore implementation

        /// <inheritdoc />
        public Task SetPasswordHashAsync(TUser user, string passwordHash, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string?> GetPasswordHashAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.PasswordHash);
        }

        /// <inheritdoc />
        public Task<bool> HasPasswordAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.PasswordHash != null);
        }

        #endregion

        #region IUserSecurityStampStore implementation

        /// <inheritdoc />
        public Task SetSecurityStampAsync(TUser user, string stamp, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string?> GetSecurityStampAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.SecurityStamp);
        }

        #endregion

        #region IUserEmailStore implementation

        /// <inheritdoc />
        public Task SetEmailAsync(TUser user, string email, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            user.Email = email?.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(email));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> GetEmailAsync(TUser user, CancellationToken cancellationToken) => 
            Task.FromResult(user.Email);

        /// <inheritdoc />
        public Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken cancellationToken) => 
            Task.FromResult(user.EmailConfirmed);

        /// <inheritdoc />
        public Task SetEmailConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<TUser> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            var getReq = new GetCompareExchangeValueOperation<string>(GetCompareExchangeKeyFromEmail(normalizedEmail));
            var idResult = await DbSession.Advanced.DocumentStore.Operations.SendAsync(getReq, token: cancellationToken);
            if (idResult == null)
            {
                #pragma warning disable 8603
                return null;
                #pragma warning restore 8603
            }
            return await DbSession.LoadAsync<TUser>(idResult.Value, cancellationToken);
        }

        /// <inheritdoc />
        public Task<string> GetNormalizedEmailAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Email);

        /// <inheritdoc />
        public Task SetNormalizedEmailAsync(TUser user, string normalizedEmail, CancellationToken cancellationToken)
        {
            user.Email = normalizedEmail.ToLowerInvariant(); // I don't like the ALL CAPS default. We're going all lower.
            return Task.CompletedTask;
        }

        #endregion

        #region IUserLockoutStore implementation

        /// <inheritdoc />
        public Task<DateTimeOffset?> GetLockoutEndDateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.LockoutEnd);
        }

        /// <inheritdoc />
        public Task SetLockoutEndDateAsync(TUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> IncrementAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        /// <inheritdoc />
        public Task ResetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> GetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.AccessFailedCount);
        }

        /// <inheritdoc />
        public Task<bool> GetLockoutEnabledAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.LockoutEnabled);
        }

        /// <inheritdoc />
        public Task SetLockoutEnabledAsync(TUser user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        #endregion

        #region IUserTwoFactorStore implementation

        /// <inheritdoc />
        public Task SetTwoFactorEnabledAsync(TUser user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.TwoFactorEnabled = enabled;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> GetTwoFactorEnabledAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.TwoFactorEnabled);
        }

        #endregion

        #region IUserPhoneNumberStore implementation

        /// <inheritdoc />
        public Task SetPhoneNumberAsync(TUser user, string phoneNumber, CancellationToken cancellationToken)
        {
            user.PhoneNumber = phoneNumber;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string?> GetPhoneNumberAsync(TUser user, CancellationToken cancellationToken) => 
            Task.FromResult(user.PhoneNumber);

        /// <inheritdoc />
        public Task<bool> GetPhoneNumberConfirmedAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PhoneNumberConfirmed);

        /// <inheritdoc />
        public Task SetPhoneNumberConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            user.PhoneNumberConfirmed = confirmed;
            return Task.CompletedTask;
        }

        #endregion

        #region IUserAuthenticatorKeyStore implementation
        
        /// <inheritdoc />
        public Task SetAuthenticatorKeyAsync(TUser user, string key, CancellationToken cancellationToken)
        {
            user.TwoFactorAuthenticatorKey = key;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string?> GetAuthenticatorKeyAsync(TUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorAuthenticatorKey);
        }

        #endregion

        #region IUserAuthenticationTokenStore

        /// <inheritdoc />
        public Task SetTokenAsync(TUser user, string loginProvider, string name, string value, CancellationToken cancellationToken)
        {
            var existingToken = user.Tokens.FirstOrDefault(t => t.LoginProvider == loginProvider && t.Name == name);
            if (existingToken != null)
            {
                existingToken.Value = value;
            }
            else
            {
                user.Tokens.Add(new IdentityUserAuthToken
                {
                    LoginProvider = loginProvider,
                    Name = name,
                    Value = value
                });
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
        {
            user.Tokens.RemoveAll(t => t.LoginProvider == loginProvider && t.Name == name);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string?> GetTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
        {
            var tokenOrNull = user.Tokens.FirstOrDefault(t => t.LoginProvider == loginProvider && t.Name == name);
            return Task.FromResult(tokenOrNull?.Value);
        }
        
        /// <inheritdoc />
        public Task ReplaceCodesAsync(TUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
        {
            user.TwoFactorRecoveryCodes = new List<string>(recoveryCodes);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> RedeemCodeAsync(TUser user, string code, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorRecoveryCodes.Remove(code));
        }

        /// <inheritdoc />
        public Task<int> CountCodesAsync(TUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorRecoveryCodes.Count);
        }

        #endregion

        #region IQueryableUserStore

        /// <summary>
        /// Gets the users as an IQueryable.
        /// </summary>
        public IQueryable<TUser> Users => this.DbSession.Query<TUser>();

        #endregion

        /// <summary>
        /// Migrates the data model from a previous version of the RavenDB.Identity framework to the v6 model.
        /// This is necessary if you stored users using an RavenDB.Identity version 5 or ealier.
        /// </summary>
        /// <returns></returns>
        public static void MigrateToV6(IDocumentStore docStore)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var collectionName = docStore.Conventions.FindCollectionName(typeof(IdentityUserByUserName));

            // Step 1: find all the old IdentityByUserName objects.
            var emails = new List<(string userId, string email)>(1000);
            using (var dbSession = docStore.OpenSession())
            {
                var stream = dbSession.Advanced.Stream<IdentityUserByUserName>($"{collectionName}/");
#pragma warning restore CS0618 // Type or member is obsolete
                while (stream.MoveNext())
                {
                    var doc = stream.Current.Document;
                    emails.Add((userId: doc.UserId, email: doc.UserName));
                }
            }

            // Step 2: store each email as a cluster-wide compare/exchange value.
            foreach (var (userId, email) in emails)
            {
                var compareExchangeKey = GetCompareExchangeKeyFromEmail(email);
                var storeOperation = new PutCompareExchangeValueOperation<string>(compareExchangeKey, userId, 0);
                var storeResult = docStore.Operations.Send(storeOperation);
                if (!storeResult.Successful)
                {
                    var exception = new Exception($"Unable to migrate to RavenDB.Identity V6. An error occurred while storing the compare/exchange value. Before running this {nameof(MigrateToV6)} again, please delete all compare/exchange values in Raven that begin with {emailReservationKeyPrefix}.");
                    exception.Data.Add("compareExchangeKey", compareExchangeKey);
                    exception.Data.Add("compareExchangeValue", userId);
                    throw exception;
                }
            }

            // Step 3: remove all IdentityUserByUserName objects.
            var operation = docStore
                .Operations
                .Send(new DeleteByQueryOperation(new Client.Documents.Queries.IndexQuery
                {
                    Query = $"from {collectionName}"
                }));
            operation.WaitForCompletion();
        }

        private IAsyncDocumentSession DbSession
        {
            get
            {
                if (session == null)
                {
                    session = getSessionFunc!();
                }
                return session;
            }
        }

        private void ThrowIfNullDisposedCancelled(TUser user, CancellationToken token)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            token.ThrowIfCancellationRequested();
        }

        private void ThrowIfDisposedOrCancelled(CancellationToken token)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
            token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Create a new email reservation with the given id value
        /// </summary>
        /// <param name="email"></param>
        /// <param name="id"></param>
        /// <returns></returns>
		private Task<CompareExchangeResult<string>> CreateEmailReservationAsync(string email, string id)
		{
			var compareExchangeKey = GetCompareExchangeKeyFromEmail(email);
			var reserveEmailOperation = new PutCompareExchangeValueOperation<string>(compareExchangeKey, id, 0);
			return DbSession.Advanced.DocumentStore.Operations.SendAsync(reserveEmailOperation);
		}

        /// <summary>
        /// Update an existing reservation to point to a new UserId
        /// </summary>
        /// <param name="email"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        private async Task<CompareExchangeResult<string>> UpdateEmailReservationAsync(string email, string id)
        {
            var key = GetCompareExchangeKeyFromEmail(email);
            var store = DbSession.Advanced.DocumentStore;

            var readResult = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null)
            {
                logger.LogError("Failed to get current index for {EmailReservation} to update it to {ReservedFor}",
                    key,
                    id);
                return new CompareExchangeResult<string>() { Successful = false };
            }

            var updateEmailUserIdOperation = new PutCompareExchangeValueOperation<string>(key, id, readResult.Index);
            return await store.Operations.SendAsync(updateEmailUserIdOperation);
        }

		private async Task<CompareExchangeResult<string>> DeleteEmailReservation(string email)
        {
            var key = GetCompareExchangeKeyFromEmail(email);
            var store = DbSession.Advanced.DocumentStore;

            var readResult = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null)
            {
                logger.LogError("Failed to get current index for {EmailReservation} to delete it", key);
                return new CompareExchangeResult<string>() { Successful = false };
            }

            var deleteEmailOperation = new DeleteCompareExchangeValueOperation<string>(key, readResult.Index);
            return await DbSession.Advanced.DocumentStore.Operations.SendAsync(deleteEmailOperation);
        }

        private static string GetCompareExchangeKeyFromEmail(string email)
        {
            return emailReservationKeyPrefix + email.ToLowerInvariant();
        }

		private string CreateUserId(TUser user) 
		{
            var userIdPart = options.UserIdType switch
            {
                UserIdType.Email => user.Email,
                UserIdType.UserName => user.UserName,
                _ => string.Empty
            };
            return CreateUserIdFromSuffix(userIdPart);
		}

        private string CreateUserIdFromSuffix(string suffix)
        {
            var conventions = DbSession.Advanced.DocumentStore.Conventions;
            var entityName = conventions.GetCollectionName(typeof(TUser));
            var prefix = conventions.TransformTypeCollectionNameToDocumentIdPrefix(entityName);
            var separator = conventions.IdentityPartsSeparator;
            return $"{prefix}{separator}{suffix.ToLowerInvariant()}";
        }

        private string CreateRoleIdFromName(string roleName)
        {
            var roleCollectionName = DbSession.Advanced.DocumentStore.Conventions.GetCollectionName(typeof(TRole));
            var prefix = DbSession.Advanced.DocumentStore.Conventions.TransformTypeCollectionNameToDocumentIdPrefix(roleCollectionName);
            var identityPartSeperator = DbSession.Advanced.DocumentStore.Conventions.IdentityPartsSeparator;
            var roleNameLowered = roleName.ToLowerInvariant();
            return prefix + identityPartSeperator + roleNameLowered;
        }
    }
}
