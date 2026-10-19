// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// An in-memory connection. Opening completes asynchronously (as a network open does), so concurrent fan-out
    /// really interleaves; commands and transactions created from it report to the owning <see cref="FakeProviderServices"/>.
    /// </summary>
    public class FakeDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        private string? _connectionString;

        internal FakeDbConnection(FakeProviderServices services, string connectionString)
        {
            Services = services;
            ConnectionString = connectionString;
        }

        public FakeProviderServices Services { get; }

        /// <summary>The shard set, shard (or database) and endpoint this connection string encodes.</summary>
        public FakeConnectionInfo Info { get; private set; } = FakeConnectionInfo.Parse(null);

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString ?? string.Empty;
            set
            {
                _connectionString = value;
                Info = FakeConnectionInfo.Parse(value);
            }
        }

        public override string Database => Info?.DatabaseKey ?? Info?.ShardSetName ?? "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "0.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }

        public override void Open()
        {
            _state = ConnectionState.Open;
            Services.Recorder.RecordConnectionOpened(this);
        }

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = Services.Script.OpenDelay;
            if (delay > System.TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
            cancellationToken.ThrowIfCancellationRequested();
            Open();
        }

        public override void Close() => _state = ConnectionState.Closed;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => new FakeDbTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(Services, string.Empty, this);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Close();
            }
            base.Dispose(disposing);
        }
    }
}
