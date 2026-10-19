// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// An in-memory <see cref="IDataProviderServiceFactory"/>. Script what each query returns through <see cref="Script"/>,
    /// run ArgentSea against a <see cref="FakeShardSets"/> or <see cref="FakeDatabases"/> built over it, then inspect
    /// what was executed through <see cref="Recorder"/>.
    /// </summary>
    public class FakeProviderServices : IDataProviderServiceFactory
    {
        private readonly ConditionalWeakTable<Exception, StrongBox<bool>> _transientFlags = new ConditionalWeakTable<Exception, StrongBox<bool>>();

        public ResultScript Script { get; } = new ResultScript();

        public CommandRecorder Recorder { get; } = new CommandRecorder();

        /// <summary>
        /// Overrides the transient test. By default an exception is transient when it is a <see cref="FakeDbException"/>
        /// with <see cref="FakeDbException.IsTransient"/> set, or was scripted with the transient flag.
        /// </summary>
        public Func<Exception, bool>? IsTransient { get; set; }

        public bool GetIsErrorTransient(Exception exception)
        {
            if (!(IsTransient is null))
            {
                return IsTransient(exception);
            }
            if (exception is null)
            {
                return false;
            }
            if (_transientFlags.TryGetValue(exception, out var flagged))
            {
                return flagged.Value;
            }
            return exception is FakeDbException fake && fake.IsTransient;
        }

        public DbConnection NewConnection(string connectionString) => new FakeDbConnection(this, connectionString);

        public DbCommand NewCommand(string storedProcedureName, DbConnection connection)
        {
            if (!(connection is FakeDbConnection))
            {
                throw new ArgumentException("The connection must be a FakeDbConnection created by this provider.", nameof(connection));
            }
            return new FakeDbCommand(this, storedProcedureName, connection);
        }

        /// <summary>
        /// Copies the caller's parameters into the command as independent <see cref="FakeDbParameter"/>s, applying any
        /// per-execution value overrides. When the query names its parameters, only those are copied (first match wins) and a
        /// missing one throws <see cref="ParameterNotFoundException"/>, as the real providers do.
        /// </summary>
        public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues)
        {
            if (parmeterNames is null || parmeterNames.Length == 0)
            {
                for (var i = 0; i < parameters.Count; i++)
                {
                    cmd.Parameters.Add(Bind(parameters[i], parameterValues));
                }
                return;
            }
            foreach (var requested in parmeterNames)
            {
                var found = false;
                for (var i = 0; i < parameters.Count; i++)
                {
                    if (NamesMatch(parameters[i].ParameterName, requested))
                    {
                        cmd.Parameters.Add(Bind(parameters[i], parameterValues));
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    throw new ParameterNotFoundException(requested);
                }
            }
        }

        internal void MarkTransient(Exception exception, bool isTransient)
        {
            if (exception is null)
            {
                return;
            }
            _transientFlags.AddOrUpdate(exception, new StrongBox<bool>(isTransient));
        }

        private static FakeDbParameter Bind(DbParameter source, IDictionary<string, object>? parameterValues)
        {
            var target = FakeDbParameter.CopyOf(source);
            if (!(parameterValues is null) && !(target.ParameterName is null))
            {
                if (parameterValues.TryGetValue(target.ParameterName, out var value))
                {
                    target.Value = value;
                }
                else
                {
                    var alternate = target.ParameterName.StartsWith("@", StringComparison.Ordinal) ? target.ParameterName.Substring(1) : "@" + target.ParameterName;
                    if (parameterValues.TryGetValue(alternate, out value))
                    {
                        target.Value = value;
                    }
                }
            }
            return target;
        }

        private static bool NamesMatch(string declared, string requested)
        {
            if (declared is null || requested is null)
            {
                return false;
            }
            return string.Equals(declared.TrimStart('@'), requested.TrimStart('@'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
