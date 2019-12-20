using System;
using System.Collections.Generic;
using System.Text;

namespace Raven.Identity
{
    /// <summary>
    /// The default implementation of <see cref="IdentityRole"/> which uses a string as the primary key.
    /// </summary>
    public class IdentityRole : IdentityRole<IdentityRoleClaim>
    {
        /// <summary>
        /// Creates a new IdentityRole.
        /// </summary>
        public IdentityRole()
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="IdentityRole"/>.
        /// </summary>
        /// <param name="roleName">The role name.</param>
        public IdentityRole(string roleName)
        {
            Name = roleName;
        }
    }

    /// <summary>
    /// Represents a role in the identity system
    /// </summary>
    /// <typeparam name="TRoleClaim">The type used for role claims.</typeparam>
    public class IdentityRole<TRoleClaim>
        where TRoleClaim : IdentityRoleClaim
    {
        /// <summary>
        /// Initializes a new instance of <see cref="IdentityRole"/>.
        /// </summary>
        public IdentityRole()
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="IdentityRole"/>.
        /// </summary>
        /// <param name="roleName">The role name.</param>
        public IdentityRole(string roleName)
        {
            Name = roleName;
        }

        /// <summary>
        /// Navigation property for the users in this role.
        /// </summary>
        public virtual ICollection<string> Users { get; } = new List<string>();

        /// <summary>
        /// Navigation property for claims in this role.
        /// </summary>
        public virtual ICollection<TRoleClaim> Claims { get; } = new List<TRoleClaim>();

        /// <summary>
        /// Gets or sets the primary key for this role.
        /// </summary>
        public virtual string? Id { get; set; }

        /// <summary>
        /// Gets or sets the name for this role.
        /// </summary>
        public virtual string Name { get; set; } = string.Empty;

        /// <summary>
        /// Returns the name of the role.
        /// </summary>
        /// <returns>The name of the role.</returns>
        public override string ToString()
        {
            return Name;
        }
    }
}
