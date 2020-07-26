using Raven.Client.Documents;
using Raven.Client.Documents.BulkInsert;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.CompareExchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Identity.Migrations
{
    /// <summary>
    /// This migration upgrades an old database (RavenDB.Identity v5 and earlier) to the new compare/exchange model used in v6+.
    /// </summary>
    /// <remarks>
    /// Previous versions of RavenDB.Identity did not ensure cluster-wide uniqueness.
    /// The compare/exchange model uses Raven's compare/exchange values to ensure user uniqueness in a cluster. 
    /// </remarks>
    public class CompareExchangeUniqueness : MigrationBase
    {
        /// <summary>
        /// Creates a new CompareExchangeUniqueness migration.
        /// </summary>
        /// <param name="db">The Raven doc store.</param>
        public CompareExchangeUniqueness(IDocumentStore db)
            : base(db)
        {
        }

        /// <summary>
        /// Runs the migration. This can take several minutes if you have thousands of users.
        /// IMPORTANT: backup your database before running this migration; data loss is possible.
        /// </summary>
        /// <returns></returns>
        public void Migrate()
        {
            // This is done in 3 steps:
            // 1. Find all (now obsolete) IdentityByUserName objects. (Formerly, we used these objects to ensure uniqueness.)
            // 2. Create a cmpXchg value for user's email address.
            // 3. Delete the IdentityByUserName collection.

#pragma warning disable CS0618 // Type or member is obsolete            
            // Step 1: find all the old IdentityByUserName objects
            var collectionName = docStore.Conventions.GetCollectionName(typeof(IdentityUserByUserName));
            var identityUsers = this.Stream<IdentityUserByUserName>();
            var emails = identityUsers.Select(u => (id: u.UserId, email: u.UserName));
#pragma warning restore CS0618 // Type or member is obsolete

            // Step 2: store each email as a compare/exchange value.
            foreach (var (id, email) in emails)
            {
                var compareExchangeKey = Conventions.CompareExchangeKeyFor(email);
                var storeOperation = new PutCompareExchangeValueOperation<string>(compareExchangeKey, id, 0);
                var storeResult = docStore.Operations.Send(storeOperation);
                if (!storeResult.Successful)
                {
                    var exception = new Exception($"Unable to migrate to RavenDB.Identity V6. An error occurred while storing the compare/exchange value. Before running {nameof(CompareExchangeUniqueness)} again, please delete all compare/exchange values in Raven that begin with {Conventions.EmailReservationKeyPrefix}.");
                    exception.Data.Add("compareExchangeKey", compareExchangeKey);
                    exception.Data.Add("compareExchangeValue", id);
                    throw exception;
                }
            }

            // Step 3: remove all IdentityUserByUserName objects.
            var operation = docStore
                .Operations
                .Send(new DeleteByQueryOperation(new Client.Documents.Queries.IndexQuery
                {
                    Query = $"from {collectionName}"
                }));
            operation.WaitForCompletion();
        }
    }
}
