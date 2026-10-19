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
    internal class DataConnectionManager
	{
		#region Private variables and constructors
		private readonly ILogger? _logger;
        private ResiliencePolicyState _resilience;
		private readonly IDataProviderServiceFactory _dataProviderServices;
        private readonly IDataConnection _connectionConfig;
		private readonly string _connectionName;
        private readonly short _shardId;

        private const int DefaultRetryCount = 6;
        private const int DefaultCircuitBreakerTestInterval = 5000; // 5 seconds
        private const int DefaultCircuitBreakerFailureCount = 20;

        internal DataConnectionManager(short shardId, IDataProviderServiceFactory dataProviderServices, IDataConnection connectionConfig, string connectionName, ILogger? logger)
		{
			this._logger = logger;
            this._dataProviderServices = dataProviderServices;
			this._connectionName = connectionName;
            _shardId = shardId;
            _connectionConfig = connectionConfig;
            _resilience = CreateResiliencePolicyState();
        }
		#endregion
		#region Private helper methods

        /// <summary>
        /// The resilience policy together with the configuration values it was built from.
        /// Immutable, so it can be swapped atomically when the configuration changes.
        /// </summary>
        private sealed class ResiliencePolicyState
        {
            internal ResiliencePolicyState(int? retryCount, int? circuitBreakerFailureCount, int? circuitBreakerTestInterval, AsyncPolicy policy)
            {
                this.RetryCount = retryCount;
                this.CircuitBreakerFailureCount = circuitBreakerFailureCount;
                this.CircuitBreakerTestInterval = circuitBreakerTestInterval;
                this.Policy = policy;
            }
            internal int? RetryCount { get; }
            internal int? CircuitBreakerFailureCount { get; }
            internal int? CircuitBreakerTestInterval { get; }
            internal AsyncPolicy Policy { get; }

            internal bool Matches(IDataConnection config)
                => config.RetryCount == this.RetryCount
                && config.CircuitBreakerFailureCount == this.CircuitBreakerFailureCount
                && config.CircuitBreakerTestInterval == this.CircuitBreakerTestInterval;
        }

        /// <summary>
        /// Returns the resilience policy for the current connection configuration.
        /// The retry count and circuit breaker settings are baked into the policy when it is built, so the policy is rebuilt when they change
        /// (the retry interval and lengthening are read on every retry, so they need no rebuild).
        /// Rebuilding replaces the circuit breaker, so its state is reset only when its settings change; otherwise the same policy (and breaker state) is reused.
        /// </summary>
        internal AsyncPolicy ResiliencePolicy
        {
            get
            {
                var current = Volatile.Read(ref this._resilience);
                if (current.Matches(this._connectionConfig))
                {
                    return current.Policy;
                }
                var rebuilt = CreateResiliencePolicyState();
                // If another thread already swapped in a newer policy, use that one rather than overwriting it.
                var prior = Interlocked.CompareExchange(ref this._resilience, rebuilt, current);
                return ReferenceEquals(prior, current) ? rebuilt.Policy : prior.Policy;
            }
        }

        // Reads each configuration value once, so the policy is built from exactly the values it is later compared against.
        private ResiliencePolicyState CreateResiliencePolicyState()
        {
            var cfgRetryCount = this._connectionConfig.RetryCount;
            var cfgFailureCount = this._connectionConfig.CircuitBreakerFailureCount;
            var cfgTestInterval = this._connectionConfig.CircuitBreakerTestInterval;
            return new ResiliencePolicyState(cfgRetryCount, cfgFailureCount, cfgTestInterval,
                GetConnectionResiliencePolicy(cfgRetryCount, cfgFailureCount, cfgTestInterval));
        }

		private AsyncPolicy GetConnectionResiliencePolicy(int? cfgRetryCount, int? cfgFailureCount, int? cfgTestInterval)
		{
            AsyncPolicy result;
            int retryCnt = cfgRetryCount ?? DefaultRetryCount;
            int cbTestInterval = cfgTestInterval ?? DefaultCircuitBreakerTestInterval;
            int cbFailureCount = cfgFailureCount ?? DefaultCircuitBreakerFailureCount;
            var retryPolicy = Policy.Handle<DbException>(ex =>
					this._dataProviderServices.GetIsErrorTransient(ex)
				)
				.WaitAndRetryAsync(
					retryCount: retryCnt,
					sleepDurationProvider: attempt => this._connectionConfig.GetRetryTimespan(attempt),
					onRetry: (exception, timeSpan, retryCount, context) => this.HandleConnectionRetry(this._connectionName, retryCount, exception)
				);

			if (cfgFailureCount < 1)
			{
				result = retryPolicy;
			}
			else
			{
				var circuitBreakerPolicy = Policy.Handle<Exception>().CircuitBreakerAsync(
					exceptionsAllowedBeforeBreaking: cbFailureCount,
					durationOfBreak: TimeSpan.FromMilliseconds(cbTestInterval),
					onBreak: (exception, timeSpan) => this._logger?.CiruitBreakingDbConnection(this._connectionName),
					onReset: () => this._logger?.CiruitBrokenDbConnectionRestored(this._connectionName),
					onHalfOpen: () => this._logger?.CiruitBrokenDbConnectionTest(this._connectionName)
					);
				result = circuitBreakerPolicy.WrapAsync(retryPolicy);
			}
			return result;
		}

        /// <summary>
        /// Returns the parameter values to apply to this execution’s command, adding this shard’s id for the shard parameter (if any).
        /// The shard id is never written into the caller’s parameter collection: a ShardSet fan-out shares one collection across
        /// concurrently executing shards, and its values are copied into each command only after that shard’s connection opens.
        /// A value supplied for the shard parameter in <paramref name="parameterValues"/> takes precedence.
        /// </summary>
        private IDictionary<string, object>? WithShardId(DbParameterCollection parameters, int shardParameterOrdinal, IDictionary<string, object>? parameterValues)
        {
            if (shardParameterOrdinal < 0 || shardParameterOrdinal >= parameters.Count)
            {
                return parameterValues;
            }
            var shardParameterName = parameters[shardParameterOrdinal].ParameterName;
            if (parameterValues is not null && parameterValues.ContainsKey(shardParameterName))
            {
                return parameterValues;
            }
            var result = parameterValues is null ? new Dictionary<string, object>(1) : new Dictionary<string, object>(parameterValues);
            result[shardParameterName] = this._shardId;
            return result;
        }

		private void HandleConnectionRetry(string connectionName, int attempt, Exception exception)
		{
			this._logger?.RetryingDbConnection(connectionName, attempt, exception);

		}

		private static readonly double TimestampToMilliseconds = (double)TimeSpan.TicksPerSecond / (Stopwatch.Frequency * TimeSpan.TicksPerMillisecond);

		#endregion

		#region Public properties

		public string ConnectionString { get => this._connectionConfig.GetConnectionString(_logger); }

        #endregion

        #region Return data methods

        // returns up to three values. name must match paramter or it will revert to column name. ignored if null or empty.
        internal async Task<(TValue1?, TValue2?, TValue3?)> ReturnAsync<TValue1, TValue2, TValue3>(Query query, string? data1Name, string? data2Name, string? data3Name, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, int shardParameterOrdinal, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var startTimestamp = Stopwatch.GetTimestamp();

            parameterValues = this.WithShardId(parameters, shardParameterOrdinal, parameterValues);
			var result = await this.ResiliencePolicy.ExecuteAsync(newToken =>
				ExecuteQueryToValueAsync<TValue1, TValue2, TValue3>(query, data1Name, data2Name, data3Name, parameters, parameterValues, newToken), cancellationToken).ConfigureAwait(false);

            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
			_logger?.TraceDbCmdExecuted(query.Name, this._connectionName, elapsedMS);
			return result;
		}

        // returns the integer return value of a procedure. Will add parameter if not provided.
        internal async Task<int> ReturnAsync(Query query, DbParameterCollection parameters, int shardParameterOrdinal, Dictionary<string, object>? mockResults, CancellationToken cancellationToken)
        {
            if (mockResults is not null && mockResults.Count > 0 && mockResults.ContainsKey(query.Name))
            {
                return (int)mockResults[query.Name];
            }
            cancellationToken.ThrowIfCancellationRequested();
            var startTimestamp = Stopwatch.GetTimestamp();

            var parameterValues = this.WithShardId(parameters, shardParameterOrdinal, null);
            var result = await this.ResiliencePolicy.ExecuteAsync(newToken =>
                ExecuteQueryToValueAsync(query, parameters, parameterValues, newToken), cancellationToken).ConfigureAwait(false);

            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
            _logger?.TraceDbCmdExecuted(query.Name, this._connectionName, elapsedMS);
            return result;
        }

        #endregion

        #region List methods


        internal async Task<List<TModel>> ListAsync<TModel>(Query query, DbParameterCollection parameters, int shardParameterOrdinal, IDictionary<string, object>? parameterValues, Dictionary<string, object>? mockResults, CancellationToken cancellationToken) where TModel : new()
		{
            if (mockResults is not null && mockResults.Count > 0 && mockResults.ContainsKey(query.Name))
            {
                return (List<TModel>)mockResults[query.Name];
            }
            cancellationToken.ThrowIfCancellationRequested();
			var startTimestamp = Stopwatch.GetTimestamp();

            parameterValues = this.WithShardId(parameters, shardParameterOrdinal, parameterValues);
            var result = await this.ResiliencePolicy.ExecuteAsync(newToken =>
				ExecuteQueryToModelListAsync<TModel>(_shardId, query, parameters, parameterValues, newToken), cancellationToken).ConfigureAwait(false);

			var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
			_logger?.TraceDbCmdExecuted(query.Name, this._connectionName, elapsedMS);
			return result;
		}

        internal async Task<List<(TValue1, TValue2, TValue3)>> ListAsync<TValue1, TValue2, TValue3>(Query query, string? data1Name, string? data2Name, string? data3Name, DbParameterCollection parameters, int shardParameterOrdinal, IDictionary<string, object>? parameterValues, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTimestamp = Stopwatch.GetTimestamp();

            parameterValues = this.WithShardId(parameters, shardParameterOrdinal, parameterValues);
            var result = await this.ResiliencePolicy.ExecuteAsync(newToken =>
                ExecuteQueryToValueListAsync<TValue1, TValue2, TValue3>(query, data1Name, data2Name, data3Name, parameters, parameterValues, newToken), cancellationToken).ConfigureAwait(false);

            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
            _logger?.TraceDbCmdExecuted(query.Name, this._connectionName, elapsedMS);
            return result;
        }

        #endregion

        #region Query methods

        internal async Task<TModel?> QueryAsync<TArg, TModel>(TModel? instance, Query query, DbParameterCollection parameters, int shardParameterOrdinal, IDictionary<string, object>? parameterValues, QueryResultModelHandler<TArg, TModel> resultHandler, bool isTopOne, TArg optionalArgument, Dictionary<string, object>? mockResults, CancellationToken cancellationToken) 
		{
            if (mockResults is not null && mockResults.Count > 0 && mockResults.ContainsKey(query.Name))
            {
                return (TModel)mockResults[query.Name];
            }
            cancellationToken.ThrowIfCancellationRequested();
			var startTimestamp = Stopwatch.GetTimestamp();

            parameterValues = this.WithShardId(parameters, shardParameterOrdinal, parameterValues);
            var result = await this.ResiliencePolicy.ExecuteAsync(newToken =>
				this.ExecuteQueryWithDelegateAsync<TModel, TArg>(instance, query, parameters, parameterValues, this._shardId, resultHandler, isTopOne, optionalArgument, newToken), cancellationToken).ConfigureAwait(false);

			var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
			_logger?.TraceDbCmdExecuted(query.Name, this._connectionName, elapsedMS);
			return result;
		}


        #endregion

        #region Run methods

        internal async Task RunAsync(Query query, DbParameterCollection parameters, int shardParameterOrdinal, IDictionary<string, object>? parameterValues, Dictionary<string, object>? mockResults, CancellationToken cancellationToken)
		{
            if (mockResults is not null && mockResults.Count > 0 && mockResults.ContainsKey(query.Name))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
			var startTimestamp = Stopwatch.GetTimestamp();
            parameterValues = this.WithShardId(parameters, shardParameterOrdinal, parameterValues);

            await this.ResiliencePolicy.ExecuteAsync(newToken => this.ExecuteRunAsync(query, parameters, parameterValues, newToken), cancellationToken).ConfigureAwait(false);

            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
			_logger?.TraceDbCmdExecuted(query.Name, this._connectionName, elapsedMS);
		}

        internal async Task<TResult?> RunBatchAsync<TResult>(BatchBase<TResult> batch, Dictionary<string, object>? mockResults, CancellationToken cancellationToken)
        {
            if (mockResults is not null && mockResults.Count > 0 && mockResults.ContainsKey(string.Empty))
            {
                return (TResult)mockResults[string.Empty];
            }

            cancellationToken.ThrowIfCancellationRequested();
            var startTimestamp = Stopwatch.GetTimestamp();


            var result = await this.ResiliencePolicy.ExecuteAsync(newToken =>
                this.ExecuteBatchAsync<TResult>(batch, newToken), cancellationToken).ConfigureAwait(false);

            var elapsedMS = (long)((Stopwatch.GetTimestamp() - startTimestamp) * TimestampToMilliseconds);
            _logger?.TraceDbCmdExecuted("QueryBatch", this._connectionName, elapsedMS);
            return result;
        }

        #endregion
        #region Private Handlers

        private async Task<List<TModel>> ExecuteQueryToModelListAsync<TModel>(short shardId, Query query, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, CancellationToken cancellationToken) 
            where TModel : new()
		{
			List<TModel>? result = null;
			cancellationToken.ThrowIfCancellationRequested();
			using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
			{
				//await this._connectPolicy.ExecuteAsync(() => connection.OpenAsync(cancellationToken)).ConfigureAwait(false);
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
				using (var cmd = this._dataProviderServices.NewCommand(query.Sql, connection))
				{
					cmd.CommandType = query.Type;
					this._dataProviderServices.SetParameters(cmd, query.ParameterNames, parameters, parameterValues);
					using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
					{
						cancellationToken.ThrowIfCancellationRequested();
						result = Mapper.ToList<TModel>(dataReader, shardId, _logger);
					}
				}
			}
			return result ?? new List<TModel>();
		}
        private async Task<List<(TValue1, TValue2, TValue3)>> ExecuteQueryToValueListAsync<TValue1, TValue2, TValue3>(Query query, string? data1Name, string? data2Name, string? data3Name, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new List<(TValue1, TValue2, TValue3)>();
            using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = this._dataProviderServices.NewCommand(query.Sql, connection))
                {
                    cmd.CommandType = query.Type;
                    this._dataProviderServices.SetParameters(cmd, query.ParameterNames, parameters, parameterValues);
                    using (var rdr = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        while (rdr.Read())
                        {
                            // A slot whose data name is null is not requested; callers type such slots as object and never read them.
                            TValue1 value1 = default!;
                            TValue2 value2 = default!;
                            TValue3 value3 = default!;
                            if (!string.IsNullOrEmpty(data1Name))
                            {
                                value1 = rdr.GetFieldValue<TValue1>(rdr.GetOrdinal(data1Name));
                            }
                            if (!string.IsNullOrEmpty(data2Name))
                            {
                                value2 = rdr.GetFieldValue<TValue2>(rdr.GetOrdinal(data2Name));
                            }
                            if (!string.IsNullOrEmpty(data3Name))
                            {
                                value3 = rdr.GetFieldValue<TValue3>(rdr.GetOrdinal(data3Name));
                            }
                            result.Add((value1, value2, value3));
                        }
                    }
                }
            }
            return result;
        }

        // Adds return value parameter if it doesn't exist
        private async Task<int> ExecuteQueryToValueAsync(Query query, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = this._dataProviderServices.NewCommand(query.Sql, connection))
                {
                    cmd.CommandType = query.Type;
                    this._dataProviderServices.SetParameters(cmd, query.ParameterNames, parameters, parameterValues);
                    DbParameter? prmRtn = null;
                    foreach (DbParameter prm in cmd.Parameters)
                    {
                        if (prm.Direction == System.Data.ParameterDirection.ReturnValue)
                        {
                            prmRtn = prm;
                            break;
                        }
                    }
                    if (prmRtn is null)
                    {
                        prmRtn = cmd.CreateParameter();
                        prmRtn.Direction = System.Data.ParameterDirection.ReturnValue;
                        cmd.Parameters.Add(prmRtn);
                    }
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    if (prmRtn.Value is null || prmRtn.Value == System.DBNull.Value)
                    {
                        throw new UnexpectedSqlResultException($"Database query {query.Name} was expected to provide a return value, but none was returned.");
                    }
                    return (int)prmRtn.Value;
                }
            }
        }

        // if dataName matches output parameter name, then returns that, otherwise looks for column in first row.
        private async Task<(TValue1?, TValue2?, TValue3?)> ExecuteQueryToValueAsync<TValue1, TValue2, TValue3>(Query query, string? data1Name, string? data2Name, string? data3Name, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, CancellationToken cancellationToken)
        {
			cancellationToken.ThrowIfCancellationRequested();
			using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
				using (var cmd = this._dataProviderServices.NewCommand(query.Sql, connection))
				{
					cmd.CommandType = query.Type;
					this._dataProviderServices.SetParameters(cmd, query.ParameterNames, parameters, parameterValues);
                    DbParameter? prmOutput1 = null;
                    DbParameter? prmOutput2 = null;
                    DbParameter? prmOutput3 = null;
                    foreach (DbParameter prm in cmd.Parameters)
                    {
                        if (!string.IsNullOrEmpty(data1Name) 
                            && (prm.Direction == System.Data.ParameterDirection.Output || prm.Direction == System.Data.ParameterDirection.InputOutput) 
                            && prm.ParameterName == data1Name)
                        {
                            prmOutput1 = prm;
                            if (prmOutput2 is not null && prmOutput3 is not null)
                            {
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(data2Name) 
                            && (prm.Direction == System.Data.ParameterDirection.Output || prm.Direction == System.Data.ParameterDirection.InputOutput) 
                            && prm.ParameterName == data2Name)
                        {
                            prmOutput2 = prm;
                            if (prmOutput1 is not null && prmOutput3 is not null)
                            {
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(data3Name) 
                            && (prm.Direction == System.Data.ParameterDirection.Output || prm.Direction == System.Data.ParameterDirection.InputOutput) 
                            && prm.ParameterName == data3Name)
                        {
                            prmOutput3 = prm;
                            if (prmOutput1 is not null && prmOutput2 is not null)
                            {
                                break;
                            }
                        }
                    }
                    TValue1? value1 = default;
                    TValue2? value2 = default;
                    TValue3? value3 = default;
                    if ((prmOutput1 is null && !string.IsNullOrEmpty(data1Name)) || (prmOutput2 is null && !string.IsNullOrEmpty(data2Name)) || (prmOutput3 is null && !string.IsNullOrEmpty(data3Name)))
                    {
                        var rdr = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (rdr.Read())
                        {
                            if ((prmOutput1 is null) && !string.IsNullOrEmpty(data1Name))
                            {
                                value1 = rdr.GetFieldValue<TValue1>(rdr.GetOrdinal(data1Name));
                            }
                            if ((prmOutput2 is null) && !string.IsNullOrEmpty(data2Name))
                            {
                                value2 = rdr.GetFieldValue<TValue2>(rdr.GetOrdinal(data2Name));
                            }
                            if ((prmOutput3 is null) && !string.IsNullOrEmpty(data3Name))
                            {
                                value3 = rdr.GetFieldValue<TValue3>(rdr.GetOrdinal(data3Name));
                            }
                        }
                    }
                    else
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    if (prmOutput1 is not null)
                    {
                        value1 = ReadOutputValue<TValue1>(prmOutput1, query.Name, data1Name);
                    }
                    if (prmOutput2 is not null)
                    {
                        value2 = ReadOutputValue<TValue2>(prmOutput2, query.Name, data2Name);
                    }
                    if (prmOutput3 is not null)
                    {
                        value3 = ReadOutputValue<TValue3>(prmOutput3, query.Name, data3Name);
                    }
                    return (value1, value2, value3);
                }
            }
		}

        // Reads an output parameter value. A null (or DBNull) output yields default(TValue) when TValue can hold null
        // (a reference type or Nullable<T>); for a non-nullable value type it throws UnexpectedSqlResultException.
        internal static TValue? ReadOutputValue<TValue>(DbParameter prmOutput, string queryName, string? dataName)
        {
            if (prmOutput.Value is null || prmOutput.Value == System.DBNull.Value)
            {
                if (default(TValue) is null)
                {
                    return default(TValue);
                }
                throw new UnexpectedSqlResultException($"Database query {queryName} expected to output the value of { dataName }, but the database value was null.");
            }
            return (TValue)prmOutput.Value;
        }

        private async Task<TModel?> ExecuteQueryWithDelegateAsync<TModel, TArg>(TModel? instance, Query query, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, short shardId, QueryResultModelHandler<TArg, TModel> resultHandler, bool isTopOne, TArg optionalArgument, CancellationToken cancellationToken)
		{
			var result = default(TModel);
			cancellationToken.ThrowIfCancellationRequested();
			using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

				cancellationToken.ThrowIfCancellationRequested();
				using (var cmd = this._dataProviderServices.NewCommand(query.Sql, connection))
				{
					cmd.CommandType = query.Type;
					this._dataProviderServices.SetParameters(cmd, query.ParameterNames, parameters, parameterValues);
					var cmdType = System.Data.CommandBehavior.Default;
					if (isTopOne)
					{
						cmdType = System.Data.CommandBehavior.SingleRow;
					}
					using (var dataReader = await cmd.ExecuteReaderAsync(cmdType, cancellationToken).ConfigureAwait(false))
					{
						cancellationToken.ThrowIfCancellationRequested();

						result = resultHandler(instance, shardId, query.Sql, optionalArgument, dataReader, cmd.Parameters, _connectionName, this._logger);
					}
				}
			}
			return result;
		}

		private async Task ExecuteRunAsync(Query query, DbParameterCollection parameters, IDictionary<string, object>? parameterValues, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

				cancellationToken.ThrowIfCancellationRequested();
				using (var cmd = this._dataProviderServices.NewCommand(query.Sql, connection))
				{
					cmd.CommandType = query.Type;
					this._dataProviderServices.SetParameters(cmd, query.ParameterNames, parameters, parameterValues);
					await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

        private async Task<TResult?> ExecuteBatchAsync<TResult>(BatchBase<TResult> batch, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResult? result = default;
            using (var connection = this._dataProviderServices.NewConnection(this._connectionConfig.GetConnectionString(_logger)))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var trn = connection.BeginTransaction())
                {
                    result = await batch.ExecuteAsync(_shardId, connection, trn, _connectionName, _dataProviderServices, _logger, cancellationToken);
                    trn.Commit();
                }
            }
            return result;
        }
        #endregion
    }
}
