using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Raven.Client.Documents.Session;
using Sample.Mvc.Models;

namespace Sample.Mvc.Controllers
{
    public class AccountController : RavenController
    {
        private readonly SignInManager<AppUser> signInManager;
        private readonly UserManager<AppUser> userManager;
        
        public AccountController(
            IAsyncDocumentSession dbSession, // injected thanks to Startup.cs call to services.AddRavenDbAsyncSession()
            UserManager<AppUser> userManager, // injected thanks to Startup.cs call to services.AddRavenDbIdentity<AppUser>()
            SignInManager<AppUser> signInManager) // injected thanks to Startup.cs call to services.AddRavenDbIdentity<AppUser>()
            : base(dbSession)
        {
            this.userManager = userManager;
            this.signInManager = signInManager;
        }

        [HttpGet]
        public IActionResult SignIn()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SignIn(SignInModel model)
        {
            var signInResult = await this.signInManager.PasswordSignInAsync(model.Email, model.Password, true, false);
            if (signInResult.Succeeded)
            {
                return RedirectToAction("Index", "Home");
            }

            var reason = signInResult.IsLockedOut ? "Your user is locked out" :
                signInResult.IsNotAllowed ? "Your user is not allowed to sign in" :
                signInResult.RequiresTwoFactor ? "2FA is required" :
                "Bad user name or password";
            return RedirectToAction("SignInFailure", new { reason = reason });
        }

        [HttpGet]
        public IActionResult SignInFailure(string reason)
        {
            ViewBag.FailureReason = reason;
            return View();
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterModel model)
        {
            // Create the user.
            var appUser = new AppUser
            {
                Email = model.Email,
                UserName = model.Email
            };
            var createUserResult = await this.userManager.CreateAsync(appUser, model.Password);
            if (!createUserResult.Succeeded)
            {
                var errorString = string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                return RedirectToAction("RegisterFailure", new { reason = errorString });
            }

            // Add him to a role.
            await this.userManager.AddToRoleAsync(appUser, AppUser.ManagerRole);

            // Sign him in and go home.
            await this.signInManager.SignInAsync(appUser, true);
            return this.RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult RegisterFailure(string reason)
        {
            ViewBag.FailureReason = reason;
            return View();
        }

        [HttpGet]
        public IActionResult ChangeRoles()
        {
            return View();
        }

        [Authorize] // Must be logged in to reach this page.
        [HttpPost]
        public async Task<IActionResult> ChangeRoles(ChangeRolesModel model)
        {
            var currentUser = await this.userManager.FindByEmailAsync(User.Identity.Name);
            var currentRoles = await this.userManager.GetRolesAsync(currentUser);

            // Add any new roles.
            var newRoles = model.Roles.Except(currentRoles).ToList();
            await this.userManager.AddToRolesAsync(currentUser, newRoles);

            // Remove any old roles we're no longer in.
            var removedRoles = currentRoles.Except(model.Roles).ToList();
            await this.userManager.RemoveFromRolesAsync(currentUser, removedRoles);
            
            // After we change roles, we need to call SignInAsync before AspNetCore Identity picks up the new roles.
            await this.signInManager.SignInAsync(currentUser, true);

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> SignOut()
        {
            await this.signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}