using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Models;
using Raven.DependencyInjection;
using Raven.Identity;
using Raven.Client.Documents;
using Sample.Common;

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
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => false;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            // Grab our RavenSettings object from appsettings.json.
            services.Configure<RavenSettings>(Configuration.GetSection("RavenSettings"));

            // Add an IDocumentStore singleton, with settings pulled from the RavenSettings.
            services.AddRavenDbDocStore();

            // Add a scoped IAsyncDocumentSession. For the sync version, use .AddRavenSession() instead.
            // Note: Your code is responsible for calling .SaveChangesAsync on this. This Sample does so via the RavenSaveChangesAsyncFilter.
            services.AddRavenDbAsyncSession();

            // Add our RavenDB.Identity provider.
            var identityBuilder = services.AddRavenDbIdentity<AppUser>();

            // Optional: some default UI for register/login/password reset/etc.
            identityBuilder.AddDefaultUI(UIFramework.Bootstrap4); 
            
            // Finally, instruct Razor Pages to call dbSession.SaveChangesAsync() when an action completes.
            // For MVC apps, you may instead use a base controller that calls .SaveChangesAsync().
            services.AddMvc(o => o.Filters.Add<RavenSaveChangesAsyncFilter>())
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseAuthentication();

            app.UseMvc();

            // Create our database if it doesn't exist yet.
            app.ApplicationServices.GetRequiredService<IDocumentStore>().EnsureExists();

            // Did you store users prior to RavenDB.Identity v6? If so, you need to call MigrateToV6.
            // Raven.Identity.UserStore<AppUser>.MigrateToV6(app.ApplicationServices.GetRequiredService<IDocumentStore>());
        }
    }
}
