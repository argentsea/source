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
    public abstract partial class ShardSetsBase<TConfiguration> where TConfiguration : class, IShardSetsConfigurationOptions, new()
    {
        /// <summary>
        /// This collection represents a complete shard set. Typically all databases within a shard set have nearly identical schemas.
        /// </summary>
        public class ShardSet : ICollection
        {
            private readonly object syncRoot = new object();
            private readonly ImmutableDictionary<short, ShardInstance<TConfiguration>> dtn;
            private readonly short _defaultShardId;
            private Dictionary<string, object>? _mockResult;

            public ShardSet(ShardSetsBase<TConfiguration> parent, IShardSetConnectionsConfiguration config)
            {
                var bdr = ImmutableDictionary.CreateBuilder<short, ShardInstance<TConfiguration>>();
                this.Key = config.ShardSetName;
                _defaultShardId = config.DefaultShardId;
                if (config.ShardsConfigInternal is null)
                {
                    throw new InvalidOperationException($"The shard set “{config.ShardSetName}” has no shard connections configured.");
                }
                foreach (var shd in config.ShardsConfigInternal)
                {
                    if (shd is null)
                    {
                        throw new Exception($"A shard set’s connection configuration was not valid; the configuration provider returned null.");
                    }
                    var shardSetsConfig = config as DataConnectionConfigurationBase;
                    var shardConfig = shd as DataConnectionConfigurationBase;
                    var readConfig = config.ReadConfigInternal as DataConnectionConfigurationBase;
                    var writeConfig = config.WriteConfigInternal as DataConnectionConfigurationBase;
                    shd.ReadConnectionInternal?.SetAmbientConfiguration(parent._globalConfiguration, shardSetsConfig, readConfig, shardConfig);
                    shd.WriteConnectionInternal?.SetAmbientConfiguration(parent._globalConfiguration, shardSetsConfig, writeConfig, shardConfig);
                    bdr.Add(shd.ShardId, new ShardInstance<TConfiguration>(parent, shd.ShardId, shd));
                }
                this.dtn = bdr.ToImmutable();
                this.ReadAll = new ShardSetReadAll(this);
                this.ReadFirst = new ShardSetReadFirst(this);
                this.Write = new ShardSetWrite(this);
            }

            public string? Key { get; set; }

            /// <summary>
            /// Set this property to prevent database execution and return the provided result instead.
            /// The dictionary key should match the Query object name.
            /// Methods returning a result will return this result instead (Note that the return types must match or an error will be thrown).
            /// A ShardSet batch need only have a non-null dictionary (i.e. can be empty) to avoid the batch database execution.
            /// The dictionary is created on first access of this property, so reading the property is enough to mock ShardSet batches.
            /// </summary>
            public Dictionary<string, object> MockResults {
                get 
                {
                    // Thread-safe lazy creation: when several threads touch this property first at the same time,
                    // exactly one dictionary is published and every caller receives that same instance. A plain
                    // null-check-then-assign could hand a caller a dictionary that is later overwritten, silently
                    // discarding the mocks it registered.
                    return LazyInitializer.EnsureInitialized(ref this._mockResult, () => new Dictionary<string, object>());
                }
            }

            public ShardInstance<TConfiguration> this[short shardId]
            {
                get { return dtn[shardId]; }
            }
            public ShardInstance<TConfiguration> this[IShardKey shardKey] => this[shardKey.ShardId];

            public int Count
            {
                get { return dtn.Count; }
            }
            public ShardInstance<TConfiguration> DefaultShard => dtn[_defaultShardId];

            public bool IsSynchronized => true;

            public object SyncRoot => syncRoot;

            public void CopyTo(Array array, int index)
                => this.dtn.Values.ToImmutableList().CopyTo((ShardInstance<TConfiguration>[])array, index);

            public IEnumerator GetEnumerator() => this.dtn.GetEnumerator();

            #region internal ShardSet query methods
            // newInstance supplies the instance passed to resultHandler (Mapper handlers populate it); when null, handlers receive default.
            internal async Task<List<TModel>> ReadQueryAllAsync<TArg, TModel>(Query query, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, QueryResultModelHandler<TArg, TModel> resultHandler, TArg dataObject, Func<TModel>? newInstance, CancellationToken cancellationToken)
            {
                var result = new List<TModel>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<TModel>)this._mockResult[query.Name];
                }
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                cancellationToken.ThrowIfCancellationRequested();

                var tsks = new List<Task<TModel?>>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            tsks.Add(this.dtn[shardId].Read._manager.QueryAsync<TArg, TModel>(newInstance is null ? default : newInstance(), query, parameters, shardParameterOrdinal, null, resultHandler, false, dataObject, null, queryCancelationToken.Token));
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            tsks.Add(this.dtn[shardTuple.Key].Read._manager.QueryAsync<TArg, TModel>(newInstance is null ? default : newInstance(), query, parameters, shardParameterOrdinal, shardTuple.Value, resultHandler, false, dataObject, null, queryCancelationToken.Token));
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var tskResult = tsk.Result;
                        if (tskResult != null)
                        {
                            result.Add(tskResult);
                        }
                    }
                }
                return result;
            }

            internal async Task<TModel?> ReadQueryFirstAsync<TArg, TModel>(Query query, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, QueryResultModelHandler<TArg, TModel> resultHandler, TArg dataObject, CancellationToken cancellationToken) where TModel : new()
            {
                TModel? result = default;
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (TModel)this._mockResult[query.Name];
                }

                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<TModel?>>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            tsks.Add(this.dtn[shardId].Read._manager.QueryAsync<TArg, TModel>(new TModel(), query, parameters, shardParameterOrdinal, null, resultHandler, false, dataObject, null, queryCancelationToken.Token));
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            tsks.Add(this.dtn[shardTuple.Key].Read._manager.QueryAsync<TArg, TModel>(new TModel(), query, parameters, shardParameterOrdinal, shardTuple.Value, resultHandler, false, dataObject, null, queryCancelationToken.Token));
                        }
                    }
                    result = await ShardFanOut.FirstNonNullAsync(tsks, queryCancelationToken, cancellationToken).ConfigureAwait(false);
                }
                return result;
            }

            internal async Task<List<TModel>> MapListAsync<TModel>(Query query, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken) where TModel : new()
            {
                var result = new List<TModel>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<TModel>)this._mockResult[query.Name];
                }

                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<List<TModel>>>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            tsks.Add(this.dtn[shardId].Read._manager.ListAsync<TModel>(query, parameters, shardParameterOrdinal, null, null, queryCancelationToken.Token));
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            tsks.Add(this.dtn[shardTuple.Key].Read._manager.ListAsync<TModel>(query, parameters, shardParameterOrdinal, shardTuple.Value, null, queryCancelationToken.Token));
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var interim = tsk.Result;
                        if (interim is not null)
                        {
                            result.AddRange(interim);
                        }
                    }
                }
                return result;
            }

            internal  async Task<List<TValue>> ListAsync<TValue>(Query query, DbParameterCollection parameters, string columnName, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken)
            {
                var result = new List<TValue>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<TValue>)this._mockResult[query.Name];
                }
                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<List<(TValue, object, object)>>>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            tsks.Add(this.dtn[shardId].Read._manager.ListAsync<TValue, object, object>(query, columnName, null, null, parameters, shardParameterOrdinal, null, queryCancelationToken.Token));
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            tsks.Add(this.dtn[shardTuple.Key].Read._manager.ListAsync<TValue, object, object>(query, columnName, null, null, parameters, shardParameterOrdinal, shardTuple.Value, queryCancelationToken.Token));
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var interim = tsk.Result;
                        if (interim is not null)
                        {
                            foreach (var itm in interim)
                            {
                                result.Add(itm.Item1);
                            }
                        }
                    }
                }
                return result;
            }

            internal async Task<List<ShardKey<TRecord>>> ListAsync<TRecord>(Query query, string recordColumnName, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken)
                where TRecord : IComparable
            {
                var result = new List<ShardKey<TRecord>>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<ShardKey<TRecord >>)this._mockResult[query.Name];
                }
                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<List<(TRecord, object, object)>>>();
                var shardIds = new Dictionary<Task<List<(TRecord, object, object)>>, short>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            var tsk = this.dtn[shardId].Read._manager.ListAsync<TRecord, object, object>(query, recordColumnName, null, null, parameters, shardParameterOrdinal, null, queryCancelationToken.Token);
                            tsks.Add(tsk);
                            shardIds.Add(tsk, shardId);
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            var tsk = this.dtn[shardTuple.Key].Read._manager.ListAsync<TRecord, object, object>(query, recordColumnName, null, null, parameters, shardParameterOrdinal, shardTuple.Value, queryCancelationToken.Token);
                            tsks.Add(tsk);
                            shardIds.Add(tsk, shardTuple.Key);
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var interim = tsk.Result;
                        if (interim is not null)
                        {
                            foreach (var itm in interim)
                            {
                                result.Add(new ShardKey<TRecord>(shardIds[tsk], itm.Item1));
                            }
                        }
                    }
                }
                return result;
            }

            internal async Task<List<ShardKey<TRecord>>> ListAsync<TRecord>(Query query, string shardColumnName, string recordColumnName, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken)
                where TRecord : IComparable
            {
                var result = new List<ShardKey<TRecord>>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<ShardKey<TRecord>>)this._mockResult[query.Name];
                }
                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<List<(short, TRecord, object)>>>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            var tsk = this.dtn[shardId].Read._manager.ListAsync<short, TRecord, object>(query, shardColumnName, recordColumnName, null, parameters, shardParameterOrdinal, null, queryCancelationToken.Token);
                            tsks.Add(tsk);
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            var tsk = this.dtn[shardTuple.Key].Read._manager.ListAsync<short, TRecord, object>(query, shardColumnName, recordColumnName, null, parameters, shardParameterOrdinal, shardTuple.Value, queryCancelationToken.Token);
                            tsks.Add(tsk);
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var interim = tsk.Result;
                        if (interim is not null)
                        {
                            foreach (var itm in interim)
                            {
                                result.Add(new ShardKey<TRecord>(itm.Item1, itm.Item2));
                            }
                        }
                    }
                }
                return result;
            }

            internal async Task<List<ShardKey<TRecord, TChild>>> ListAsync<TRecord, TChild>(Query query, string recordColumnName, string childColumnName, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken)
                where TRecord : IComparable
                where TChild : IComparable
            {
                var result = new List<ShardKey<TRecord, TChild>>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<ShardKey<TRecord, TChild>>)this._mockResult[query.Name];
                }
                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<List<(TRecord, TChild, object)>>>();
                var shardIds = new Dictionary<Task<List<(TRecord, TChild, object)>>, short>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            var tsk = this.dtn[shardId].Read._manager.ListAsync<TRecord, TChild, object>(query, recordColumnName, childColumnName, null, parameters, shardParameterOrdinal, null, queryCancelationToken.Token);
                            tsks.Add(tsk);
                            shardIds.Add(tsk, shardId);
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            var tsk = this.dtn[shardTuple.Key].Read._manager.ListAsync<TRecord, TChild, object>(query, recordColumnName, childColumnName, null, parameters, shardParameterOrdinal, shardTuple.Value, queryCancelationToken.Token);
                            tsks.Add(tsk);
                            shardIds.Add(tsk, shardTuple.Key);
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var interim = tsk.Result;
                        if (interim is not null)
                        {
                            foreach (var itm in interim)
                            {
                                result.Add(new ShardKey<TRecord, TChild>(shardIds[tsk], itm.Item1, itm.Item2));
                            }
                        }
                    }
                }
                return result;
            }

            internal async Task<List<ShardKey<TRecord, TChild>>> ListAsync<TRecord, TChild>(Query query, string shardColumnName, string recordColumnName, string childColumnName, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken)
                where TRecord : IComparable
                where TChild : IComparable
            {
                var result = new List<ShardKey<TRecord, TChild>>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<ShardKey<TRecord, TChild>>)this._mockResult[query.Name];
                }
                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task<List<(short, TRecord, TChild)>>>();
                using (var queryCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (shardParameterValues is null)
                    {
                        foreach (var shardId in dtn.Keys)
                        {
                            var tsk = this.dtn[shardId].Read._manager.ListAsync<short, TRecord, TChild>(query, shardColumnName, recordColumnName, childColumnName, parameters, shardParameterOrdinal, null, queryCancelationToken.Token);
                            tsks.Add(tsk);
                        }
                    }
                    else
                    {
                        if (shardParameterValues.Shards.Count == 0)
                        {
                            return result;
                        }
                        foreach (var shardTuple in shardParameterValues.Shards)
                        {
                            var tsk = this.dtn[shardTuple.Key].Read._manager.ListAsync<short, TRecord, TChild>(query, shardColumnName, recordColumnName, childColumnName, parameters, shardParameterOrdinal, shardTuple.Value, queryCancelationToken.Token);
                            tsks.Add(tsk);
                        }
                    }
                    await ShardFanOut.WhenAllCancelOnFaultAsync(tsks, queryCancelationToken).ConfigureAwait(false);
                    foreach (var tsk in tsks)
                    {
                        var interim = tsk.Result;
                        if (interim is not null)
                        {
                            foreach (var itm in interim)
                            {
                                result.Add(new ShardKey<TRecord, TChild>(itm.Item1, itm.Item2, itm.Item3));
                            }
                        }
                    }
                }
                return result;
            }

            internal async Task RunAllAsync(Query query, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, CancellationToken cancellationToken)
            {
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                var tsks = new List<Task>();
                // Writes are not cancelled when a sibling shard fails: cancelling an in-flight write would leave that shard's
                // outcome unknown to the caller, so every shard's write runs to completion (or caller cancellation).
                if (shardParameterValues is null)
                {
                    foreach (var shardId in dtn.Keys)
                    {
                        tsks.Add(this.dtn[shardId].Write._manager.RunAsync(query, parameters, shardParameterOrdinal, null, null, cancellationToken));
                    }
                }
                else
                {
                    if (shardParameterValues.Shards.Count == 0)
                    {
                        return;
                    }
                    foreach (var shardTuple in shardParameterValues.Shards)
                    {
                        tsks.Add(this.dtn[shardTuple.Key].Write._manager.RunAsync(query, parameters, shardParameterOrdinal, shardTuple.Value, null, cancellationToken));
                    }
                }
                await ShardFanOut.WhenAllSurfaceAllFailuresAsync(tsks).ConfigureAwait(false);
            }

            internal async Task BatchAllAsync(ShardSetBatch batch, ShardsValues? shardParameterValues, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(batch);
                // Test the backing field: the MockResults getter lazily creates the dictionary, so testing the property is always true.
                // A ShardSet batch is mocked once the caller has obtained the mock dictionary (it may be empty).
                if (this._mockResult is not null)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var tsks = new List<Task>();
                // Writes are not cancelled when a sibling shard fails; see RunAllAsync.
                if (shardParameterValues is null)
                {
                    foreach (var shardId in dtn.Keys)
                    {
                        tsks.Add(this.dtn[shardId].Write._manager.RunBatchAsync<object>(batch, null, cancellationToken));
                    }
                }
                else
                {
                    if (shardParameterValues.Shards.Count == 0)
                    {
                        return;
                    }
                    foreach (var shardTuple in shardParameterValues.Shards)
                    {
                        tsks.Add(this.dtn[shardTuple.Key].Write._manager.RunBatchAsync<object>(batch, null, cancellationToken));
                    }
                }
                await ShardFanOut.WhenAllSurfaceAllFailuresAsync(tsks).ConfigureAwait(false);
            }

            // See ReadQueryAllAsync for newInstance.
            internal async Task<List<TModel>> WriteQueryAllAsync<TArg, TModel>(Query query, DbParameterCollection parameters, ShardsValues? shardParameterValues, string? shardParameterName, QueryResultModelHandler<TArg, TModel> resultHandler, TArg dataObject, Func<TModel>? newInstance, CancellationToken cancellationToken)
            {
                var result = new List<TModel>();
                if (query is null || string.IsNullOrEmpty(query.Sql) || string.IsNullOrEmpty(query.Name))
                {
                    throw new ArgumentNullException(nameof(query));
                }
                ArgumentNullException.ThrowIfNull(parameters);
                if (this._mockResult is not null && this._mockResult.Count > 0 && this._mockResult.ContainsKey(query.Name))
                {
                    return (List<TModel>)this._mockResult[query.Name];
                }
                var shardParameterOrdinal = parameters.GetParameterOrdinal(shardParameterName);
                cancellationToken.ThrowIfCancellationRequested();

                var tsks = new List<Task<TModel?>>();
                // Writes are not cancelled when a sibling shard fails; see RunAllAsync.
                if (shardParameterValues is null)
                {
                    foreach (var shardId in dtn.Keys)
                    {
                        tsks.Add(this.dtn[shardId].Write._manager.QueryAsync<TArg, TModel>(newInstance is null ? default : newInstance(), query, parameters, shardParameterOrdinal, null, resultHandler, false, dataObject, null, cancellationToken));
                    }
                }
                else
                {
                    if (shardParameterValues.Shards.Count == 0)
                    {
                        return result;
                    }
                    foreach (var shardTuple in shardParameterValues.Shards)
                    {
                        tsks.Add(this.dtn[shardTuple.Key].Write._manager.QueryAsync<TArg, TModel>(newInstance is null ? default : newInstance(), query, parameters, shardParameterOrdinal, shardTuple.Value, resultHandler, false, dataObject, null, cancellationToken));
                    }
                }
                await ShardFanOut.WhenAllSurfaceAllFailuresAsync(tsks).ConfigureAwait(false);
                foreach (var tsk in tsks)
                {
                    var tskResult = tsk.Result;
                    if (tskResult != null)
                    {
                        result.Add(tskResult);
                    }
                }
                return result;
            }

            #endregion

            public ShardSetReadAll ReadAll { get; }
            public ShardSetReadFirst ReadFirst { get; }
            public ShardSetWrite Write { get; }
        }
    }
}
