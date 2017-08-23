# ![RavenDB logo](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/RavenDB.Identity/nuget-icon.png?raw=true) RavenDB.Identity #
RavenDB identity provider for ASP.NET Core.

The simple and easy Identity provider for RavenDB and ASP.NET Core. Use Raven to store your users and logins.

(This is an Identity Provider for .NET Core. If you're looking for an Identity Provider for MVC6+ and the full .NET framework, see our [sister project](https://github.com/JudahGabriel/RavenDB.AspNet.Identity).

## Instructions ##
1. In Startup.cs:

```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Add RavenDB and identity.
	services
		.AddRavenDb(Configuration.GetConnectionString("RavenDbConnection")) // Create a RavenDB DocumentStore singleton.
		.AddRavenDbAsyncSession() // Create a RavenDB IAsyncDocumentSession for each request.
		.AddRavenDbIdentity<AppUser>(); // Use Raven for users and roles. AppUser is your class, a simple DTO to hold user data. See https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Models/AppUser.cs

	...
}
```

2. In your controller actions, call .SaveChanges when you're done making changes. Typically this is done via a [RavenController base class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Controllers/RavenController.cs).

3. You're done! 

Need help? See the [sample app](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Sample).