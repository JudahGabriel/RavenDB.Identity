# ![RavenDB logo](https://github.com/JudahGabriel/RavenDB.AspNet.Identity/blob/master/RavenDB.AspNet.Identity/nuget-icon.png?raw=true) RavenDB.AspNetCore.Identity #
An ASP.NET Core Identity provider for RavenDB

## Instructions ##
1. In Startup.cs:

```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Add RavenDB and identity.
	services
		.AddRavenDb(Configuration.GetConnectionString("RavenDbConnection")) // Create a RavenDB DocumentStore singleton.
		.AddRavenDbAsyncSession() // Create a RavenDB IAsyncDocumentSession for each request.
		.AddRavenDbIdentity<AppUser>(); // Use Raven for users and roles. AppUser is a simple DTO to hold our user data. See https://github.com/JudahGabriel/RavenDB.AspNet.Identity/blob/master/Sample.Web.NetCore/Models/AppUser.cs

	...
}
```

2. In your controller actions, call .SaveChanges when you're done making changes. Typically this is done via a [RavenController base class](https://github.com/JudahGabriel/RavenDB.AspNet.Identity/blob/master/Sample.Web.NetCore/Controllers/RavenController.cs).

3. You're done! 

Need help? See the [sample app](https://github.com/JudahGabriel/RavenDB.AspNet.Identity/tree/master/Sample.Web.NetCore)