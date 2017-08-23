using Microsoft.Extensions.DependencyInjection;
using Raven.Client;
using Raven.Client.Document;
using System;

namespace RavenDB.Identity
{
    /// <summary>
    /// Extends the <see cref="IServiceCollection"/> so that RavenDB services can be registered through it.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a RavenDB <see cref="DocumentStore"/> as a singleton.
        /// </summary>
        /// <example>
        ///     <code>
        ///         public void ConfigureServices(IServiceCollection services) 
        ///         {
        ///             services.AddRavenDb(Configuration.GetConnectionString("RavenDbConnection")));
        ///         }
        ///     </code>
        /// </example>
        /// <param name="serviceCollection"> The <see cref="IServiceCollection" /> to add services to. </param>
        /// <param name="connectionString"> The connection string to the Raven database instance. </param>
        /// <param name="configureAction">An optional action to configure the <see cref="IDocumentStore" />.</param>
        /// <remarks>Based on code from https://github.com/maqduni/AspNetCore.Identity.RavenDb/blob/master/src/Maqduni.AspNetCore.Identity.RavenDb/RavenDbServiceCollectionExtensions.cs</remarks>
        /// <returns>
        /// The same service collection so that multiple calls can be chained.
        /// </returns>
        public static IServiceCollection AddRavenDb(
            this IServiceCollection serviceCollection,
            string connectionString,
            Action<IDocumentStore> configureAction = null)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("Connection string cannot be null or empty.");
            }

            var documentStore = new DocumentStore();
            configureAction?.Invoke(documentStore);
            
            documentStore.ParseConnectionString(connectionString);
            documentStore.Initialize();

            serviceCollection.AddSingleton<IDocumentStore>(documentStore);

            return serviceCollection;
        }

        /// <summary>
        /// Registers a RavenDB <see cref="IAsyncDocumentSession"/> to be created and disposed on each request.
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
            var docStore = serviceCollection.BuildServiceProvider().GetService<IDocumentStore>();
            if (docStore == null)
            {
                throw new InvalidOperationException($"Please call {nameof(AddRavenDb)} before calling {nameof(AddRavenDbAsyncSession)}.");
            }
            
            serviceCollection.Add(new ServiceDescriptor(typeof(IAsyncDocumentSession), p => docStore.OpenAsyncSession(), ServiceLifetime.Scoped));
            return serviceCollection;
        }

        /// <summary>
        /// Registers a RavenDB as the user store.
        /// </summary>
        /// <typeparam name="TUser">The type of user. This should be a class you created derived from <see cref="IdentityUser"/>.</typeparam>
        /// <param name="services"></param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddRavenDbIdentity<TUser>(this IServiceCollection services)
            where TUser : IdentityUser
        {
            services.AddIdentity<TUser, IdentityRole>(); // Adds the AspNet identity system to work with our RavenDB identity objects.

            services.AddScoped<Microsoft.AspNetCore.Identity.IUserStore<TUser>, UserStore<TUser>>();
            services.AddScoped<Microsoft.AspNetCore.Identity.IRoleStore<IdentityRole>, RoleStore<IdentityRole>>();
            return services;
        }
    }
}
