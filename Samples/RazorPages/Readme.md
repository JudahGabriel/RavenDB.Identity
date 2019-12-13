# RavenDB.Identity Sample

This is a Razor Pages sample that shows how to use Raven.Identity.

There are four areas of interest:
 1. [appsettings.json](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/appsettings.json) - where we configure our connection to Raven.
 2. [AppUser.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Models/AppUser.cs) - our user class containing any user data like FirstName and LastName.
 3. [RavenSaveChangesAsyncFilter.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Filters/RavenSaveChangesAsyncFilter.cs) - where we save changes to Raven after actions finish executing. This makes sense for a Razor Pages project. For an MVC or Web API project, use a RavenController base class instead.
 4. [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Startup.cs) - where we wire up everything.

More details below.

## 1. appsettings.json - connection to Raven

Our [appsettings.json file](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/appsettings.json) defines our connection to Raven. This is done using the [RavenDB.DependencyInjection](https://github.com/JudahGabriel/RavenDB.DependencyInjection/) package.

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

## 2. AppUser.cs - user class

We create our own [AppUser class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Models/AppUser.cs) to hold user data:

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

While we could call `.SaveChangesAsync()` in the code-behind of every Razor page, that is tedious and error prone. Instead, we create a Razor action filter to save changes, [RaveSaveChangesAsyncFilter.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Filters/RavenSaveChangesAsyncFilter.cs):

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

## 4. Startup.cs, wiring it all together

In [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Startup.cs), we wire up all of the above steps:

```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Configure Raven Identity in just a few lines of code:
    services
        .AddRavenDbDocStore() // 1. Configures Raven connection using the settings in appsettings.json.
        .AddRavenDbAsyncSession(); // 2. Add a scoped IAsyncDocumentSession. For the sync version, use .AddRavenSession() instead.

    // 3. Add our RavenDB.Identity provider.
    var identityBuilder = services
        .AddDefaultIdentity<AppUser>()
        .AddRavenDbIdentityStores<AppUser>();

	...
}

public void Configure(IApplicationBuilder app, IHostingEnvironment env)
{
    ...
    // Instruct AspNetCore to use authentication and authorization.
    app.UseAuthentication();
    app.UseAuthorization();
    ...
}

```