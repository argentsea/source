// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Storage;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;

namespace ArgentSea.Orleans
{

    public class ArgentSeaShardGrainPersistence<TShardOptions> : IGrainStorage where TShardOptions : class, IShardSetsConfigurationOptions, new() //ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly string serviceId;
        private readonly ILogger? logger;
        private readonly string name;
        private readonly ShardSetsBase<TShardOptions>.ShardSet shards;
        private readonly Dictionary<string, OrleansShardQueryDefinitions> queryDefinitions;
        //private readonly int initStage;
        private readonly bool validateGrainKeys;

        // The caches are keyed by the Orleans state name AND the state type, as Orleans keys its own storage bridges:
        // the state name is not unique to a state type (every Grain<TState> uses "state", and [PersistentState] names
        // may repeat across grains and providers), while each compiled lambda is specific to the state type.
        private static readonly ConcurrentDictionary<(string StateName, Type StateType), Lazy<Func<ReadOnlyMemory<byte>, ParameterCollection, ILogger?, short>>> _readLambda = new();
        // Also keyed by the ValidateGrainKeys setting: the compiled lambda bakes that setting in, so two
        // providers with different settings must not share one.
        private static readonly ConcurrentDictionary<(string StateName, Type StateType, bool ValidateGrainKeys), Lazy<Delegate>> _writeLambda = new();
        //private static readonly ConcurrentDictionary<string, Lazy<Func<ReadOnlyMemory<byte>, object, ParameterCollection, ILogger?, short>>> _clearLambda = new();
        private static readonly ConcurrentDictionary<(string StateName, Type StateType, bool ValidateGrainKeys), Lazy<Delegate>> _clearLambda = new();
        private static readonly ConcurrentDictionary<(string StateName, Type StateType), Lazy<Delegate>> _setShardLambda = new();

        public ArgentSeaShardGrainPersistence(
            ShardSetsBase<TShardOptions> shards,
            OrleansShardPersistenceOptions orleansOptions,
            ClusterOptions clusterOptions,
            ILogger<ArgentSeaShardGrainPersistence<TShardOptions>>? logger)
        {
            var shardSetKey = orleansOptions.ShardSetKey;
            this.name = shardSetKey;
            this.shards = shards[shardSetKey];
            this.queryDefinitions = orleansOptions.Queries;
            this.serviceId = clusterOptions.ServiceId;
            this.logger = logger;
            //this.initStage = orleansOptions.Value.InitStage;
            this.validateGrainKeys = orleansOptions.ValidateGrainKeys;
        }

        //public void Participate(ISiloLifecycle observer)
        //{
        //    var name = OptionFormattingUtilities.Name<ArgentSeaShardGrainPersistence<TShardOptions>>(this.name);
        //    observer.Subscribe(name, this.initStage, Init, Close);
        //}

        public async Task ReadStateAsync<TModel>(string grainType, GrainId grainId, IGrainState<TModel> grainState)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            if (!this.queryDefinitions.TryGetValue(grainType, out var queries))
            {
                throw new OrleansQueryNotProvidedException(grainType, nameof(ReadStateAsync));
            }
            var prms = new ParameterCollection();


            var lazyShardIdAndParams = _readLambda.GetOrAdd((grainType, typeof(TModel)), new Lazy<Func<ReadOnlyMemory<byte>, ParameterCollection, ILogger?, short>>(() => OrleansExpressionHelper.BuildShardReadLambda<TModel>(grainType, this.logger), LazyThreadSafetyMode.ExecutionAndPublication));
            if (lazyShardIdAndParams.IsValueCreated)
            {
                logger?.OrleansShardCacheHit(grainType);
            }
            else
            {
                logger?.OrleansShardCacheMiss(grainType);
            }
            var shardId = lazyShardIdAndParams.Value(grainId.Key.Value, prms, this.logger);

            var instance = grainState.State ?? Activator.CreateInstance<TModel>();

            // Every mapping branch returns null (default) when the database returns no result, so the result is always kept.
            TModel? result;
            if (queries.ResultFormat == QueryResultFormat.ResultSet)
            {
                if (Mapper.HasCollectionMapProperties(typeof(TModel)))
                {
                    result = await this.shards[shardId].Read.MapReaderWithCollectionsAsync<TModel>(instance, queries.ReadQuery, prms, CancellationToken.None);
                }
                else
                {
                    result = await this.shards[shardId].Read.MapReaderAsync<TModel>(instance, queries.ReadQuery, prms, CancellationToken.None);
                }
            }
            else
            {
                // The key parameters are already present as inputs; declare the remaining mapped properties as output parameters so the procedure can return them.
                prms.CreateOutputParameters<TModel>(this.logger);
                result = await this.shards[shardId].Read.MapOutputAsync<TModel>(instance, queries.ReadQuery, prms, CancellationToken.None);
            }
            if (result is null)
            {
                // No record: use the type’s IActivator factory, if any; otherwise create a default instance and set its shard key from the grain id.
                if (!GrainStateActivator.TryActivate<TModel>(grainType, grainId.Key.Value, out var activated))
                {
                    activated = Activator.CreateInstance<TModel>();
                    var lazySetShard = _setShardLambda.GetOrAdd((grainType, typeof(TModel)), new Lazy<Delegate>(() => OrleansExpressionHelper.BuildSetShardLambda<TModel>(grainType, this.logger), LazyThreadSafetyMode.ExecutionAndPublication));
                    if (lazySetShard.IsValueCreated)
                    {
                        logger?.OrleansShardCacheHit(grainType);
                    }
                    else
                    {
                        logger?.OrleansShardCacheMiss(grainType);
                    }
                    ((Action<ReadOnlyMemory<byte>, TModel, ILogger?>)lazySetShard.Value)(grainId.Key.Value, activated, this.logger);
                }
                grainState.State = activated;
                grainState.RecordExists = false;
            }
            else
            {
                grainState.State = result;
                grainState.RecordExists = true;
            }
            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
            this.logger?.TraceShardReadCmdExecuted(shardId, grainType, elapsedMS);
        }


        public async Task WriteStateAsync<TModel>(string grainType, GrainId grainId, IGrainState<TModel> grainState)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            if (grainState.State is null)
            {
                throw new NoNullAllowedException($"The grain state cannot be written because it is null.");
            }
            if (!this.queryDefinitions.TryGetValue(grainType, out var queries))
            {
                throw new OrleansQueryNotProvidedException(grainType, nameof(WriteStateAsync));
            }
            var prms = new ParameterCollection()
                .CreateInputParameters<TModel>(grainState.State, this.logger);


            var lazyShardId = _writeLambda.GetOrAdd((grainType, typeof(TModel), this.validateGrainKeys), new Lazy<Delegate>(() => OrleansExpressionHelper.BuildShardWriteLambda<TModel>(grainType, this.validateGrainKeys), LazyThreadSafetyMode.ExecutionAndPublication));
            if (lazyShardId.IsValueCreated)
            {
                logger?.OrleansShardCacheHit(grainType);
            }
            else
            {
                logger?.OrleansShardCacheMiss(grainType);
            }
            var lzyValue = (Func<ReadOnlyMemory<byte>, TModel, short>)lazyShardId.Value;
            var shardId = lzyValue(grainId.Key.Value, grainState.State);


            await this.shards[shardId].Write.RunAsync(queries.WriteQuery, prms, CancellationToken.None);
            grainState.RecordExists = true;
            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
            this.logger?.TraceShardWriteCmdExecuted(shardId, grainType, elapsedMS);
        }

        public async Task ClearStateAsync<TModel>(string grainType, GrainId grainId, IGrainState<TModel> grainState)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            if (grainState.State is null)
            {
                throw new NoNullAllowedException($"The grain state cannot be cleared because it is null.");
            }
            if (!this.queryDefinitions.TryGetValue(grainType, out var queries))
            {
                throw new OrleansQueryNotProvidedException(grainType, nameof(ClearStateAsync));
            }
            var prms = new ParameterCollection();

            var lazyShardIdAndParams = _clearLambda.GetOrAdd((grainType, typeof(TModel), this.validateGrainKeys), new Lazy<Delegate>(() => OrleansExpressionHelper.BuildShardClearLambda<TModel>(grainType, this.validateGrainKeys, this.logger), LazyThreadSafetyMode.ExecutionAndPublication));
            if (lazyShardIdAndParams.IsValueCreated)
            {
                logger?.OrleansShardCacheHit(grainType);
            }
            else
            {
                logger?.OrleansShardCacheMiss(grainType);
            }
            var lzy = (Func<ReadOnlyMemory<byte>, TModel, ParameterCollection, ILogger?, short>)lazyShardIdAndParams.Value;
            var shardId = lzy(grainId.Key.Value, grainState.State, prms, this.logger);

            await this.shards[shardId].Write.RunAsync(queries.ClearQuery, prms, CancellationToken.None);
            grainState.RecordExists = false;
            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
            this.logger?.TraceShardClearCmdExecuted(shardId, grainType, elapsedMS);
        }

        private static readonly double TimestampToMilliseconds = (double)TimeSpan.TicksPerSecond / (Stopwatch.Frequency * TimeSpan.TicksPerMillisecond);

        //private Task Init (CancellationToken cancellationToken)
        //{
        //    this.logger?.PersistanceLifecycleOnStart(this.name);
        //    if (this.shards is null || this.shards.Count == 0)
        //    {
        //        throw new Exception($"The ShardSet {this.name} is not defined in the configuration.");
        //    }
        //    return Task.CompletedTask;
        //}

        //private Task Close(CancellationToken cancellationToken)
        //{
        //    this.logger?.PersistanceLifecycleOnStop(this.name);
        //    return Task.CompletedTask;
        //}
    }
}
