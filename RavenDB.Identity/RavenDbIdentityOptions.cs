namespace Raven.Identity
{
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
    }
}