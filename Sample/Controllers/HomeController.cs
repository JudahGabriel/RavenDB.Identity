using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Raven.Client;
using Sample.Web.NetCore.Models;

namespace Sample.Web.NetCore.Controllers
{
    public class HomeController : RavenController
    {
        public HomeController(IAsyncDocumentSession dbSession)
            : base(dbSession)
        {
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
