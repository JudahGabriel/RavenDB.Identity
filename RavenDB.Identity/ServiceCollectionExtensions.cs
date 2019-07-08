using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using System;

namespace Raven.Identity
{
    /// <summary>
    /// Extends the <see cref="IServiceCollection"/> so that RavenDB services can be registered through it.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a RavenDB as the user store.
        /// </summary>
        /// <typeparam name="TUser">The type of user. This should be a class you created derived from <see cref="IdentityUser"/>.</typeparam>
        /// <param name="services"></param>
        /// <param name="setupAction">Identity options configuration.</param>
        /// <returns>The identity builder.</returns>
        public static IdentityBuilder AddRavenDbIdentity<TUser>(this IServiceCollection services, Action<IdentityOptions> setupAction = null)
            where TUser : IdentityUser
        {
			return AddRavenDbIdentity<TUser, IdentityRole>(services, setupAction);
        }

		/// <summary>
		/// Registers a RavenDB as the user store.
		/// </summary>
		/// <typeparam name="TUser">The type of user. This should be a class you created derived from <see cref="IdentityUser"/>.</typeparam>
		/// <typeparam name="TRole">The type of role. This should be a class you created derived from <see cref="IdentityRole"/>.</typeparam>
		/// <param name="services"></param>
		/// <param name="setupAction">Identity options configuration.</param>
		/// <returns>The identity builder.</returns>
		public static IdentityBuilder AddRavenDbIdentity<TUser, TRole>(this IServiceCollection services, Action<IdentityOptions> setupAction = null)
			where TUser : IdentityUser
			where TRole : IdentityRole, new()
		{
            // Add the AspNet identity system to work with our RavenDB identity objects.
            IdentityBuilder identityBuilder;
			if (setupAction != null)
			{
				identityBuilder = services.AddIdentity<TUser, TRole>(setupAction)
					.AddDefaultTokenProviders();
			}
			else
			{
				identityBuilder = services.AddIdentity<TUser, TRole>()
					.AddDefaultTokenProviders();
			}

			services.AddScoped<Microsoft.AspNetCore.Identity.IUserStore<TUser>, UserStore<TUser, TRole>>();
			services.AddScoped<Microsoft.AspNetCore.Identity.IRoleStore<TRole>, RoleStore<TRole>>();

			return identityBuilder;
		}
    }
}
