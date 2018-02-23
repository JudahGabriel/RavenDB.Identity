using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Identity
{
    /// <summary>
    /// A two-factor authentication authorization token.
    /// </summary>
    public class IdentityUserAuthToken
    {
        /// <summary>
        /// The ID of the auth token.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The ID of the <see cref="IdentityUser"/> this auth token is for.
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// The login provider.
        /// </summary>
        public string LoginProvider { get; set; }

        /// <summary>
        /// The name of the token.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The value of the token.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Gets the well-known ID for a IdentityuserAuthToken.
        /// </summary>
        /// <param name="db"></param>
        /// <param name="userId"></param>
        /// <param name="loginProvider"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetWellKnownId(IDocumentStore db, string userId, string loginProvider, string name)
        {
            var vals = new[] { userId, loginProvider, name }
                .Select(v => v ?? "[null]");
            var collection = db.Conventions.GetCollectionName(typeof(IdentityUserAuthToken));
            var separator = db.Conventions.IdentityPartsSeparator;
            var valsHash = CalculateHash(string.Join("-", vals));
            return collection + separator + valsHash.ToString();
        }

        private static int CalculateHash(string input)
        {
            // We can't use input.GetHashCode in .NET Core, as it can (and does!) return different values each time the app is run.
            // See https://github.com/dotnet/corefx/issues/19703
            // Instead, we've implemented the following deterministic string hash algorithm: https://stackoverflow.com/a/5155015/536
            unchecked
            {
                int hash = 23;
                foreach (char c in input)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}
