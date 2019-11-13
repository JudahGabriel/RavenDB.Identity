using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raven.Client.Documents;
using Raven.DependencyInjection;
using Raven.Identity;
using Sample.Mvc.Common;
using Sample.Mvc.Models;
using System.Collections.Generic;

namespace Sample.Mvc
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
                options.Secure = CookieSecurePolicy.Always;
            });

            services
                .AddRavenDbDocStore() // Create our IDocumentStore singleton using the database settings in appsettings.json
                .AddRavenDbAsyncSession() // Create an Raven IAsyncDocumentSession for every request.
                .AddRavenDbIdentity<AppUser>(); // Let Raven store users and roles.

            services.AddControllersWithViews();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            // Required for Raven Identity to work with authorization and authentication.
            app.UseAuthentication();
            app.UseAuthorization();

            // Create the database if it doesn't exist.
            // Also, create our roles if they don't exist. Needed because we're doing some role-based auth in this demo.
            var docStore = app.ApplicationServices.GetRequiredService<IDocumentStore>();
            docStore.EnsureExists();
            docStore.EnsureRolesExist(new List<string> { AppUser.AdminRole, AppUser.ManagerRole });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
