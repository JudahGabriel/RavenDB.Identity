using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Raven.Identity
{
    /// <summary>
    /// Base user class for RavenDB Identity. Inherit from this class to add your own properties.
    /// </summary>
    public class IdentityUser
    {
        /// <summary>
        /// The ID of the user.
        /// </summary>
        public virtual string Id { get; set; }

        /// <summary>
        /// The user name. Usually the same as the email.
        /// </summary>
        public virtual string UserName { get; set; }

        /// <summary>
        /// The password hash.
        /// </summary>
        public virtual string PasswordHash { get; set; }

        /// <summary>
        /// The security stamp.
        /// </summary>
        public virtual string SecurityStamp { get; set; }

        /// <summary>
        /// The email of the user.
        /// </summary>
        public virtual string Email { get; set; }

        /// <summary>
        /// The phone number.
        /// </summary>
        public virtual string PhoneNumber { get; set; }

        /// <summary>
        /// Whether the user has confirmed their email address.
        /// </summary>
        public virtual bool EmailConfirmed { get; set; }

        /// <summary>
        /// Whether the user has confirmed their phone.
        /// </summary>
        public virtual bool IsPhoneNumberConfirmed { get; set; }

        /// <summary>
        /// Number of times sign in failed.
        /// </summary>
        public virtual int AccessFailedCount { get; set; }

        /// <summary>
        /// Whether the user is locked out.
        /// </summary>
        public virtual bool LockoutEnabled { get; set; }

        /// <summary>
        /// When the user lock out is over.
        /// </summary>
        public virtual DateTimeOffset? LockoutEndDate { get; set; }

        /// <summary>
        /// Whether 2-factor authentication is enabled.
        /// </summary>
        public virtual bool TwoFactorEnabled { get; set; }

        /// <summary>
        /// The two-factor authenticator key.
        /// </summary>
        public string TwoFactorAuthenticatorKey { get; set; }

        /// <summary>
        /// The roles of the user. To modify the user's roles, use <see cref="UserManager{TUser}.AddToRoleAsync(TUser, string)"/> nad <see cref="UserManager{TUser}.RemoveFromRolesAsync(TUser, IEnumerable{string})"/>.
        /// </summary>
        public virtual IReadOnlyList<string> Roles { get; private set; }

        /// <summary>
        /// The user's claims, for use in claims-based authentication.
        /// </summary>
        public virtual List<IdentityUserClaim> Claims { get; private set; }

        /// <summary>
        /// The logins of the user.
        /// </summary>
        public virtual List<UserLoginInfo> Logins { get; private set; }

        /// <summary>
        /// Creates a new IdentityUser.
        /// </summary>
        public IdentityUser()
        {
            this.Claims = new List<IdentityUserClaim>();
            this.Roles = new List<string>();
            this.Logins = new List<UserLoginInfo>();
        }

        /// <summary>
        /// Gets the mutable roles list. This shouldn't be modified by user code; roles should be changed via UserManager instead.
        /// </summary>
        /// <returns></returns>
        internal List<string> GetRolesList()
        {
            return (List<string>)this.Roles;
        }
    }

    /// <summary>
    /// Defines a user login.
    /// </summary>
    public sealed class IdentityUserLogin
    {
        /// <summary>
        /// The ID of the login.
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// The user ID.
        /// </summary>
        public string UserId { get; set; }
        /// <summary>
        /// The login provider.
        /// </summary>
        public string Provider { get; set; }
        /// <summary>
        /// The login provider key.
        /// </summary>
        public string ProviderKey { get; set; }
    }

    /// <summary>
    /// A login claim.
    /// </summary>
    public class IdentityUserClaim
    {
        /// <summary>
        /// The type of the login claim.
        /// </summary>
        public virtual string ClaimType { get; set; }
        /// <summary>
        /// The login claim value.
        /// </summary>
        public virtual string ClaimValue { get; set; }
    }

    /// <summary>
    /// Entity that aides in helping us load users by a well-known name directly from the RavenDB ACID storage engine, bypassing the eventually consistent RavenDB indexes.
    /// </summary>
    public sealed class IdentityUserByUserName
    {
        /// <summary>
        /// The ID of the user.
        /// </summary>
        public string UserId { get; set; }
        /// <summary>
        /// The user name.
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Creates a new IdentityUserByUserName.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="userName"></param>
        public IdentityUserByUserName(string userId, string userName)
        {
            UserId = userId;
            UserName = userName;
        }
    }
}
