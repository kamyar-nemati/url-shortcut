﻿using Cassandra;

namespace URL_Shortcut.Utils.Database
{
    public class QueryInsertURL
    {
        ISession session;

        public QueryInsertURL(ISession session)
        {
            this.session = session;
        }

        /// <summary>
        /// Insert a new URL into database.
        /// </summary>
        /// <param name="uuid">URL's TimeUUID</param>
        /// <param name="url">Long URL</param>
        /// <param name="signature">Short URL</param>
        /// <param name="sha512">URL's SHA512</param>
        /// <param name="sha256">URL's SHA256</param>
        /// <returns>Returns true if operation was successful.</returns>
        public bool InsertURL(TimeUuid uuid, string url, string signature, string sha512, string sha256)
        {
            // Prepare the statements
            var stmtInsertURL = this.session.Prepare("INSERT INTO tbl_urls (uuid, url, signature, created_on) VALUES (?, ?, ?, unixtimestampof(now())) ;");
            var stmtInsertHash = this.session.Prepare("INSERT INTO tbl_hashes (sha512, sha256, uuid) VALUES (?, ?, ?) ;");
            var stmtInsertSignature = this.session.Prepare("INSERT INTO tbl_signatures (signature, uuid) VALUES (?, ?) ;");

            // Make a batch of statements
            BatchStatement batchStatement = new BatchStatement()
                .Add(stmtInsertURL.Bind(uuid, url, signature))
                .Add(stmtInsertHash.Bind(sha512, sha256, uuid))
                .Add(stmtInsertSignature.Bind(signature, uuid));

            // Execute the statements
            this.session.Execute(batchStatement);

            // Update the global counter
            var cql = "UPDATE tbl_counters SET counter = counter + 1 WHERE key = ? ;";
            var prep = this.session.Prepare(cql);
            var stmt = prep.Bind("urls");
            this.session.Execute(stmt);
            
            return true;
        }
    }
}
