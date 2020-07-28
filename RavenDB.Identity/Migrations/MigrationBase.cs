using Raven.Client.Documents;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Queries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Identity.Migrations
{
    /// <summary>
    /// Base class for migrations.
    /// </summary>
    public class MigrationBase
    {
        /// <summary>
        /// The Raven doc store.
        /// </summary>
        protected readonly IDocumentStore docStore;

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="db">The Raven document store.</param>
        protected MigrationBase(IDocumentStore db)
        {
            this.docStore = db;
        }

        /// <summary>
        /// Lazily streams documents of the specified type back.
        /// </summary>
        /// <typeparam name="T">The type of document to stream.</typeparam>
        /// <returns>A lazy stream of documents.</returns>
        public IEnumerable<T> Stream<T>()
        {
            return StreamWithMetadata<T>().Select(r => r.Document);
        }

        /// <summary>
        /// Lazily streams document of the specified type back, including metadata.
        /// </summary>
        /// <typeparam name="T">The type of document to stream.</typeparam>
        /// <returns>A lazy stream of documents.</returns>
        public IEnumerable<StreamResult<T>> StreamWithMetadata<T>()
        {
            using var dbSession = docStore.OpenSession();
            var collectionName = this.docStore.Conventions.FindCollectionName(typeof(T));
            var identityPartsSeparator = this.docStore.Conventions.IdentityPartsSeparator;
            using var stream = dbSession.Advanced.Stream<T>(collectionName + identityPartsSeparator);
            while (stream.MoveNext())
            {
                yield return stream.Current;
            }
        }
    }
}
