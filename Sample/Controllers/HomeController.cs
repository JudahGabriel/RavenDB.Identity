using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Sample.Models;
using System.Linq;
using System.Threading.Tasks;

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
        [Authorize(Roles = "admin")] // Authorize should always use lower-case role names.
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
