// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.Common;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ArgentSea
{
    public static class BatchExtensions
    {
        #region Shard Extension Methods

        public static ShardBatch<ShardKey<TRecord>> Add<TRecord>(this ShardBatch<ShardKey<TRecord>> batch, Query query, DbParameterCollection parameters, string shardIdColumnName, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyStep<TRecord>(query, parameters, shardIdColumnName, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord>> Add<TRecord>(this ShardBatch<ShardKey<TRecord>> batch, Query query, string shardIdColumnName, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyStep<TRecord>(query, new ParameterCollection(), shardIdColumnName, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord, TChild>> Add<TRecord, TChild>(this ShardBatch<ShardKey<TRecord, TChild>> batch, Query query, DbParameterCollection parameters, string shardIdColumnName, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildStep<TRecord, TChild>(query, parameters, shardIdColumnName, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord, TChild>> Add<TRecord, TChild>(this ShardBatch<ShardKey<TRecord, TChild>> batch, Query query, string shardIdColumnName, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildStep<TRecord, TChild>(query, new ParameterCollection(), shardIdColumnName, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord>> Add<TRecord>(this ShardBatch<ShardKey<TRecord>> batch, Query query, DbParameterCollection parameters, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyStep<TRecord>(query, parameters, null, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord>> Add<TRecord>(this ShardBatch<ShardKey<TRecord>> batch, Query query, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyStep<TRecord>(query, new ParameterCollection(), null, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord, TChild>> Add<TRecord, TChild>(this ShardBatch<ShardKey<TRecord, TChild>> batch, Query query, DbParameterCollection parameters, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildStep<TRecord, TChild>(query, parameters, null, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<ShardKey<TRecord, TChild>> Add<TRecord, TChild>(this ShardBatch<ShardKey<TRecord, TChild>> batch, Query query, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildStep<TRecord, TChild>(query, new ParameterCollection(), null, recordIdColumnName, childIdColumnName));
            return batch;
        }


        public static ShardBatch<TModel> Add<TArg, TModel>(this ShardBatch<TModel> batch, Query query, DbParameterCollection parameters, QueryResultModelHandler<TArg, TModel> resultHandler, TArg optionalArgument) where TModel : new()
        {
            batch.Add(new ModelStep<TArg, TModel>(query, parameters, resultHandler, optionalArgument));
            return batch;
        }
        public static ShardBatch<TModel> Add<TModel>(this ShardBatch<TModel> batch, Query query, DbParameterCollection parameters, QueryResultModelHandler<object?, TModel> resultHandler) where TModel : new()
        {
            batch.Add(new ModelStep<object?, TModel>(query, parameters, resultHandler, null));
            return batch;
        }
        public static ShardBatch<TModel> Add<TArg, TModel>(this ShardBatch<TModel> batch, Query query, QueryResultModelHandler<TArg, TModel> resultHandler, TArg optionalArgument) where TModel : new()
        {
            batch.Add(new ModelStep<TArg, TModel>(query, new ParameterCollection(), resultHandler, optionalArgument));
            return batch;
        }
        public static ShardBatch<TModel> Add<TModel>(this ShardBatch<TModel> batch, Query query, QueryResultModelHandler<object?, TModel> resultHandler) where TModel : new()
        {
            batch.Add(new ModelStep<object?, TModel>(query, new ParameterCollection(), resultHandler, null));
            return batch;
        }

        public static ShardBatch<IList<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<IList<ShardKey<TRecord>>> batch, Query query, DbParameterCollection parameters, string shardIdColumnName, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListStep<TRecord>(query, parameters, shardIdColumnName, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<IList<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<IList<ShardKey<TRecord>>> batch, Query query, string shardIdColumnName, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListStep<TRecord>(query, new ParameterCollection(), shardIdColumnName, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<IList<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<IList<ShardKey<TRecord, TChild>>> batch, Query query, DbParameterCollection parameters, string shardIdColumnName, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListStep<TRecord, TChild>(query, parameters, shardIdColumnName, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<IList<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<IList<ShardKey<TRecord, TChild>>> batch, Query query, string shardIdColumnName, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListStep<TRecord, TChild>(query, new ParameterCollection(), shardIdColumnName, recordIdColumnName, childIdColumnName));
            return batch;
        }

        public static ShardBatch<List<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<List<ShardKey<TRecord>>> batch, Query query, DbParameterCollection parameters, string shardIdColumnName, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListOfListStep<TRecord>(query, parameters, shardIdColumnName, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<List<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<List<ShardKey<TRecord>>> batch, Query query, string shardIdColumnName, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListOfListStep<TRecord>(query, new ParameterCollection(), shardIdColumnName, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<List<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<List<ShardKey<TRecord, TChild>>> batch, Query query, DbParameterCollection parameters, string shardIdColumnName, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListOfListStep<TRecord, TChild>(query, parameters, shardIdColumnName, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<List<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<List<ShardKey<TRecord, TChild>>> batch, Query query, string shardIdColumnName, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListOfListStep<TRecord, TChild>(query, new ParameterCollection(), shardIdColumnName, recordIdColumnName, childIdColumnName));
            return batch;
        }


        public static ShardBatch<IList<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<IList<ShardKey<TRecord>>> batch, Query query, DbParameterCollection parameters, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListStep<TRecord>(query, parameters, null, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<IList<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<IList<ShardKey<TRecord>>> batch, Query query, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListStep<TRecord>(query, new ParameterCollection(), null, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<IList<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<IList<ShardKey<TRecord, TChild>>> batch, Query query, DbParameterCollection parameters, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListStep<TRecord, TChild>(query, parameters, null, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<IList<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<IList<ShardKey<TRecord, TChild>>> batch, Query query, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListStep<TRecord, TChild>(query, new ParameterCollection(), null, recordIdColumnName, childIdColumnName));
            return batch;
        }

        public static ShardBatch<List<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<List<ShardKey<TRecord>>> batch, Query query, DbParameterCollection parameters, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListOfListStep<TRecord>(query, parameters, null, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<List<ShardKey<TRecord>>> Add<TRecord>(this ShardBatch<List<ShardKey<TRecord>>> batch, Query query, string recordIdColumnName) where TRecord : IComparable
        {
            batch.Add(new KeyListOfListStep<TRecord>(query, new ParameterCollection(), null, recordIdColumnName));
            return batch;
        }
        public static ShardBatch<List<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<List<ShardKey<TRecord, TChild>>> batch, Query query, DbParameterCollection parameters, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListOfListStep<TRecord, TChild>(query, parameters, null, recordIdColumnName, childIdColumnName));
            return batch;
        }
        public static ShardBatch<List<ShardKey<TRecord, TChild>>> Add<TRecord, TChild>(this ShardBatch<List<ShardKey<TRecord, TChild>>> batch, Query query, string recordIdColumnName, string childIdColumnName) where TRecord : IComparable where TChild : IComparable
        {
            batch.Add(new ChildListOfListStep<TRecord, TChild>(query, new ParameterCollection(), null, recordIdColumnName, childIdColumnName));
            return batch;
        }



        public static ShardBatch<TRecord> Add<TRecord>(this ShardBatch<TRecord> batch, Query query, DbParameterCollection parameters, string dataColumnName) where TRecord : IComparable
        {
            batch.Add(new RecordStep<TRecord>(query, parameters, dataColumnName));
            return batch;
        }
        public static ShardBatch<TRecord> Add<TRecord>(this ShardBatch<TRecord> batch, Query query, string dataColumnName) where TRecord : IComparable
        {
            batch.Add(new RecordStep<TRecord>(query, new ParameterCollection(), dataColumnName));
            return batch;
        }

        public static ShardBatch<IList<TRecord>> Add<TRecord>(this ShardBatch<IList<TRecord>> batch, Query query, DbParameterCollection parameters, string dataColumnName)
        {
            batch.Add(new ListStep<TRecord>(query, parameters, dataColumnName));
            return batch;
        }
        public static ShardBatch<List<TRecord>> Add<TRecord>(this ShardBatch<List<TRecord>> batch, Query query, DbParameterCollection parameters, string dataColumnName)
        {
            batch.Add(new ListOfListStep<TRecord>(query, parameters, dataColumnName));
            return batch;
        }
        #endregion
        #region Database Extension Methods
        public static DatabaseBatch<TModel> Add<TArg, TModel>(this DatabaseBatch<TModel> batch, Query query, DbParameterCollection parameters, QueryResultModelHandler<TArg, TModel> resultHandler, TArg optionalArgument) where TModel : new()
        {
            batch.Add(new ModelStep<TArg, TModel>(query, parameters, resultHandler, optionalArgument));
            return batch;
        }
        public static DatabaseBatch<TModel> Add<TModel>(this DatabaseBatch<TModel> batch, Query query, DbParameterCollection parameters, QueryResultModelHandler<object?, TModel> resultHandler) where TModel : new()
        {
            batch.Add(new ModelStep<object?, TModel>(query, parameters, resultHandler, null));
            return batch;
        }
        public static DatabaseBatch<TModel> Add<TArg, TModel>(this DatabaseBatch<TModel> batch, Query query, QueryResultModelHandler<TArg, TModel> resultHandler, TArg optionalArgument) where TModel : new()
        {
            batch.Add(new ModelStep<TArg, TModel>(query, new ParameterCollection(), resultHandler, optionalArgument));
            return batch;
        }
        public static DatabaseBatch<TModel> Add<TModel>(this DatabaseBatch<TModel> batch, Query query, QueryResultModelHandler<object?, TModel> resultHandler) where TModel : new()
        {
            batch.Add(new ModelStep<object?, TModel>(query, new ParameterCollection(), resultHandler, null));
            return batch;
        }


        public static DatabaseBatch<TRecord> Add<TRecord>(this DatabaseBatch<TRecord> batch, Query query, DbParameterCollection parameters, string dataColumnName) where TRecord : IComparable
        {
            batch.Add(new RecordStep<TRecord>(query, parameters, dataColumnName));
            return batch;
        }
        public static DatabaseBatch<TRecord> Add<TRecord>(this DatabaseBatch<TRecord> batch, Query query, string dataColumnName) where TRecord : IComparable
        {
            batch.Add(new RecordStep<TRecord>(query, new ParameterCollection(), dataColumnName));
            return batch;
        }

        public static DatabaseBatch<IList<TRecord>> Add<TRecord>(this DatabaseBatch<IList<TRecord>> batch, Query query, DbParameterCollection parameters, string dataColumnName)
        {
            batch.Add(new ListStep<TRecord>(query, parameters, dataColumnName));
            return batch;
        }
        public static DatabaseBatch<List<TRecord>> Add<TRecord>(this DatabaseBatch<List<TRecord>> batch, Query query, DbParameterCollection parameters, string dataColumnName)
        {
            batch.Add(new ListOfListStep<TRecord>(query, parameters, dataColumnName));
            return batch;
        }
        #endregion
        #region ShardSet Extension Methods


        #endregion
        #region Private classes
        private class KeyStep<TRecord> : BatchStep<ShardKey<TRecord>> where TRecord : IComparable
        {
            private readonly string? _shardIdColumnName;
            private readonly string _recordIdColumnName;
            private readonly Query _query;
            private readonly DbParameterCollection _parameters;

            public KeyStep(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName)
            {
                _query = query;
                _parameters = parameters;
                _shardIdColumnName = shardIdColumnName;
                _recordIdColumnName = recordIdColumnName;
            }
            protected internal override async Task<ShardKey<TRecord>> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var shardOrd = -1;
                        short shardIdData = shardId;
                        if (_shardIdColumnName is not null)
                        {
                            shardOrd = dataReader.GetOrdinal(_shardIdColumnName);
                        }
                        var recordOrd = dataReader.GetOrdinal(_recordIdColumnName);
                        if (dataReader.Read())
                        {
                            if (shardOrd != -1)
                            {
                                shardIdData = dataReader.GetFieldValue<short>(shardOrd);
                            }
                            var recordid = dataReader.GetFieldValue<TRecord>(recordOrd);
                            return new ShardKey<TRecord>(shardIdData, recordid);
                        }
                    }
                }
                return ShardKey<TRecord>.Empty;
            }
        }
        private class ChildStep<TRecord, TChildId> : BatchStep<ShardKey<TRecord, TChildId>> where TRecord : IComparable where TChildId : IComparable
        {
            private readonly string? _shardIdColumnName;
            private readonly string _recordIdColumnName;
            private readonly string _childIdColumnName;
            private readonly Query _query;
            private readonly DbParameterCollection _parameters;

            public ChildStep(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName, string childIdColumnName)
            {
                _query = query;
                _parameters = parameters;
                _shardIdColumnName = shardIdColumnName;
                _recordIdColumnName = recordIdColumnName;
                _childIdColumnName = childIdColumnName;
            }

            protected internal override async Task<ShardKey<TRecord, TChildId>> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var shardOrd = -1;
                        var shardIdData = shardId;
                        if (_shardIdColumnName is not null)
                        {
                            shardOrd = dataReader.GetOrdinal(_shardIdColumnName);
                        }
                        var recordOrd = dataReader.GetOrdinal(_recordIdColumnName);
                        var childOrd = dataReader.GetOrdinal(_childIdColumnName);
                        if (dataReader.Read())
                        {
                            if (shardOrd != -1)
                            {
                                shardIdData = dataReader.GetFieldValue<short>(shardOrd);
                            }
                            var recordid = dataReader.GetFieldValue<TRecord>(recordOrd);
                            var childid = dataReader.GetFieldValue<TChildId>(childOrd);
                            return new ShardKey<TRecord, TChildId>(shardIdData, recordid, childid);
                        }
                    }
                }
                return ShardKey<TRecord, TChildId>.Empty;
            }
        }
        private class ModelStep<TArg, TModel> : BatchStep<TModel> where TModel : new()
        {
            private readonly bool _isTopOne;
            private readonly Query _query;
            private readonly QueryResultModelHandler<TArg, TModel> _resultHandler;
            private readonly DbParameterCollection _parameters;
            private readonly TArg _optionalArgument;
            public ModelStep(Query query, DbParameterCollection parameters, QueryResultModelHandler<TArg, TModel> resultHandler, bool isTopOne, TArg optionalArgument)
            {
                _isTopOne = isTopOne;
                _query = query;
                _resultHandler = resultHandler;
                _parameters = parameters;
                _optionalArgument = optionalArgument;
            }
            public ModelStep(Query query, DbParameterCollection parameters, QueryResultModelHandler<TArg, TModel> resultHandler, TArg optionalArgument)
            {
                _isTopOne = false;
                _query = query;
                _resultHandler = resultHandler;
                _parameters = parameters;
                _optionalArgument = optionalArgument;
            }

            protected internal override async Task<TModel?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
            {
                TModel? result = default;
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    var cmdType = System.Data.CommandBehavior.Default;
                    if (_isTopOne)
                    {
                        cmdType = System.Data.CommandBehavior.SingleRow;
                    }
                    using (var dataReader = await cmd.ExecuteReaderAsync(cmdType, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // The handler populates a new instance (as ShardSet.ReadFirst does), so the Mapper handlers can be used in batches.
                        result = _resultHandler(new TModel(), shardId, _query.Sql, _optionalArgument, dataReader, cmd.Parameters, connectionName, logger);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Reads every row of a query into a list of shard keys. Shared by the IList&lt;&gt;- and List&lt;&gt;-typed key list steps,
        /// which differ only in the result type their batch is declared with.
        /// </summary>
        private sealed class KeyListReader<TRecord> where TRecord : IComparable
        {
            private readonly string? _shardIdColumnName;
            private readonly string _recordIdColumnName;
            private readonly Query _query;
            private readonly DbParameterCollection _parameters;

            public KeyListReader(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName)
            {
                _query = query;
                _parameters = parameters;
                _shardIdColumnName = shardIdColumnName;
                _recordIdColumnName = recordIdColumnName;
            }
            public async Task<List<ShardKey<TRecord>>> ReadAsync(short shardId, DbConnection connection, DbTransaction transaction, IDataProviderServiceFactory services, CancellationToken cancellationToken)
            {
                var result = new List<ShardKey<TRecord>>();
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var shardOrd = -1;
                        var shardIdData = shardId;
                        if (_shardIdColumnName is not null)
                        {
                            shardOrd = dataReader.GetOrdinal(_shardIdColumnName);
                        }
                        var recordOrd = dataReader.GetOrdinal(_recordIdColumnName);
                        while (dataReader.Read())
                        {
                            if (shardOrd != -1)
                            {
                                shardIdData = dataReader.GetFieldValue<short>(shardOrd);
                            }
                            var recordid = dataReader.GetFieldValue<TRecord>(recordOrd);
                            result.Add(new ShardKey<TRecord>(shardIdData, recordid));
                        }
                    }
                }
                return result;
            }
        }

        private class KeyListStep<TRecord> : BatchStep<IList<ShardKey<TRecord>>> where TRecord : IComparable
        {
            private readonly KeyListReader<TRecord> _reader;

            public KeyListStep(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName)
                => _reader = new KeyListReader<TRecord>(query, parameters, shardIdColumnName, recordIdColumnName);

            protected internal override async Task<IList<ShardKey<TRecord>>?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
                => await _reader.ReadAsync(shardId, connection, transaction, services, cancellationToken).ConfigureAwait(false);
        }

        private class KeyListOfListStep<TRecord> : BatchStep<List<ShardKey<TRecord>>> where TRecord : IComparable
        {
            private readonly KeyListReader<TRecord> _reader;

            public KeyListOfListStep(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName)
                => _reader = new KeyListReader<TRecord>(query, parameters, shardIdColumnName, recordIdColumnName);

            protected internal override async Task<List<ShardKey<TRecord>>?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
                => await _reader.ReadAsync(shardId, connection, transaction, services, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads every row of a query into a list of two-part shard keys. Shared by the IList&lt;&gt;- and List&lt;&gt;-typed child list steps.
        /// </summary>
        private sealed class ChildListReader<TRecord, TChild> where TRecord : IComparable where TChild : IComparable
        {
            private readonly string? _shardIdColumnName;
            private readonly string _recordIdColumnName;
            private readonly string _childIdColumnName;
            private readonly Query _query;
            private readonly DbParameterCollection _parameters;

            public ChildListReader(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName, string childIdColumnName)
            {
                _query = query;
                _parameters = parameters;
                _shardIdColumnName = shardIdColumnName;
                _recordIdColumnName = recordIdColumnName;
                _childIdColumnName = childIdColumnName;
            }

            public async Task<List<ShardKey<TRecord, TChild>>> ReadAsync(short shardId, DbConnection connection, DbTransaction transaction, IDataProviderServiceFactory services, CancellationToken cancellationToken)
            {
                var result = new List<ShardKey<TRecord, TChild>>();
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var shardOrd = -1;
                        var shardIdData = shardId;
                        if (_shardIdColumnName is not null)
                        {
                            shardOrd = dataReader.GetOrdinal(_shardIdColumnName);
                        }
                        var recordOrd = dataReader.GetOrdinal(_recordIdColumnName);
                        var childOrd = dataReader.GetOrdinal(_childIdColumnName);
                        while (dataReader.Read())
                        {
                            if (shardOrd != -1)
                            {
                                shardIdData = dataReader.GetFieldValue<short>(shardOrd);
                            }
                            var recordid = dataReader.GetFieldValue<TRecord>(recordOrd);
                            var childid = dataReader.GetFieldValue<TChild>(childOrd);
                            result.Add(new ShardKey<TRecord, TChild>(shardIdData, recordid, childid));
                        }
                    }
                }
                return result;
            }
        }

        private class ChildListStep<TRecord, TChild> : BatchStep<IList<ShardKey<TRecord, TChild>>> where TRecord : IComparable where TChild : IComparable
        {
            private readonly ChildListReader<TRecord, TChild> _reader;

            public ChildListStep(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName, string childIdColumnName)
                => _reader = new ChildListReader<TRecord, TChild>(query, parameters, shardIdColumnName, recordIdColumnName, childIdColumnName);

            protected internal override async Task<IList<ShardKey<TRecord, TChild>>?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
                => await _reader.ReadAsync(shardId, connection, transaction, services, cancellationToken).ConfigureAwait(false);
        }

        private class ChildListOfListStep<TRecord, TChild> : BatchStep<List<ShardKey<TRecord, TChild>>> where TRecord : IComparable where TChild : IComparable
        {
            private readonly ChildListReader<TRecord, TChild> _reader;

            public ChildListOfListStep(Query query, DbParameterCollection parameters, string? shardIdColumnName, string recordIdColumnName, string childIdColumnName)
                => _reader = new ChildListReader<TRecord, TChild>(query, parameters, shardIdColumnName, recordIdColumnName, childIdColumnName);

            protected internal override async Task<List<ShardKey<TRecord, TChild>>?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
                => await _reader.ReadAsync(shardId, connection, transaction, services, cancellationToken).ConfigureAwait(false);
        }

        private class RecordStep<TRecord> : BatchStep<TRecord> where TRecord : IComparable
        {
            private readonly string _recordIdColumnName;
            private readonly Query _query;
            private readonly DbParameterCollection _parameters;

            public RecordStep(Query query, DbParameterCollection parameters, string recordIdColumnName)
            {
                _query = query;
                _parameters = parameters;
                _recordIdColumnName = recordIdColumnName;
            }

            protected internal override async Task<TRecord?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var recordOrd = dataReader.GetOrdinal(_recordIdColumnName);
                        if (dataReader.Read())
                        {
                            var recordid = dataReader.GetFieldValue<TRecord>(recordOrd);
                            return recordid;
                        }
                    }
                }
                return default;
            }
        }

        /// <summary>
        /// Reads one column of every row of a query into a list. Shared by the IList&lt;&gt;- and List&lt;&gt;-typed list steps.
        /// </summary>
        private sealed class ListReader<TRecord>
        {
            private readonly string _dataColumnName;
            private readonly Query _query;
            private readonly DbParameterCollection _parameters;

            public ListReader(Query query, DbParameterCollection parameters, string dataColumnName)
            {
                _query = query;
                _parameters = parameters;
                _dataColumnName = dataColumnName;
            }

            public async Task<List<TRecord>> ReadAsync(DbConnection connection, DbTransaction transaction, IDataProviderServiceFactory services, CancellationToken cancellationToken)
            {
                var result = new List<TRecord>();
                cancellationToken.ThrowIfCancellationRequested();
                using (var cmd = services.NewCommand(_query.Sql, connection))
                {
                    cmd.CommandType = _query.Type;
                    cmd.Transaction = transaction;
                    services.SetParameters(cmd, _query.ParameterNames, _parameters, null);
                    using (var dataReader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default, cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var recordOrd = dataReader.GetOrdinal(_dataColumnName);
                        while (dataReader.Read())
                        {
                            result.Add(dataReader.GetFieldValue<TRecord>(recordOrd));
                        }
                    }
                }
                return result;
            }
        }

        private class ListStep<TRecord> : BatchStep<IList<TRecord>>
        {
            private readonly ListReader<TRecord> _reader;

            public ListStep(Query query, DbParameterCollection parameters, string dataColumnName)
                => _reader = new ListReader<TRecord>(query, parameters, dataColumnName);

            protected internal override async Task<IList<TRecord>?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
                => await _reader.ReadAsync(connection, transaction, services, cancellationToken).ConfigureAwait(false);
        }

        private class ListOfListStep<TRecord> : BatchStep<List<TRecord>>
        {
            private readonly ListReader<TRecord> _reader;

            public ListOfListStep(Query query, DbParameterCollection parameters, string dataColumnName)
                => _reader = new ListReader<TRecord>(query, parameters, dataColumnName);

            protected internal override async Task<List<TRecord>?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
                => await _reader.ReadAsync(connection, transaction, services, cancellationToken).ConfigureAwait(false);
        }
        #endregion
    }
}