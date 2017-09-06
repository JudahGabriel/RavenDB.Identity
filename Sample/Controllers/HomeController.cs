using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sample.Models;
using Raven.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Sample.Controllers
{
    public class HomeController : RavenController
    {
        private UserManager<AppUser> userManager;

        public HomeController(IAsyncDocumentSession dbSession, UserManager<AppUser> userManager)
            : base(dbSession)
        {
            this.userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // Do a simple RavenDB query.
            var users = await this.DbSession
                .Query<AppUser>()
                .ToListAsync();
            ViewBag.MessageFromRaven = $"Hi from RavenDB! There are {users.Count} users in the database. (◕‿◕✿)";

            return View();
        }

        // Require that the user be logged in.
        [Authorize]
        public IActionResult Auth()
        {
            return View();
        }

        // Require that the user be in the Admin role.
        [Authorize(Roles = "Admin")]
        public IActionResult AuthAdmin()
        {
            return View();
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
