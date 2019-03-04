# <img src="https://github.com/JudahGabriel/RavenDB.Identity/blob/master/RavenDB.Identity/nuget-icon.png?raw=true" width="50px" height="50px" /> RavenDB.Identity
The simple and easy Identity provider for RavenDB and ASP.NET Core. Use Raven to store your users and logins.

## Instructions ##
1. Add an [AppUser class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Models/AppUser.cs) that derives from Raven.Identity.IdentityUser:
```csharp
public class AppUser : Raven.Identity.IdentityUser
{
    /// <summary>
    /// A user's full name.
    /// </summary>
    public string FullName { get; set; }
}
```

2. In Startup.cs:

```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Grab our RavenSettings object from appsettings.json.
    services.Configure<RavenSettings>(Configuration.GetSection("RavenSettings"));

	// Add RavenDB and identity.
	services
		.AddRavenDbDocStore() // Create an IDocumentStore singleton from the RavenSettings.
		.AddRavenDbAsyncSession() // Create a RavenDB IAsyncDocumentSession for each request. docStore is your IDocumentStore instance. You're responsible for calling .SaveChanges after each request.
		.AddRavenDbIdentity<AppUser>(); // Use Raven to manage users and roles.

	...
}
```

3. In your controller actions, call .SaveChanges when you're done making changes. Typically this is done via a RavenController base class for MVC/WebAPI projects, or via an [ActionFilter](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Filters/RavenSaveChangesAsyncFilter.cs) for Razor Pages projects.

Need help? See the [sample app](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Sample) to see it all in action.

Not using .NET Core? See our [sister project](https://github.com/JudahGabriel/RavenDB.AspNet.Identity) for a RavenDB Identity Provider for MVC 5+ and WebAPI 2+ on the full .NET Framework.