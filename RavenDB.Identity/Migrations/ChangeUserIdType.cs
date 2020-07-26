using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raven.Identity.Migrations
{
    /// <summary>
    /// This migration changes your existing users to use a different user ID strategy.
    /// Note: This migration will migrate your existing users. However, if you have objects in your database referring to existing user Ids, you'll need to manually migrate those.
    /// </summary>
    public class ChangeUserIdType : MigrationBase
    {
        private readonly UserIdType newUserIdType;

        /// <summary>
        /// Creates a new ChangeUserIdType migration.
        /// </summary>
        /// <param name="db">The Raven doc store.</param>
        /// <param name="newUserIdType">The type of ID to migrate to.</param>
        public ChangeUserIdType(IDocumentStore db, UserIdType newUserIdType)
            : base(db)
        {
            this.newUserIdType = newUserIdType;
        }

        /// <summary>
        /// Runs the migration. This operation can take several minutes depeneding on how many users are in your database.
        /// IMPORTANT: backup your database before running this migration, as data loss is possible.
        /// </summary>
        public void Migrate<TUser>()
            where TUser : IdentityUser
        {
            // Raven doesn't allow you to change a document's existing ID.
            // Instead, you must create a new document with that ID.
            //
            // Step 1, grab all the existing users.
            // Step 2, recreate that user with the new ID
            // Step 3, delete all the old users.

            // 1. Grab all the existing users.
            var existingUserStream = this.StreamWithMetadata<TUser>();
            using var bulkInsert = docStore.BulkInsert();
            var userIdsToDelete = new List<string>(1000);
            foreach (var userStream in existingUserStream)
            {
                var user = userStream.Document;

                // Do we need a new ID?
                var newId = Conventions.UserIdFor(user, this.newUserIdType, this.docStore);
                var needsNewId = !string.Equals(newId, user.Id);
                if (needsNewId)
                {
                    // Step 2, clone the user with the new ID.
                    bulkInsert.Store(user, newId, userStream.Metadata);

                    // Step 3a, queue up the delete for the existing user.
                    userIdsToDelete.Add(userStream.Id);
                }
            }

            // Step 3b, delete the old users.
            using var dbSession = docStore.OpenSession();
            userIdsToDelete.ForEach(dbSession.Delete);
            dbSession.SaveChanges();
        }
    }
}
