using Raven.Client.Documents.Session;

namespace Raven.Identity
{
    /// <summary>
    /// Options for initializing RavenDB.Identity.
    /// </summary>
    public class RavenDbIdentityOptions
    {
        /// <summary>
        /// Whether to use static indexes, defaults to false.
        /// </summary>
        /// <remarks>
        /// Indexes need to be deployed to server in order for static index queries to work.
        /// </remarks>
        /// <seealso cref="IdentityUserIndex{TUser}"/>
    	public bool UseStaticIndexes { get; set; }

        /// <summary>
        ///   If set, changes detected in <see cref="RoleStore{TRole}" /> and <see cref="UserStore{TUser,TRole}"/>
        ///   will be saved to Raven immediately (by calling <see cref="IAsyncDocumentSession.SaveChangesAsync"/>).
        ///   Leave false (the default) if you've implemented the save changes call in middleware. 
        /// </summary>
        public bool AutoSaveChanges { get; set; }
    }
}