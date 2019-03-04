# RavenDB.Identity Sample

This is a Razor Pages sample that shows how to use Raven.Identity.

There are four areas of interest:
 1. [appsettings.json](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/appsettings.json) - where we configure our connection to Raven.
 2. [AppUser.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Models/AppUser.cs) - our user class containing any user data like FirstName and LastName.
 3. [RavenSaveChangesAsyncFilter.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Filters/RavenSaveChangesAsyncFilter.cs) - where we save changes to Raven after actions finish executing. This makes sense for a Razor Pages project. For an MVC or Web API project, use a RavenController base class instead.
 4. [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Startup.cs) - where we wire up everything.

More details below.

## 1. appsettings.json - connection to Raven

Our [appsettings.json file](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/appsettings.json) defines our connection to Raven. This is done using the [RavenDB.DependencyInjection](https://github.com/JudahGabriel/RavenDB.DependencyInjection/) package.

```json
"RavenSettings": {
	"Urls": [
		"http://live-test.ravendb.net"
	],
	"DatabaseName": "Raven.Identity.Sample",
	"CertFilePath": "",
	"CertPassword": ""
},
```

## 2. AppUser.cs - user class

We create our own [AppUser class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Models/AppUser.cs) to hold user data:

```csharp
public class AppUser : Raven.Identity.IdentityUser
{
    /// <summary>
    /// The full name of the user.
    /// </summary>
    public string FullName { get; set; }
}
```

While this step isn't strictly necessary -- it's possible to skip AppUser and just use the built-in `Raven.Identity.IdentityUser` -- we recommend creating an AppUser class so you can extend your users with app-specific data.

## 3. RavenSaveChangesAsyncFilter

We need to `.SaveChangesAsync()` for anything to persist in Raven. Where should we do this?

While we could call `.SaveChangesAsync()` in the code-behind of every Razor page, that is tedious and error prone. Instead, we create a Razor action filter to save changes, [RaveSaveChangesAsyncFilter.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Filters/RavenSaveChangesAsyncFilter.cs):

```csharp
/// <summary>
/// Razor Pages filter that saves any changes after the action completes.
/// </summary>
public class RavenSaveChangesAsyncFilter : IAsyncPageFilter
{
    private readonly IAsyncDocumentSession dbSession;

    public RavenSaveChangesAsyncFilter(IAsyncDocumentSession dbSession)
    {
        this.dbSession = dbSession;
    }

    public async Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        await Task.CompletedTask;
    }

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var result = await next.Invoke();

        // If there was no exception, and the action wasn't cancelled, save changes.
        if (result.Exception == null && !result.Canceled)
        {
            await this.dbSession.SaveChangesAsync();
        }
    }
}
```

For MVC and Web API projects can use an action filter, or may alternately use a RavenController base class to accomplish the same thing.

## 4. Start.cs, wiring it all together

In [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Sample/Startup.cs), we wire up all of the above steps:

```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Grab our RavenSettings object from appsettings.json.
    services.Configure<RavenSettings>(Configuration.GetSection("RavenSettings"));

	...

	// Add an IDocumentStore singleton, with settings pulled from the RavenSettings.
    services.AddRavenDbDocStore();

    // Add a scoped IAsyncDocumentSession. For the sync version, use .AddRavenSession() instead.
    // Note: Your code is responsible for calling .SaveChangesAsync() on this. This Sample does so via the RavenSaveChangesAsyncFilter.
    services.AddRavenDbAsyncSession();

	// Use Raven for our users
	services.AddRavenDbIdentity<AppUser>();
	
	...

	// Call .SaveChangesAsync() after each action.
	services
		.AddMvc(o => o.Filters.Add<RavenSaveChangesAsyncFilter>())
		.SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
}
```