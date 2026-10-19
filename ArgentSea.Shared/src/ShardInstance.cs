// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Data.Common;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Polly;

namespace ArgentSea
{
    /// <summary>
    /// This class represents a distinct shard, or database instance, within the shardset.
    /// </summary>
    public class ShardInstance<TConfiguration> where TConfiguration : class, IShardSetsConfigurationOptions, new()
    {
        public ShardInstance(ShardSetsBase<TConfiguration> parent, short shardId, IShardConnectionConfiguration shardConnection)
        {
            this.ShardId = shardId;
            // When only one connection is configured, use it for both reads and writes.
            var readConnection = shardConnection.ReadConnectionInternal ?? shardConnection.WriteConnectionInternal;
            var writeConnection = shardConnection.WriteConnectionInternal ?? shardConnection.ReadConnectionInternal;
            if (readConnection is null || writeConnection is null)
            {
                throw new InvalidOperationException($"Shard {shardId} has neither a read nor a write connection configured.");
            }
            this.Read = new ShardDataConnection<TConfiguration>(parent, shardId, readConnection);
            this.Write = new ShardDataConnection<TConfiguration>(parent, shardId, writeConnection);
        }
        public short ShardId { get; }
        public ShardDataConnection<TConfiguration> Read { get; }
        public ShardDataConnection<TConfiguration> Write { get; }

    }
}
