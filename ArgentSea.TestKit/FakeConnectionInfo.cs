// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// Whether a fake connection is a shard's (or database's) read or write endpoint.
    /// </summary>
    public enum FakeConnectionAccess
    {
        Read,
        Write
    }

    /// <summary>
    /// The identity the TestKit builders encode into each fake connection string, so the provider can tell
    /// which shard set, shard (or database) and endpoint a command ran against.
    /// </summary>
    public sealed class FakeConnectionInfo
    {
        public const string Prefix = "FakeProvider";

        private FakeConnectionInfo(string? connectionString, string? shardSetName, short? shardId, string? databaseKey, FakeConnectionAccess access)
        {
            ConnectionString = connectionString;
            ShardSetName = shardSetName;
            ShardId = shardId;
            DatabaseKey = databaseKey;
            Access = access;
        }

        public string? ConnectionString { get; }
        public string? ShardSetName { get; }
        /// <summary>Null when the connection belongs to a (non-sharded) database rather than a shard.</summary>
        public short? ShardId { get; }
        public string? DatabaseKey { get; }
        public FakeConnectionAccess Access { get; }

        public static string ForShard(string shardSetName, short shardId, FakeConnectionAccess access)
            => $"{Prefix};ShardSet={shardSetName};ShardId={shardId};Access={access}";

        public static string ForDatabase(string databaseKey, FakeConnectionAccess access)
            => $"{Prefix};Database={databaseKey};Access={access}";

        /// <summary>
        /// Parses a connection string. Strings not produced by the builders (any free-form text) parse to an info with no shard or database.
        /// </summary>
        public static FakeConnectionInfo Parse(string? connectionString)
        {
            string? shardSet = null;
            short? shardId = null;
            string? database = null;
            var access = FakeConnectionAccess.Read;
            if (!string.IsNullOrEmpty(connectionString))
            {
                foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq < 0)
                    {
                        continue;
                    }
                    var key = part.Substring(0, eq).Trim();
                    var value = part.Substring(eq + 1).Trim();
                    if (string.Equals(key, "ShardSet", StringComparison.OrdinalIgnoreCase))
                    {
                        shardSet = value;
                    }
                    else if (string.Equals(key, "ShardId", StringComparison.OrdinalIgnoreCase) && short.TryParse(value, out var id))
                    {
                        shardId = id;
                    }
                    else if (string.Equals(key, "Database", StringComparison.OrdinalIgnoreCase))
                    {
                        database = value;
                    }
                    else if (string.Equals(key, "Access", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<FakeConnectionAccess>(value, true, out var parsedAccess))
                    {
                        access = parsedAccess;
                    }
                }
            }
            return new FakeConnectionInfo(connectionString, shardSet, shardId, database, access);
        }

        public override string ToString() => ConnectionString ?? string.Empty;
    }
}
