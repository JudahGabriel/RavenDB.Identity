namespace Raven.Identity
{
    /// <summary>
    /// Options for Raven Identity storage.
    /// </summary>
    public class RavenIdentityOptions
    {
        /// <summary>
        /// How IDs will be generated for users. Defaults to <see cref="UserIdType.Email"/>.
        /// </summary>
        /// <remarks>
        /// If you change this after users are already in your database, you'll need to migrate those users to use the new ID.
        /// </remarks>
        public UserIdType UserIdType { get; set; } = UserIdType.Email;
    }
}