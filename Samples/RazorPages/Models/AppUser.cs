using Raven.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sample.Models
{
    public class AppUser : Raven.Identity.IdentityUser
    {
        /// <summary>
        /// The user's full name.
        /// </summary>
        public string FullName { get; set; }
    }
}
