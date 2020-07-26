using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raven.Identity
{
    /// <summary>
    /// Contains constants and methods that deal with the conventions of RavenDB.Identity.
    /// </summary>
    public static class Conventions
    {
        /// <summary>
        /// The prefix used for compare/exchange values used by RavenDB.Identity to ensure user uniqueness based on email address.
        /// </summary>
        public const string EmailReservationKeyPrefix = "emails/";

        /// <summary>
        /// Gets the compare/exchange key used to store the specified email address.
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public static string CompareExchangeKeyFor(string email)
        {
            return EmailReservationKeyPrefix + email.ToLowerInvariant();
        }

        /// <summary>
        /// Creates a user ID based on the ID type.
        /// </summary>
        /// <typeparam name="TUser">The type of user.</typeparam>
        /// <param name="user">The user to create the ID for.</param>
        /// <param name="idType">The type of the ID to create.</param>
        /// <param name="db">The Raven document store.</param>
        /// <returns>A string ID. If <paramref name="idType"/> is <see cref="UserIdType.ServerGenerated"/>, this will be a partial ID (e.g. "AppUsers/") which will be completed by Raven when the entity is stored.</returns>
        public static string UserIdFor<TUser>(TUser user, UserIdType idType, IDocumentStore db)
            where TUser : IdentityUser
        {
            var userIdPart = idType switch
            {
                UserIdType.Email => user.Email,
                UserIdType.UserName => user.UserName,
                _ => string.Empty
            };
            return UserIdWithSuffix<TUser>(userIdPart, db);
        }

        /// <summary>
        /// Creates a user ID using the Raven collection name of the specified <typeparamref name="TUser"/> and configured identity parts separator.
        /// Typically, this will return a value like "AppUsers/foo", where foo is the specified <paramref name="suffix"/>.
        /// </summary>
        /// <typeparam name="TUser">The type of user.</typeparam>
        /// <param name="suffix">The suffix to append to the ID.</param>
        /// <param name="db">The Raven database. Used for determining the identity parts separator and collection name.</param>
        /// <returns>A user ID generated using the collection name of the <typeparamref name="TUser"/>, the database's configured identity parts separator, and specified suffix, e.g. "AppUsers/foo"</returns>
        public static string UserIdWithSuffix<TUser>(string suffix, IDocumentStore db)
        {
            var entityName = db.Conventions.GetCollectionName(typeof(TUser));
            var prefix = db.Conventions.TransformTypeCollectionNameToDocumentIdPrefix(entityName);
            var separator = db.Conventions.IdentityPartsSeparator;
            return $"{prefix}{separator}{suffix.ToLowerInvariant()}";
        }

        /// <summary>
        /// Creates the ID for the role with the specified name.
        /// </summary>
        /// <typeparam name="TRole">The type of role.</typeparam>
        /// <param name="roleName">The name of the role.</param>
        /// <param name="db">The Raven database. Used for finding the collection name for <typeparamref name="TRole"/>s and the identity parts separator.</param>
        /// <returns>An ID for the role with the specified name.</returns>
        public static string RoleIdFor<TRole>(string roleName, IDocumentStore db)
            where TRole : IdentityRole
        {
            var roleCollectionName = db.Conventions.GetCollectionName(typeof(TRole));
            var prefix = db.Conventions.TransformTypeCollectionNameToDocumentIdPrefix(roleCollectionName);
            var identityPartSeperator = db.Conventions.IdentityPartsSeparator;
            var roleNameLowered = roleName.ToLowerInvariant();
            return prefix + identityPartSeperator + roleNameLowered;
        }
    }
}
