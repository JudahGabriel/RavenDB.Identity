using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sample.Mvc.Models;

namespace Sample.Mvc.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            var allRoles = new[] { AppUser.AdminRole, AppUser.ManagerRole };
            var userRoles = string.Join(", ", allRoles.Where(r => User.IsInRole(r)));
            
            ViewBag.UserRoles = userRoles;
            return View();
        }

        [HttpGet]
        [Authorize(Roles = AppUser.AdminRole)]
        public IActionResult AdminOnly()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
