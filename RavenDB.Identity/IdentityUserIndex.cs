using System.Linq;
using Raven.Client.Documents.Indexes;

namespace Raven.Identity
{
    /// <summary>
    /// Index to user when querying users.
    /// </summary>
    public class IdentityUserIndex<TUser> : AbstractIndexCreationTask<TUser, IdentityUserIndex<TUser>.Result> where TUser : IdentityUser
    {
        public class Result
        {
            public string UserName { get; set; }
            public string Email { get; set; }
            public string[] LoginProviderIdentifiers { get; set; }
            public string[] Roles { get; set; }
        }

        /// <summary>
        /// Creates the map.
        /// </summary>
        public IdentityUserIndex()
        {
            Map = users => from user in users
                select new Result
                {
                    UserName = user.UserName,
                    Email = user.Email,
                    LoginProviderIdentifiers = user.Logins.Select(x => x.LoginProvider + "|" + x.ProviderKey).ToArray(),
                    Roles = user.Roles.ToArray()
                };
        }

        /// <inheritdoc />
        public override string IndexName => "IdentityUserIndex";
    }
}