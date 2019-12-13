# <img src="https://github.com/JudahGabriel/RavenDB.Identity/blob/master/RavenDB.Identity/nuget-icon.png?raw=true" width="50px" height="50px" /> RavenDB.Identity
The simple and easy Identity provider for RavenDB and ASP.NET Core. Use Raven to store your users and roles.

## Instructions ##

***Important:** Upgrading from a previous version of RavenDB.Identity? See <a href="#updating-from-old-version">Updating From Old Version</a> for steps to migrate to the latest RavenDB.Identity.*

1. Add an [AppUser class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Models/AppUser.cs) that derives from Raven.Identity.IdentityUser:
```csharp
public class AppUser : Raven.Identity.IdentityUser
{
    /// <summary>
    /// A user's full name.
    /// </summary>
    public string FullName { get; set; }
}
```

2. In appsettings.json, configure your connection to Raven:

```json
"RavenSettings": {
    "Urls": [
        "http://live-test.ravendb.net"
    ],
    "DatabaseName": "Raven.Identity.Sample.RazorPages",
    "CertFilePath": "",
    "CertPassword": ""
},
```

3. In [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Startup.cs), wire it all up:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Grab our RavenSettings object from appsettings.json.
    services.Configure<RavenSettings>(Configuration.GetSection("RavenSettings"));
    
    ...
    
    // Add RavenDB and identity.
    services
        .AddRavenDbDocStore() // Create an IDocumentStore singleton from the RavenSettings.
        .AddRavenDbAsyncSession() // Create a RavenDB IAsyncDocumentSession for each request. You're responsible for calling .SaveChanges after each request.
        .AddIdentity<AppUser, IdentityRole>() // Adds an identity system to ASP.NET Core
        .AddRavenDbIdentityStores<AppUser>(); // Use RavenDB as the store for identity users and roles.
    ...
}

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    ...
    // Instruct ASP.NET Core to use authentication and authorization.
    app.UseAuthentication();
    app.UseAuthorization();
    ...
}
```

4. In your controller actions, [call .SaveChangesAsync() when you're done making changes](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Filters/RavenSaveChangesAsyncFilter.cs#L35). Typically this is done via a [RavenController base class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Controllers/RavenController.cs) for MVC/WebAPI projects or via a [page filter](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Filters/RavenSaveChangesAsyncFilter.cs) for Razor Pages projects.

## Modifying RavenDB conventions

Need to modify RavenDB conventions? You can use the `services.AddRavenDbDocStore(options)` overload:

```csharp
services.AddRavenDbDocStore(options =>
{
    // Maybe we want to change the identity parts separator.
    options.BeforeInitializeDocStore = docStore => docStore.Conventions.IdentityPartsSeparator = "-";
})
```

## <a id="updating-from-old-version">Updating From Old Version of RavenDB.Identity</a>

Using an old version of RavenDB.Identity and want to upgrade to the latest? You need to call the `MigrateToV6` method:

```csharp
// Update our existing users to the latest RavenDB.Identity v6.
// This is necessary only if you stored users with a previous version of RavenDB.Identity.
// Failure to call this method will result in existing users not being able to login.
// This method can take several minutes if you have thousands of users.
UserStore<AppUser>.MigrateToV6(docStore);
```

This upgrade step is necessary because we [updated RavenDB.Identity to use RavenDB's cluster-safe compare/exchange](https://github.com/JudahGabriel/RavenDB.Identity/issues/5) to enforce user name/email uniqueness. 

Previous versions of RavenDB.Identity had relied on `IdentityUserByUserName` IDs to enforce uniqueness, but this isn't guaranteed to work in a cluster. Calling MigrateToV6 will create compare/exchange values in Raven for each email address, and will remove the now-obsolete IdentityUserByUserNames collection.

## Getting Started and Sample Project

Need help? Checkout the [Razor Pages sample](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Samples/RazorPages) or [MVC sample](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Samples/Mvc) to see it all in action.

## Not using .NET Core?

See our [sister project](https://github.com/JudahGabriel/RavenDB.AspNet.Identity) for a RavenDB Identity Provider for the full .NET Framework.