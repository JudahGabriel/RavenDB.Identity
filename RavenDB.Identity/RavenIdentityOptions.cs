namespace Raven.Identity
{
    /// <summary>
    /// Options for Raven Identity storage.
    /// </summary>
    public class RavenIdentityOptions
    {
        /// <summary>
        /// If set to true, this will use a server generated id for the User Id. In this case, changing the user email address
        /// has no effect on the User Id. If set to false, changing the users email address will change their User Id. Default
        /// is false.
        /// </summary>
        /// <remarks>It is critical this value is not changed once users have been stored in RavenDb. Doing so will result
        /// in strange behavior and probably loss of data.</remarks>
        public bool StableUserId { get; set; } = false;
    }
}