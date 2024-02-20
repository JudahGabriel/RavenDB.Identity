using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
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
using Raven.Client.Documents.Linq;

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
        private readonly ILogger logger;
        private readonly IOptions<RavenDbIdentityOptions> options;

        /// <summary>
        /// Creates a new user store that uses the Raven document session returned from the specified session fetcher.
        /// </summary>
        /// <param name="getSession">The function that gets the Raven document session.</param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public UserStore(Func<IAsyncDocumentSession> getSession, ILogger<UserStore<TUser, TRole>> logger, IOptions<RavenDbIdentityOptions> options)
        {
            this.getSessionFunc = getSession;
            this.logger = logger;
            this.options = options;
        }

        /// <summary>
        /// Creates a new user store that uses the specified Raven document session.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public UserStore(IAsyncDocumentSession session, ILogger<UserStore<TUser, TRole>> logger, IOptions<RavenDbIdentityOptions> options)
        {
            this.session = session;
            this.logger = logger;
            this.options = options;
        }

        #region IDisposable implementation

        /// <summary>
        /// Disposes the user store.
        /// </summary>
        public virtual void Dispose()
        {
            _disposed = true;
        }

        #endregion

	    #region AutoSaveChanges implementation

	    private async Task SaveChangesAsync()
	    {
	      if (options.Value.AutoSaveChanges)
	      {
	        await DbSession.SaveChangesAsync();
	      }
	    }

	    #endregion
        #region IUserStore implementation

        /// <inheritdoc />
        public virtual Task<string?> GetUserIdAsync(TUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

        /// <inheritdoc />
        public virtual Task<string> GetUserNameAsync(TUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        /// <inheritdoc />
        public virtual Task SetUserNameAsync(TUser user, string userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string> GetNormalizedUserNameAsync(TUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        /// <inheritdoc />
        public virtual Task SetNormalizedUserNameAsync(TUser user, string normalizedName, CancellationToken cancellationToken)
        {
            user.UserName = normalizedName.ToLowerInvariant();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual async Task<IdentityResult> CreateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            // Make sure we have a valid email address, as we use this for uniqueness.
            var email = user.Email?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("The user's email address can't be null or empty.", nameof(user));
            }

            // Normalize the email and user name.
            user.Email = email;
            user.UserName = user.UserName?.ToLowerInvariant() ?? email ?? string.Empty;

			// See if the email address is already taken.
			// We do this using Raven's compare/exchange functionality, which works cluster-wide.
			// https://ravendb.net/docs/article-page/4.1/csharp/client-api/operations/compare-exchange/overview#creating-a-key
			//
            // User creation is done in 3 steps:
            // 1. Reserve the email address, pointing to an empty user ID.
            // 2. Store the user and save it.
            // 3. Update the email address reservation to point to the new user's email.

            // 1. Reserve the email address.
            logger.LogDebug("Creating email reservation for {UserEmail}", email);
			var reserveEmailResult = await CreateEmailReservationAsync(email!, string.Empty); // Empty string: Just reserve it for now while we create the user and assign the user's ID.
            if (!reserveEmailResult.Successful)
            {
                logger.LogError("Error creating email reservation for {email}", email);
                return IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(email));
            }

            // 2. Store the user in the database and save it.
            try
            {
                await DbSession.StoreAsync(user, cancellationToken);
                await DbSession.SaveChangesAsync(cancellationToken);

                // 3. Update the email reservation to point to the saved user.
                var updateReservationResult = await UpdateEmailReservationAsync(email!, user.Id!);
                if (!updateReservationResult.Successful)
                {
                    logger.LogError("Error updating email reservation for {email} to {id}", email, user.Id);
                    throw new Exception("Unable to update the email reservation");
                }
            }
            catch (Exception createUserError)
            {
                // The compare/exchange email reservation is cluster-wide, outside of the session scope.
                // We need to manually roll it back.
                logger.LogError(createUserError, "Error during user creation");
                DbSession.Delete(user); // It's possible user is already saved to the database. If so, delete him.
                try
                {
                    await this.DeleteEmailReservation(user.Email!);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Caught an exception trying to remove user email reservation for {email} after save failed. An admin must manually delete the compare exchange key {compareExchangeKey}.", user.Email, Conventions.CompareExchangeKeyFor(user.Email!));
                }

                return IdentityResult.Failed(new IdentityErrorDescriber().DefaultError());
            }

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public virtual async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken)
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
            var hasUserChanged = changes.TryGetValue(user.Id, out var userChange);
            if (!hasUserChanged || userChange == null)
            {
                logger.LogWarning("UserStore UpdateAsync called without any changes to the User {UserId}", user.Id);

                // No changes to this document
                return IdentityResult.Success;
            }

            // Check if their changed their email. If not, the rest of the code is unnecessary
            var emailChange = userChange.FirstOrDefault(x => string.Equals(x.FieldName, nameof(user.Email)));
            if (emailChange == null)
            {
                logger.LogTrace("User {UserId} did not have modified Email, saving normally", user.Id);

                // Email didn't change, so no reservation to update. Just save the user data
                await SaveChangesAsync();
                return IdentityResult.Success;
            }

            // If the user changed their email, we need to update the email compare/exchange reservation.

            // Get the previous value for their email
            var oldEmail = emailChange.FieldOldValue.ToString() ?? string.Empty;
            if (string.Equals(user.UserName, oldEmail, StringComparison.InvariantCultureIgnoreCase))
            {
                logger.LogTrace("Updating username to match modified email for {UserId}", user.Id);

                // The username was set to their email so we should update user name as well.
                user.UserName = user.Email;
            }

            // See if the email change was only due to case sensitivity.
            if (string.Equals(user.Email, oldEmail, StringComparison.InvariantCultureIgnoreCase))
            {
        		// Save the other user data.
                await SaveChangesAsync();
                return IdentityResult.Success;
            }

            // Create the new email reservation.
            var emailReservation = await CreateEmailReservationAsync(user.Email, user.Id);
            if (!emailReservation.Successful)
            {
                DbSession.Advanced.IgnoreChangesFor(user);
                return IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.Email));
            }

            await TryRemoveMigratedEmailReservation(oldEmail, user.Email);

            await SaveChangesAsync();
            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public virtual async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken)
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
        public virtual Task<TUser> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            this.DbSession.LoadAsync<TUser>(userId, cancellationToken);

        /// <inheritdoc />
        public virtual  Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            return UserQuery()
                .SingleOrDefaultAsync(u => u.UserName == normalizedUserName, cancellationToken);
        }

        #endregion

        #region IUserLoginStore implementation

        /// <inheritdoc />
        public virtual Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken cancellationToken)
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
        public virtual Task RemoveLoginAsync(TUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            user.Logins.RemoveAll(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.Logins as IList<UserLoginInfo>);
        }

        /// <inheritdoc />
        public virtual Task<TUser> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            if (options.Value.UseStaticIndexes)
            {
                // index has a bit different structure
                var key = loginProvider + "|" + providerKey;
                return DbSession.Query<IdentityUserIndex<TUser>.Result, IdentityUserIndex<TUser>>()
                    .Where(u => u.LoginProviderIdentifiers != null && u.LoginProviderIdentifiers.Contains(key))
                    .As<TUser>()
                    .FirstOrDefaultAsync(cancellationToken);
            }

            return DbSession.Query<TUser>()
                .FirstOrDefaultAsync(u => u.Logins.Any(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey), cancellationToken);
        }

        #endregion

        #region IUserClaimStore implementation

        /// <inheritdoc />
        public virtual Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            IList<Claim> result = user.Claims
                .Select(c => new Claim(c.ClaimType, c.ClaimValue))
                .ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public virtual Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.Claims.AddRange(claims.Select(c => new IdentityUserClaim { ClaimType = c.Type, ClaimValue = c.Value }));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual async Task ReplaceClaimAsync(TUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
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
        public virtual Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.Claims.RemoveAll(identityClaim => claims.Any(c => c.Type == identityClaim.ClaimType && c.Value == identityClaim.ClaimValue));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            if (claim == null)
            {
                throw new ArgumentNullException(nameof(claim));
            }

            var list = await UserQuery()
                .Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value))
                .ToListAsync(cancellationToken);

            return list;
        }

        #endregion

        #region IUserRoleStore implementation

        /// <inheritdoc />
        public virtual async Task AddToRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            // See if we have an IdentityRole with that name.
            var roleId = Conventions.RoleIdFor<TRole>(roleName, DbSession.Advanced.DocumentStore);
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
        public virtual async Task RemoveFromRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
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
        public virtual Task<IList<string>> GetRolesAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult<IList<string>>(new List<string>(user.Roles));
        }

        /// <inheritdoc />
        public virtual Task<bool> IsInRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(roleName))
            {
                throw new ArgumentNullException(nameof(roleName));
            }

            return Task.FromResult(user.Roles.Contains(roleName, StringComparer.InvariantCultureIgnoreCase));
        }

        /// <inheritdoc />
        public virtual async Task<IList<TUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            if (string.IsNullOrEmpty(roleName))
            {
                throw new ArgumentNullException(nameof(roleName));
            }

            var users = await UserQuery()
                .Where(u => u.Roles.Contains(roleName, StringComparer.InvariantCultureIgnoreCase))
                .ToListAsync(cancellationToken);

            return users;
        }

        #endregion

        #region IUserPasswordStore implementation

        /// <inheritdoc />
        public virtual Task SetPasswordHashAsync(TUser user, string passwordHash, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string?> GetPasswordHashAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.PasswordHash);
        }

        /// <inheritdoc />
        public virtual Task<bool> HasPasswordAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.PasswordHash != null);
        }

        #endregion

        #region IUserSecurityStampStore implementation

        /// <inheritdoc />
        public virtual Task SetSecurityStampAsync(TUser user, string stamp, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string?> GetSecurityStampAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.SecurityStamp);
        }

        #endregion

        #region IUserEmailStore implementation

        /// <inheritdoc />
        public virtual Task SetEmailAsync(TUser user, string email, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            user.Email = email?.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(email));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string> GetEmailAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Email);

        /// <inheritdoc />
        public virtual Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.EmailConfirmed);

        /// <inheritdoc />
        public virtual Task SetEmailConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
#pragma warning disable CS8613 // Nullability of reference types in return type doesn't match implicitly implemented member.
        public virtual async Task<TUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
#pragma warning restore CS8613 // Nullability of reference types in return type doesn't match implicitly implemented member.
        {
            // While we could just do an index query here: DbSession.Query<TUser>().FirstOrDefaultAsync(u => u.Email == normalizedEmail)
            // We decided against this because indexes can be stale.
            // Instead, we're going to go straight to the compare/exchange values and find the user for the email.
            var key = Conventions.CompareExchangeKeyFor(normalizedEmail);
            var readResult = await DbSession.Advanced.DocumentStore.Operations.ForDatabase(((AsyncDocumentSession)DbSession).DatabaseName).SendAsync(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null || string.IsNullOrEmpty(readResult.Value))
            {
                return null;
            }

            return await DbSession.LoadAsync<TUser>(readResult.Value);
        }

        /// <inheritdoc />
        public virtual Task<string> GetNormalizedEmailAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Email);

        /// <inheritdoc />
        public virtual Task SetNormalizedEmailAsync(TUser user, string normalizedEmail, CancellationToken cancellationToken)
        {
            user.Email = normalizedEmail.ToLowerInvariant(); // I don't like the ALL CAPS default. We're going all lower.
            return Task.CompletedTask;
        }

        #endregion

        #region IUserLockoutStore implementation

        /// <inheritdoc />
        public virtual Task<DateTimeOffset?> GetLockoutEndDateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.LockoutEnd);
        }

        /// <inheritdoc />
        public virtual Task SetLockoutEndDateAsync(TUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<int> IncrementAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        /// <inheritdoc />
        public virtual Task ResetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<int> GetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.AccessFailedCount);
        }

        /// <inheritdoc />
        public virtual Task<bool> GetLockoutEnabledAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            return Task.FromResult(user.LockoutEnabled);
        }

        /// <inheritdoc />
        public virtual Task SetLockoutEnabledAsync(TUser user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        #endregion

        #region IUserTwoFactorStore implementation

        /// <inheritdoc />
        public virtual Task SetTwoFactorEnabledAsync(TUser user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.TwoFactorEnabled = enabled;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<bool> GetTwoFactorEnabledAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.TwoFactorEnabled);
        }

        #endregion

        #region IUserPhoneNumberStore implementation

        /// <inheritdoc />
        public virtual Task SetPhoneNumberAsync(TUser user, string phoneNumber, CancellationToken cancellationToken)
        {
            user.PhoneNumber = phoneNumber;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string?> GetPhoneNumberAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PhoneNumber);

        /// <inheritdoc />
        public virtual Task<bool> GetPhoneNumberConfirmedAsync(TUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PhoneNumberConfirmed);

        /// <inheritdoc />
        public virtual Task SetPhoneNumberConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            user.PhoneNumberConfirmed = confirmed;
            return Task.CompletedTask;
        }

        #endregion

        #region IUserAuthenticatorKeyStore implementation

        /// <inheritdoc />
        public virtual Task SetAuthenticatorKeyAsync(TUser user, string key, CancellationToken cancellationToken)
        {
            user.TwoFactorAuthenticatorKey = key;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string?> GetAuthenticatorKeyAsync(TUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorAuthenticatorKey);
        }

        #endregion

        #region IUserAuthenticationTokenStore

        /// <inheritdoc />
        public virtual Task SetTokenAsync(TUser user, string loginProvider, string name, string value, CancellationToken cancellationToken)
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
        public virtual Task RemoveTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
        {
            user.Tokens.RemoveAll(t => t.LoginProvider == loginProvider && t.Name == name);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<string?> GetTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
        {
            var tokenOrNull = user.Tokens.FirstOrDefault(t => t.LoginProvider == loginProvider && t.Name == name);
            return Task.FromResult(tokenOrNull?.Value);
        }

        /// <inheritdoc />
        public virtual Task ReplaceCodesAsync(TUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
        {
            user.TwoFactorRecoveryCodes = new List<string>(recoveryCodes);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task<bool> RedeemCodeAsync(TUser user, string code, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorRecoveryCodes.Remove(code));
        }

        /// <inheritdoc />
        public virtual Task<int> CountCodesAsync(TUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorRecoveryCodes.Count);
        }

        #endregion

        #region IQueryableUserStore

        /// <summary>
        /// Gets the users as an IQueryable.
        /// </summary>
        public virtual IQueryable<TUser> Users => this.DbSession.Query<TUser>();

        #endregion

        /// <summary>
        /// Gets access to current session being used by this store.
        /// </summary>
        protected IAsyncDocumentSession DbSession
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
        protected virtual Task<CompareExchangeResult<string>> CreateEmailReservationAsync(string email, string id)
		{
			var compareExchangeKey = Conventions.CompareExchangeKeyFor(email);
			var reserveEmailOperation = new PutCompareExchangeValueOperation<string>(compareExchangeKey, id, 0);
            return DbSession.Advanced.DocumentStore.Operations.ForDatabase(((AsyncDocumentSession)DbSession).DatabaseName).SendAsync(reserveEmailOperation);
        }

        /// <summary>
        /// Update an existing reservation to point to a new UserId
        /// </summary>
        /// <param name="email"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        protected virtual async Task<CompareExchangeResult<string>> UpdateEmailReservationAsync(string email, string id)
        {
            var key = Conventions.CompareExchangeKeyFor(email);
            var store = DbSession.Advanced.DocumentStore;

            var readResult = await store.Operations.ForDatabase(((AsyncDocumentSession)DbSession).DatabaseName).SendAsync(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null)
            {
                logger.LogError("Failed to get current index for {EmailReservation} to update it to {ReservedFor}", key, id);
                return new CompareExchangeResult<string>() { Successful = false };
            }

            var updateEmailUserIdOperation = new PutCompareExchangeValueOperation<string>(key, id, readResult.Index);
            return await store.Operations.ForDatabase(((AsyncDocumentSession)DbSession).DatabaseName).SendAsync(updateEmailUserIdOperation);
        }

		/// <summary>
		/// Removes email reservation.
		/// </summary>
		/// <param name="email"></param>
		/// <returns></returns>
		protected virtual async Task<CompareExchangeResult<string>> DeleteEmailReservation(string email)
        {
            var key = Conventions.CompareExchangeKeyFor(email);
            var store = DbSession.Advanced.DocumentStore;

            var readResult = await store.Operations.ForDatabase(((AsyncDocumentSession)DbSession).DatabaseName).SendAsync(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null)
            {
                logger.LogError("Failed to get current index for {EmailReservation} to delete it", key);
                return new CompareExchangeResult<string>() { Successful = false };
            }

            var deleteEmailOperation = new DeleteCompareExchangeValueOperation<string>(key, readResult.Index);
            return await DbSession.Advanced.DocumentStore.Operations.ForDatabase(((AsyncDocumentSession)DbSession).DatabaseName).SendAsync(deleteEmailOperation);
        }

        /// <summary>
        /// Attempts to remove an old email reservation as part of a migration from one email to another.
        /// If unsuccessful, a warning will be logged, but no exception will be thrown.
        /// </summary>
        private async Task TryRemoveMigratedEmailReservation(string oldEmail, string newEmail)
        {
            var deleteEmailResult = await DeleteEmailReservation(oldEmail);
            if (!deleteEmailResult.Successful)
            {
                // If this happens, it's not critical: the user still changed their email successfully.
                // They just won't be able to register again with their old email. Log a warning.
                logger.LogWarning("When user changed email from {oldEmail} to {newEmail}, there was an error removing the old email reservation. The compare exchange key {compareExchangeChange} should be removed manually by an admin.", oldEmail, newEmail, Conventions.CompareExchangeKeyFor(oldEmail));
            }
        }

        /// <summary>
        /// Creates either a static index based query or dynamic query based on options.
        /// </summary>
        /// <returns></returns>
        private IRavenQueryable<TUser> UserQuery()
        {
            return options.Value.UseStaticIndexes
                ? DbSession.Query<TUser, IdentityUserIndex<TUser>>()
                : DbSession.Query<TUser>();
        }
    }
}
