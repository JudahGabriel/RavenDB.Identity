using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sample.Mvc.Models
{
    public class AppUser : Raven.Identity.IdentityUser
    {
        public const string AdminRole = "admin";
        public const string ManagerRole = "manager";

        /// <summary>
        /// The user's full name.
        /// </summary>
        public string FullName { get; set; }
    }
}
