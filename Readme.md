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

3a. In [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Startup.cs), wire it all up. Note that this approach will use the email address as part of the User Id, such that changing their email will change their User Id as well:

```csharp
public void ConfigureServices(IServiceCollection services)
{    
    // Add RavenDB and identity.
    services
        .AddRavenDbDocStore() // Create an IDocumentStore singleton from the RavenSettings.
        .AddRavenDbAsyncSession() // Create a RavenDB IAsyncDocumentSession for each request. You're responsible for calling .SaveChanges after each request.
        .AddIdentity<AppUser, IdentityRole>() // Adds an identity system to ASP.NET Core
        .AddRavenDbIdentityStores<AppUser, IdentityRole>(); // Use RavenDB as the store for identity users and roles. Specify your app user type here, and your role type. If you don't have a role type, use Raven.Identity.IdentityRole.
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

## Changing how user IDs are generated

Previous versions of RavenDB.Identity used email-based user IDs, e.g. `"AppUser/johndoe@mail.com". Newer versions use Raven's default behavior, typically `"AppUsers/1-A"`. To change how Raven controls IDs for your users, see Raven's [Global Identifier Generation Conventions](https://ravendb.net/docs/article-page/4.2/csharp/client-api/configuration/identifier-generation/global).

If you have old users in your database using the email-based ID convention, no problem, Raven.Identity will still work with the old users. If you want consistent IDs across all your users, you can migrate existing users to a new ID generation scheme using the `ChangeUserIdType` migration:

```csharp
// Important: backup your database before running this migration.
var newIdType = UserIdType.ServerGenerated; // Or whatever ID type you prefer.
var migration = new Raven.Identity.Migrations.ChangeUserIdType(docStore, newIdType);
migration.Migrate<AppUser>(); // AppUser, or whatever you user type is.
```

Regardless of how IDs are generated, uniqueness is based on email address. You can't have 2 users in your database with the same email address.

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

Using RavenDB.Identity v5 or earlier and want to upgrade to the latest? You need to run the `CompareExchangeUniqueness` migration:

```csharp
// Important: backup your database before running this migration.
var migration = new Raven.Identity.Migrations.CompareExchangeUniqueness(docStore);
migration.Migrate();
```

This upgrade step is necessary because we [updated RavenDB.Identity to use RavenDB's cluster-safe compare/exchange](https://github.com/JudahGabriel/RavenDB.Identity/issues/5) to enforce email-based uniqueness. 

Previous versions of RavenDB.Identity had relied on `IdentityUserByUserName` IDs to enforce uniqueness, but this isn't guaranteed to work in a cluster. Doing this migration will create compare/exchange values in Raven for each user email address, and will remove the now-obsolete `IdentityUserByUserNames` collection.

## Getting Started and Sample Project

Need help? Checkout the our samples to see how to use it:

- [Razor Pages](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Samples/RazorPages) 
- [MVC](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Samples/Mvc)

## Not using .NET Core?

See our [sister project](https://github.com/JudahGabriel/RavenDB.AspNet.Identity) for a RavenDB Identity Provider for the full .NET Framework.
