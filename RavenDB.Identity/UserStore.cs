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

namespace Raven.Identity
{
    /// <summary>
    /// UserStore for entities in a RavenDB database.
    /// </summary>
    /// <typeparam name="TUser"></typeparam>
    public class UserStore<TUser> : 
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
    {
        private bool _disposed;
        private readonly Func<IAsyncDocumentSession> getSessionFunc;
        private IAsyncDocumentSession _session;

        private const string emailReservationKeyPrefix = "emails/";

        /// <summary>
        /// Creates a new user store that uses the Raven document session returned from the specified session fetcher.
        /// </summary>
        /// <param name="getSession">The function that gets the Raven document session.</param>
        public UserStore(Func<IAsyncDocumentSession> getSession)
        {
            this.getSessionFunc = getSession;
        }

        /// <summary>
        /// Creates a new user store that uses the specified Raven document session.
        /// </summary>
        /// <param name="session"></param>
        public UserStore(IAsyncDocumentSession session)
        {
            this._session = session;
        }

        #region IDispoable implementation

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
        public Task<string> GetUserIdAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.Id);
        }

        /// <inheritdoc />
        public Task<string> GetUserNameAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.UserName);
        }

        /// <inheritdoc />
        public Task SetUserNameAsync(TUser user, string userName, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.UserName = userName;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> GetNormalizedUserNameAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            // Raven string comparison queries are case-insensitive. We can just return the user name.
            return Task.FromResult(user.UserName);
        }

        /// <inheritdoc />
        public Task SetNormalizedUserNameAsync(TUser user, string normalizedName, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

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

            if (string.IsNullOrEmpty(user.Id))
            {
                var conventions = DbSession.Advanced.DocumentStore.Conventions;
                var entityName = conventions.GetCollectionName(typeof(TUser));
                var separator = conventions.IdentityPartsSeparator;
                var id = $"{entityName}{separator}{user.Email}";
                user.Id = id;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // See if the email address is already taken.
            // We do this using Raven's compare/exchange functionality, which works cluster-wide.
            // https://ravendb.net/docs/article-page/4.1/csharp/client-api/operations/compare-exchange/overview#creating-a-key
            //
            // Try to reserve a new user email 
            // Note: This operation takes place outside of the session transaction it is a cluster-wide reservation.
            var compareExchangeKey = GetCompareExchangeKeyFromEmail(user.Email);
            var reserveEmailOperation = new PutCompareExchangeValueOperation<string>(compareExchangeKey, user.Id, 0);
            var reserveEmailResult = await DbSession.Advanced.DocumentStore.Operations.SendAsync(reserveEmailOperation);
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

            // Because this a a cluster-wide operation due to compare/exchange tokens,
            // we need to save changes here; if we can't store the user, 
            // we need to roll back the email reservation.
            try
            {
                await DbSession.SaveChangesAsync();
            }
            catch (Exception)
            {
                // The compare/exchange email reservation is cluster-wide, outside of the session scope. 
                // We need to manually roll it back.
                await this.DeleteUserEmailReservation(user.Email);
                throw;
            }
            
            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            
            return Task.FromResult(IdentityResult.Success);
        }

        /// <inheritdoc />
        public async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Remove the cluster-wide compare/exchange key.
            var deletionResult = await DeleteUserEmailReservation(user.Email);
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
            // Only if the deletion succeeds will we remove the cluseter-wide compare/exchange key.
            this.DbSession.Delete(user);
            await this.DbSession.SaveChangesAsync();

            return IdentityResult.Success;
        }

        /// <inheritdoc />
        public Task<TUser> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);

            return DbSession.LoadAsync<TUser>(userId);
        }

        /// <inheritdoc />
        public async Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);
            if (string.IsNullOrEmpty(normalizedUserName))
            {
                throw new ArgumentNullException(nameof(normalizedUserName));
            }
            
            var compareExchangeKey = GetCompareExchangeKeyFromEmail(normalizedUserName);
            var getEmailReservationOperation = new GetCompareExchangeValueOperation<string>(compareExchangeKey);
            var emailReservationResultOrNull = await DbSession.Advanced.DocumentStore.Operations.SendAsync(getEmailReservationOperation);
            var userId = emailReservationResultOrNull?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return await FindByIdAsync(userId, cancellationToken);
        }

        #endregion

        #region IUserLoginStore implementation

        /// <inheritdoc />
        public async Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            if (login == null)
            {
                throw new ArgumentNullException(nameof(login));
            }

            if (!user.Logins.Any(x => x.LoginProvider == login.LoginProvider && x.ProviderKey == login.ProviderKey))
            {
                user.Logins.Add(login);

                var userLogin = new IdentityUserLogin
                {
                    Id = Util.GetLoginId(login),
                    UserId = user.Id,
                    Provider = login.LoginProvider,
                    ProviderKey = login.ProviderKey
                };
                await DbSession.StoreAsync(userLogin);
            }
        }

        /// <inheritdoc />
        public async Task RemoveLoginAsync(TUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            var login = new UserLoginInfo(loginProvider, providerKey, string.Empty);
            string loginId = Util.GetLoginId(login);
            var loginDoc = await DbSession.LoadAsync<IdentityUserLogin>(loginId);
            if (loginDoc != null)
            {
                DbSession.Delete(loginDoc);
            }

            cancellationToken.ThrowIfCancellationRequested();

            user.Logins.RemoveAll(x => x.LoginProvider == login.LoginProvider && x.ProviderKey == login.ProviderKey);
        }

        /// <inheritdoc />
        public Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.Logins.ToIList());
        }

        /// <inheritdoc />
        public async Task<TUser> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            var login = new UserLoginInfo(loginProvider, providerKey, string.Empty);
            string loginId = Util.GetLoginId(login);
            var loginDoc = await DbSession.LoadAsync<IdentityUserLogin>(loginId);
            if (loginDoc != null)
            {
                return await DbSession.LoadAsync<TUser>(loginDoc.UserId);
            }

            return null;
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

            var roleNameLowered = roleName.ToLower();
            if (!user.Roles.Contains(roleNameLowered, StringComparer.OrdinalIgnoreCase))
            {
                user.GetRolesList().Add(roleNameLowered);
            }

            // See if we have an IdentityRole with that.
            var roleId = "IdentityRoles/" + roleNameLowered;
            var existingRoleOrNull = await this.DbSession.LoadAsync<IdentityRole>(roleId, cancellationToken);
            if (existingRoleOrNull == null)
            {
                ThrowIfDisposedOrCancelled(cancellationToken);
                existingRoleOrNull = new IdentityRole(roleNameLowered);
                await this.DbSession.StoreAsync(existingRoleOrNull, roleId, cancellationToken);
            }

            if (!existingRoleOrNull.Users.Contains(user.Id, StringComparer.OrdinalIgnoreCase))
            {
                existingRoleOrNull.Users.Add(user.Id);
            }
        }

        /// <inheritdoc />
        public async Task RemoveFromRoleAsync(TUser user, string roleName, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.GetRolesList().RemoveAll(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));

            var roleId = "IdentityRoles/" + roleName.ToLower();
            var roleOrNull = await DbSession.LoadAsync<IdentityRole>(roleId, cancellationToken);
            if (roleOrNull != null)
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

            return Task.FromResult(user.Roles.Contains(roleName, StringComparer.OrdinalIgnoreCase));
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
        public Task<string> GetPasswordHashAsync(TUser user, CancellationToken cancellationToken)
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
        public Task<string> GetSecurityStampAsync(TUser user, CancellationToken cancellationToken)
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
            user.Email = email ?? throw new ArgumentNullException(nameof(email));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> GetEmailAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.Email);
        }

        /// <inheritdoc />
        public Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.EmailConfirmed);
        }

        /// <inheritdoc />
        public Task SetEmailConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<TUser> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            ThrowIfDisposedOrCancelled(cancellationToken);

            return DbSession.Query<TUser>()
                .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        }

        /// <inheritdoc />
        public Task<string> GetNormalizedEmailAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            // Raven string comparison queries are case-insensitive. We can just return the user name.
            return Task.FromResult(user.Email);
        }

        /// <inheritdoc />
        public Task SetNormalizedEmailAsync(TUser user, string normalizedEmail, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);
            if (string.IsNullOrEmpty(normalizedEmail))
            {
                throw new ArgumentNullException(nameof(normalizedEmail));
            }

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
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            user.PhoneNumber = phoneNumber;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> GetPhoneNumberAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.PhoneNumber);
        }

        /// <inheritdoc />
        public Task<bool> GetPhoneNumberConfirmedAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

            return Task.FromResult(user.PhoneNumberConfirmed);
        }

        /// <inheritdoc />
        public Task SetPhoneNumberConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            ThrowIfNullDisposedCancelled(user, cancellationToken);

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
        public Task<string> GetAuthenticatorKeyAsync(TUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.TwoFactorAuthenticatorKey);
        }

        #endregion

        #region IUserAuthenticationTokenStore

        /// <inheritdoc />
        public async Task SetTokenAsync(TUser user, string loginProvider, string name, string value, CancellationToken cancellationToken)
        {
            var id = IdentityUserAuthToken.GetWellKnownId(DbSession.Advanced.DocumentStore, user.Id, loginProvider, name);
            ThrowIfDisposedOrCancelled(cancellationToken);

            var existingOrNull = await DbSession.LoadAsync<IdentityUserAuthToken>(id);
            if (existingOrNull == null)
            {
                existingOrNull = new IdentityUserAuthToken
                {
                    Id = id,
                    LoginProvider = loginProvider,
                    Name = name,
                    UserId = user.Id,
                    Value = value
                };
                await DbSession.StoreAsync(existingOrNull);
            }

            existingOrNull.Value = value;
        }

        /// <inheritdoc />
        public Task RemoveTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
        {
            var id = IdentityUserAuthToken.GetWellKnownId(DbSession.Advanced.DocumentStore, user.Id, loginProvider, name);
            DbSession.Delete(id);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<string> GetTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
        {
            var id = IdentityUserAuthToken.GetWellKnownId(DbSession.Advanced.DocumentStore, user.Id, loginProvider, name);
            var tokenOrNull = await DbSession.LoadAsync<IdentityUserAuthToken>(id);
            if (tokenOrNull == null)
            {
                return null;
            }

            return tokenOrNull.Value;
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
            // Step 1: find all the old IdentityByUserName objects.
            var emails = new List<(string userId, string email)>(1000);
            using (var dbSession = docStore.OpenSession())
            {
#pragma warning disable CS0618 // Type or member is obsolete
                var collectionName = store.Conventions.FindCollectionName(typeof(IdentityUserByUserName));
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
                    Query = "from IdentityUserByUserNames"
                }));
            operation.WaitForCompletion();
        }

        private IAsyncDocumentSession DbSession
        {
            get
            {
                if (_session == null)
                {
                    _session = getSessionFunc();
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

        private Task<CompareExchangeResult<string>> DeleteUserEmailReservation(string email)
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
    }
}
