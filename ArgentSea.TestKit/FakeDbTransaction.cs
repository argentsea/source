// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Data;
using System.Data.Common;
using System.Threading;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// A transaction that records its begin, commit and rollback in the <see cref="CommandRecorder"/>.
    /// Disposing a transaction that was neither committed nor rolled back records a rollback, as the real providers roll back on dispose.
    /// </summary>
    public class FakeDbTransaction : DbTransaction
    {
        private static int _nextId;
        private readonly FakeDbConnection _connection;
        private bool _completed;

        internal FakeDbTransaction(FakeDbConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection;
            IsolationLevel = isolationLevel;
            Id = Interlocked.Increment(ref _nextId);
            _connection.Services.Recorder.RecordTransaction(this, TransactionEventKind.Begin);
        }

        /// <summary>A process-wide unique id, so recorded commands can be matched to the transaction they ran in.</summary>
        public int Id { get; }

        public bool IsCommitted { get; private set; }

        public bool IsRolledBack { get; private set; }

        public override IsolationLevel IsolationLevel { get; }

        protected override DbConnection DbConnection => _connection;

        public override void Commit()
        {
            if (_completed)
            {
                throw new InvalidOperationException("This transaction has already been completed.");
            }
            _completed = true;
            IsCommitted = true;
            _connection.Services.Recorder.RecordTransaction(this, TransactionEventKind.Commit);
        }

        public override void Rollback()
        {
            if (_completed)
            {
                throw new InvalidOperationException("This transaction has already been completed.");
            }
            _completed = true;
            IsRolledBack = true;
            _connection.Services.Recorder.RecordTransaction(this, TransactionEventKind.Rollback);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                IsRolledBack = true;
                _connection.Services.Recorder.RecordTransaction(this, TransactionEventKind.Rollback, implicitRollback: true);
            }
            base.Dispose(disposing);
        }
    }
}
