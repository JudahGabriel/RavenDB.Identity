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
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddRavenDbIdentity<TUser>(this IServiceCollection services, Action<IdentityOptions> setupAction = null)
            where TUser : IdentityUser
        {
            // Add the AspNet identity system to work with our RavenDB identity objects.
            if (setupAction != null)
            {
                services.AddIdentity<TUser, IdentityRole>(setupAction)
                    .AddDefaultTokenProviders();
            }
            else
            {
                services.AddIdentity<TUser, IdentityRole>()
                    .AddDefaultTokenProviders();
            }
            
            services.AddScoped<Microsoft.AspNetCore.Identity.IUserStore<TUser>, UserStore<TUser>>();
            services.AddScoped<Microsoft.AspNetCore.Identity.IRoleStore<IdentityRole>, RoleStore<IdentityRole>>();

            return services;
        }

        /// <summary>
        /// Registers a RavenDB <see cref="IAsyncDocumentSession"/> to be created and disposed on each request.
        /// </summary>
        /// <example>
        ///     <code>
        ///         public void ConfigureServices(IServiceCollection services) 
        ///         {
        ///             services.AddRavenDbAsyncSession(() => myRavenDocStore);
        ///         }
        ///     </code>
        /// </example>
        /// <param name="serviceCollection"> The <see cref="IServiceCollection" /> to add services to. </param>
        /// <param name="dbGetter">The function that gets the database.</param>
        /// <remarks>Based on code from https://github.com/maqduni/AspNetCore.Identity.RavenDb/blob/master/src/Maqduni.AspNetCore.Identity.RavenDb/RavenDbServiceCollectionExtensions.cs</remarks>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddRavenDbAsyncSession(this IServiceCollection serviceCollection, Func<IDocumentStore> dbGetter)
        {
            serviceCollection.Add(new ServiceDescriptor(typeof(IAsyncDocumentSession), p => dbGetter().OpenAsyncSession(), ServiceLifetime.Scoped));
            return serviceCollection;
        }

        /// <summary>
        /// Registers a RavenDB <see cref="IAsyncDocumentSession"/> to be created and disposed on each request.
        /// </summary>
        /// <example>
        ///     <code>
        ///         public void ConfigureServices(IServiceCollection services) 
        ///         {
        ///             services.AddRavenDbAsyncSession(() => myRavenDocStore);
        ///         }
        ///     </code>
        /// </example>
        /// <param name="serviceCollection"> The <see cref="IServiceCollection" /> to add services to. </param>
        /// <param name="db">The RavenDB document store.</param>
        /// <remarks>Based on code from https://github.com/maqduni/AspNetCore.Identity.RavenDb/blob/master/src/Maqduni.AspNetCore.Identity.RavenDb/RavenDbServiceCollectionExtensions.cs</remarks>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddRavenDbAsyncSession(this IServiceCollection serviceCollection, IDocumentStore db)
        {
            serviceCollection.Add(new ServiceDescriptor(typeof(IAsyncDocumentSession), p => db.OpenAsyncSession(), ServiceLifetime.Scoped));
            return serviceCollection;
        }

        /// <summary>
        /// Registers a RavenDB <see cref="IAsyncDocumentSession"/> to be created and disposed on each request. 
        /// This requires for an <see cref="IDocumentStore"/> to be added to dependency injection services.
        /// </summary>
        /// <example>
        ///     <code>
        ///         public void ConfigureServices(IServiceCollection services) 
        ///         {
        ///             services.AddRavenDbAsyncSession();
        ///         }
        ///     </code>
        /// </example>
        /// <param name="serviceCollection"> The <see cref="IServiceCollection" /> to add services to. </param>
        /// <remarks>Based on code from https://github.com/maqduni/AspNetCore.Identity.RavenDb/blob/master/src/Maqduni.AspNetCore.Identity.RavenDb/RavenDbServiceCollectionExtensions.cs</remarks>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddRavenDbAsyncSession(this IServiceCollection serviceCollection)
        {
            serviceCollection.Add(new ServiceDescriptor(typeof(IAsyncDocumentSession), p => p.GetRequiredService<IDocumentStore>().OpenAsyncSession(), ServiceLifetime.Scoped));
            return serviceCollection;
        }
    }
}
