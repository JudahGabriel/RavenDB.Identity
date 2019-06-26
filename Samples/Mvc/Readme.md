# RavenDB.Identity Sample

This is an AspNetCore MVC sample that shows how to use Raven.Identity.

There are five areas of interest:
 1. [appsettings.json](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/appsettings.json) - where we configure our connection to Raven.
 2. [AppUser.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Models/AppUser.cs) - our user class containing any user data like FirstName and LastName.
 3. [RavenController.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Controllers/RavenController.cs) - where we save changes to Raven after actions finish executing.
 4. [AccountController.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Controllers/AccountController.cs) - where we register users, sign in, change roles.
 5. [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Startup.cs) - where we wire up everything.

More details below.

## 1. appsettings.json - connection to Raven

Our [appsettings.json file](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/appsettings.json) defines our connection to Raven. This is done using the [RavenDB.DependencyInjection](https://github.com/JudahGabriel/RavenDB.DependencyInjection/) package.

```json
"RavenSettings": {
	"Urls": [
		"http://live-test.ravendb.net"
	],
	"DatabaseName": "Raven.Identity.Sample.Mvc",
	"CertFilePath": "",
	"CertPassword": ""
},
```

## 2. AppUser.cs - user class

We create our own [AppUser class](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Models/AppUser.cs) to hold user data:

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

## 3. RavenController

We need to `.SaveChangesAsync()` for anything to persist in Raven. Where should we do this?

While we could call `.SaveChangesAsync()` in every controller action, that is tedious and error prone. Instead, we create a base controller, [RavenController.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Controllers/RavenController.cs):

```csharp
/// <summary>
    /// A controller that calls DbSession.SaveChangesAsync() when an action finishes executing successfully.
    /// </summary>
    public class RavenController : Controller
    {
        public RavenController(IAsyncDocumentSession dbSession)
        {
            this.DbSession = DbSession;
        }

        public IAsyncDocumentSession DbSession { get; private set; }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executedContext = await next.Invoke();
            if (executedContext.Exception == null)
            {
                await DbSession.SaveChangesAsync();   
            }
        }
    }
```

## 4. AccountController

In [AccountController.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/Mvc/Controllers/AccountController.cs), we use the standard AspNetCore identity APIs to do registration, sign in, role change, and more:

```csharp
// Register a new user.
var appUser = new AppUser
{
    Email = model.Email
};
var password = "SuperSecret613$$"
var createUserResult = await this.userManager.CreateAsync(appUser, password);
```

Likewise for sign-in:

```csharp
public async Task<IActionResult> SignIn(SignInModel model)
{
    var signInResult = await this.signInManager.PasswordSignInAsync(model.Email, model.Password, true, false);
    if (signInResult.Succeeded)
    {
        return RedirectToAction("Index", "Home");
    }

	...
}
```

## 5. Start.cs, wiring it all together

In [Startup.cs](https://github.com/JudahGabriel/RavenDB.Identity/blob/master/Samples/RazorPages/Startup.cs), we wire up all of the above steps:

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

Finally, make sure you call `app.UseAuthentication()` inside Configure:
```csharp
public void Configure(IApplicationBuilder app, IHostingEnvironment env)
{
    ...
    app.UseAuthentication();
    ...
}
```