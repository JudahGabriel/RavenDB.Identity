using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Models;
using Sample.Services;
using Raven.Identity;
using Raven.Client.Documents;

namespace Sample
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            // Connect to a Raven server. We're using the public test playground at http://live-test.ravendb.net
            // NOTE: Getting a DatabaseDoesNotExistException? Go to http://live-test.ravendb.net and create a database with the name 'Raven.Identity.Sample'
            var docStore = new DocumentStore
            {
                Urls = new string[] { "http://live-test.ravendb.net" },
                Database = "Raven.Identity.Sample"
            };
            docStore.Initialize();

            // Create the database if it doesn't exist yet.
            try
            {
                using (var dbSession = docStore.OpenSession())
                {
                    dbSession.Query<AppUser>().Take(0).ToList();
                }
            }
            catch (Raven.Client.Exceptions.Database.DatabaseDoesNotExistException)
            {
                docStore.Maintenance.Server.Send(new Raven.Client.ServerWide.Operations.CreateDatabaseOperation(new Raven.Client.ServerWide.DatabaseRecord
                {
                    DatabaseName = "Raven.Identity.Sample"
                }));
            }
            
            // Add RavenDB and identity.
            services
                .AddRavenDbAsyncSession(docStore) // Create a RavenDB IAsyncDocumentSession for each request.
                .AddRavenDbIdentity<AppUser>(); // Use Raven for users and roles.

            // You can change the login path if need be.
            // services.ConfigureApplicationCookie(options => options.LoginPath = "/my/login/path");

            // Add application services.
            services.AddTransient<IEmailSender, EmailSender>();

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseAuthentication();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
