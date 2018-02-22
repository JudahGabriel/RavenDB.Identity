# ![RavenDB logo](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/RavenDB.Identity/nuget-icon.png?raw=true) RavenDB.Identity #
RavenDB identity provider for ASP.NET Core.

The simple and easy Identity provider for RavenDB and ASP.NET Core. Use Raven to store your users and logins. Uses RavenDB 4+

## Instructions ##
1. In Startup.cs:

```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Add RavenDB and identity.
	services
		.AddRavenDbAsyncSession(docStore) // Create a RavenDB IAsyncDocumentSession for each request. docStore is your IDocumentStore instance.
		.AddRavenDbIdentity<AppUser>(); // Use Raven for users and roles. AppUser is your class, a simple DTO to hold user data. See https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Models/AppUser.cs

	...
}
```

2. In your controller actions, call .SaveChanges when you're done making changes. Typically this is done via a [RavenController base class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Controllers/RavenController.cs).

3. You're done! 

Need help? See the [sample app](https://github.com/JudahGabriel/RavenDB.Identity/tree/master/Sample).

Not using .NET Core? See our [sister project](https://github.com/JudahGabriel/RavenDB.AspNet.Identity) for a RavenDB Identity Provider for MVC 5+ and WebAPI 2+ on the full .NET Framework.