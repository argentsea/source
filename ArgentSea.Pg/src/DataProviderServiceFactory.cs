// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using Npgsql;
using System.Threading;

namespace ArgentSea.Pg
{
    /// <summary>
    /// This class is a provider-specific resouce to enable provider-neutral code to execute. It is unlikely that you would reference this in consumer code.
    /// </summary>
	public class DataProviderServiceFactory : IDataProviderServiceFactory
	{
		public bool GetIsErrorTransient(Exception exception)
        {
            if (exception is NpgsqlException ex)
            {
                return ex.IsTransient;
            }
            return false;
        }

        public DbCommand NewCommand(string storedProcedureName, DbConnection connection)
        {
            if (connection is NpgsqlConnection)
            {
                return new NpgsqlCommand(storedProcedureName, (NpgsqlConnection)connection);
            }
            throw new ArgumentException("The connection must be an NpgsqlConnection.", nameof(connection));
        }

        public DbConnection NewConnection(string connectionString)
        {
            return new NpgsqlConnection(connectionString);
        }

        public void SetParameters(DbCommand cmd, string[]? queryParameterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues)
        {
            // Each target parameter is a clone of its source parameter, so it carries the source value;
            // an entry in parameterValues (keyed by parameter name) replaces that value.
            if (queryParameterNames is null || queryParameterNames.Length == 0)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    var npgSourcePrm = (NpgsqlParameter)parameters[i];
                    var targetPrm = npgSourcePrm.Clone();
                    if (parameterValues is not null)
                    {
                        if (parameterValues.TryGetValue(targetPrm.ParameterName, out var prmValue))
                        {
                            targetPrm.Value = prmValue;
                        }
                    }
                    cmd.Parameters.Add(targetPrm);
                }
            }
            else
            {
                for (int i = 0; i < queryParameterNames.Length; i++)
                {
                    var found = false;
                    for (int j = 0; j < parameters.Count; j++)
                    {
                        if (queryParameterNames[i] == parameters[j].ParameterName)
                        {
                            found = true;
                            var npgSourcePrm = (NpgsqlParameter)parameters[j];
                            var targetPrm = npgSourcePrm.Clone();
                            if (parameterValues is not null)
                            {
                                if (parameterValues.TryGetValue(queryParameterNames[i], out var prmValue))
                                {
                                    targetPrm.Value = prmValue;
                                }
                            }
                            cmd.Parameters.Add(targetPrm);
                            // First source parameter with a matching name wins; duplicates are ignored.
                            break;
                        }
                    }
                    if (!found)
                    {
                        throw new ParameterNotFoundException(queryParameterNames[i]);
                    }
                }
            }
            if (!IsMultiplexing(cmd.Connection!.ConnectionString)) // NewCommand always binds the command to its connection
            {
                ((NpgsqlCommand)cmd).Prepare();
            }
        }

        /// <summary>
        /// Returns true when the connection string enables Npgsql multiplexing (explicit command preparation is not supported with multiplexing).
        /// Parsing uses NpgsqlConnectionStringBuilder, so key spelling, casing, and whitespace follow Npgsql's own rules.
        /// </summary>
        internal static bool IsMultiplexing(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return false;
            }
            return new NpgsqlConnectionStringBuilder(connectionString).Multiplexing;
        }
    }
}
