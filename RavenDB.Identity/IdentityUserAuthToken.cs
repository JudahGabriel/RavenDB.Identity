using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Identity
{
    /// <summary>
    /// A authorization token created by a login provider.
    /// </summary>
    public class IdentityUserAuthToken
    {
        /// <summary>
        /// The ID of the <see cref="IdentityUser"/> this auth token is for.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// The login provider.
        /// </summary>
        public string LoginProvider { get; set; } = string.Empty;

        /// <summary>
        /// The name of the token.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The value of the token.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }
}
