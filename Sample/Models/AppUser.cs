using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RavenDB.Identity;

namespace Sample.Models
{
    // Add profile data for application users by adding properties to the AppUser class
    public class AppUser : IdentityUser
    {
        /// <summary>
        /// Sample property. Add your own.
        /// </summary>
        public bool IsAyende { get; set; }
    }
}
