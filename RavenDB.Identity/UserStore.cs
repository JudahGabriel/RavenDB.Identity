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
using Microsoft.Extensions.Options;

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
        private IAsyncDocumentSession? _session;
        private readonly bool _stableUserId;

        private const string emailReservationKeyPrefix = "emails/";

        /// <summary>
        /// Creates a new user store that uses the Raven document session returned from the specified session fetcher.
        /// </summary>
        /// <param name="getSession">The function that gets the Raven document session.</param>
        public UserStore(Func<IAsyncDocumentSession> getSession, IOptions<RavenIdentityOptions> options)
        {
            this.getSessionFunc = getSession;
            this._stableUserId = options.Value.StableUserId;
        }

        /// <summary>
        /// Creates a new user store that uses the specified Raven document session.
        /// </summary>
        /// <param name="session"></param>
        public UserStore(IAsyncDocumentSession session, IOptions<RavenIdentityOptions> options)
        {
            this._session = session;
            this._stableUserId = options.Value.StableUserId;
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

            // Make sure we have a valid email address.
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                throw new ArgumentException("The user's email address can't be null or empty.", nameof(user));
            }

            // Make sure user email is stored in a consistent way
            user.Email = user.Email.ToLowerInvariant();

            if (string.IsNullOrEmpty(user.UserName))
            {
                user.UserName = user.Email;
            }

            if (!_stableUserId)
            {
                user.Id = CreateEmailDerivedUserId(user.Email);
            }

            cancellationToken.ThrowIfCancellationRequested();

			// See if the email address is already taken.
			// We do this using Raven's compare/exchange functionality, which works cluster-wide.
			// https://ravendb.net/docs/article-page/4.1/csharp/client-api/operations/compare-exchange/overview#creating-a-key
			//
			// Try to reserve a new user email
			// Note: This operation takes place outside of the session transaction it is a cluster-wide reservation.
            // If we are using a stable id, pass a placeholder value, we will replace it shortly
            var reservationId = _stableUserId ? "-1" : user.Id;
			var reserveEmailResult = await CreateUserKeyReservationAsync(user.Email, reservationId);
            if (!reserveEmailResult.Successful)
            {
                return IdentityResult.Failed(new[]
                {
                    new IdentityError
                    {
                        Code = "DuplicateEmail",
                        Description = $"The email address {user.Email} is already taken."
                    }
                });
            }

            // This model allows us to lookup a user by name in order to get the id
            await DbSession.StoreAsync(user, cancellationToken);

            if (_stableUserId)
            {
                // NOW we can update our email reservation to the server generated User Id
                var updateReservationResult = await UpdateUserKeyReservationAsync(user.Email, DbSession.Advanced.GetDocumentId(user));
                if (!updateReservationResult.Successful)
                {
                    return IdentityResult.Failed(new[]
                    {
                        new IdentityError
                        {
                            Code = "UnknownError",
                            Description = $"The email reservation was not updated successfully."
                        }
                    });
                }
            }

            // Because this a a cluster-wide operation due to compare/exchange tokens,
            // we need to save changes here; if we can't store the user,
            // we need to roll back the email reservation.
            try
            {
                await DbSession.SaveChangesAsync(cancellationToken);
            }
            catch (Exception)
            {
                // The compare/exchange email reservation is cluster-wide, outside of the session scope.
                // We need to manually roll it back.
                await this.DeleteUserKeyReservation(user.Email);
                throw;
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

			cancellationToken.ThrowIfCancellationRequested();

            // If nothing changed we have no work to do
            var changes = DbSession.Advanced.WhatChanged();
            if (changes?[user.Id] == null)
            {
                // No changes to this document
                return IdentityResult.Success;
            }

            // Check if their changed their email. If not, the rest of the code is unnecessary
            var emailChange = changes[user.Id].FirstOrDefault(x =>
                string.Equals(x.FieldName, nameof(user.Email)));
            if (emailChange == null)
            {
                // Email didn't change, so no reservation to update. Just save the user data
                await DbSession.SaveChangesAsync(cancellationToken);
                return IdentityResult.Success;
            }

            // Get the previous value for their email
            var oldEmail = emailChange.FieldOldValue.ToString();

            if (string.Equals(user.UserName, oldEmail, StringComparison.InvariantCultureIgnoreCase))
            {
                // The username was set to their email so we should update it.
                user.UserName = user.Email;
            }

            if (string.Equals(user.Email, oldEmail, StringComparison.InvariantCultureIgnoreCase))
            {
                // The property was modified but not in a meaningful way (still the same value, probably just a case change)
                // Doing this after the username update just in case they were changing the casing on their username
                await DbSession.SaveChangesAsync(cancellationToken);
                return IdentityResult.Success;
            }

            // Email change was more than just casing, we need to update their reservation, possibly more depending on UserId mode
            var oldId = user.Id;
            var newEmail = user.Email;

            if (!_stableUserId)
            {
                // we need to update their User Id
                user.Id = CreateEmailDerivedUserId(user.Email);
            }

            // TODO: I think a failure in these compare/exchange will leave things in a bad state, consider recovery
            // eg: if delete works but create fails, then the email reservation is 'lost'

            // Since their email changed we need to delete their old email reservation
            var compareExchangeResult = await DeleteUserKeyReservation(oldEmail);
            if (!compareExchangeResult.Successful)
            {
                return IdentityResult.Failed(new[]
                {
                    new IdentityError
                    {
                        Code = "ConcurrencyFailure",
                        Description = "Unable to update user email."
                    }
                });
            }

            // Then replace it with a new reservation at the new email address
            compareExchangeResult = await CreateUserKeyReservationAsync(newEmail, user.Id);
            if (!compareExchangeResult.Successful)
            {
                return IdentityResult.Failed(new[]
                {
                    new IdentityError
                    {
                        Code = "ConcurrencyFailure",
                        Description = "Unable to update user email."
                    }
                });
            }

            try
            {
                // We are only updating the same user record
                await DbSession.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // The compare/exchange email reservation is cluster-wide, outside of the session scope.
                // We need to manually roll it back.
                await DeleteUserKeyReservation(user.Email);
                // Make sure that if we rollback the reservation, we use the OLD user Id and not the updated value
                await CreateUserKeyReservationAsync(oldEmail, oldId);
                throw;
            }

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Remove the cluster-wide compare/exchange key.
            var deletionResult = await DeleteUserKeyReservation(user.Email);
            if (!deletionResult.Successful)
            {
                return IdentityResult.Failed(new[]
                {
                    new IdentityError
                    {
                        Code = "ConcurrencyFailure",
                        Description = "Unable to delete user email compare/exchange value"
                    }
                });
            }

            // Delete the user and save it. We must save it because deleting is a cluster-wide operation.
            // Only if the deletion succeeds will we remove the cluster-wide compare/exchange key.
            this.DbSession.Delete(user);
            await this.DbSession.SaveChangesAsync(cancellationToken);

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public Task<TUser> FindByIdAsync(string userId, CancellationToken cancellationToken) => 
            this.DbSession.LoadAsync<TUser>(userId, cancellationToken);

        /// <inheritdoc />
        public Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            DbSession.Query<TUser>()
            .SingleOrDefaultAsync(u => u.UserName == normalizedUserName, token: cancellationToken);

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
            var identityUserCollection = DbSession.Advanced.DocumentStore.Conventions.GetCollectionName(typeof(TRole));
			var prefix = DbSession.Advanced.DocumentStore.Conventions.TransformTypeCollectionNameToDocumentIdPrefix(identityUserCollection);
            var identityPartSeperator = DbSession.Advanced.DocumentStore.Conventions.IdentityPartsSeparator;
            var roleNameLowered = roleName.ToLowerInvariant();
            var roleId = prefix + identityPartSeperator + roleNameLowered;
            var existingRoleOrNull = await this.DbSession.LoadAsync<IdentityRole>(roleId, cancellationToken);
            if (existingRoleOrNull == null)
            {
                ThrowIfDisposedOrCancelled(cancellationToken);
                existingRoleOrNull = new TRole
                {
                    Name = roleNameLowered
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
            user.Email = email.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(email));
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
                if (_session == null)
                {
                    _session = getSessionFunc!();
                }
                return _session;
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
		private Task<CompareExchangeResult<string>> CreateUserKeyReservationAsync(string email, string id)
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
        private async Task<CompareExchangeResult<string>> UpdateUserKeyReservationAsync(string email, string id)
        {
            var key = GetCompareExchangeKeyFromEmail(email);
            var store = DbSession.Advanced.DocumentStore;

            var readResult = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null)
            {
                return new CompareExchangeResult<string>() { Successful = false };
            }

            var updateEmailUserIdOperation = new PutCompareExchangeValueOperation<string>(key, id, readResult.Index);
            return await store.Operations.SendAsync(updateEmailUserIdOperation);
        }

		private Task<CompareExchangeResult<string>> DeleteUserKeyReservation(string email)
        {
            var key = GetCompareExchangeKeyFromEmail(email);
            var store = DbSession.Advanced.DocumentStore;

            var readResult = store.Operations.Send(new GetCompareExchangeValueOperation<string>(key));
            if (readResult == null)
            {
                return Task.FromResult(new CompareExchangeResult<string>() { Successful = false });
            }

            var deleteEmailOperation = new DeleteCompareExchangeValueOperation<string>(key, readResult.Index);
            return DbSession.Advanced.DocumentStore.Operations.SendAsync(deleteEmailOperation);
        }

        private static string GetCompareExchangeKeyFromEmail(string email)
        {
            return emailReservationKeyPrefix + email.ToLowerInvariant();
        }

		private string CreateEmailDerivedUserId(string email)
		{
			var conventions = DbSession.Advanced.DocumentStore.Conventions;
			var entityName = conventions.GetCollectionName(typeof(TUser));
			var prefix = conventions.TransformTypeCollectionNameToDocumentIdPrefix(entityName);
			var separator = conventions.IdentityPartsSeparator;
			return $"{prefix}{separator}{email.ToLowerInvariant()}";
		}
	}
}
