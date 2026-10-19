// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// One retry delay the connection manager asked for: the attempt number, the delay the settings computed for it
    /// (the same value a <see cref="SimpleDbConnection"/> with these settings would return) and the delay actually handed back.
    /// </summary>
    public sealed class RetryDelayRequest
    {
        internal RetryDelayRequest(int attempt, TimeSpan computedDelay, TimeSpan appliedDelay)
        {
            Attempt = attempt;
            ComputedDelay = computedDelay;
            AppliedDelay = appliedDelay;
        }

        /// <summary>The 1-based retry attempt the delay precedes (1 is the wait before the first retry, i.e. the second execution).</summary>
        public int Attempt { get; }

        /// <summary>The delay the connection's retry interval and lengthening settings compute for this attempt.</summary>
        public TimeSpan ComputedDelay { get; }

        /// <summary>The delay the connection manager was actually told to wait (see <see cref="FakeConnectionSettings.RetryDelayOverride"/>).</summary>
        public TimeSpan AppliedDelay { get; }

        public override string ToString() => $"attempt {Attempt}: computed {ComputedDelay.TotalMilliseconds} ms, applied {AppliedDelay.TotalMilliseconds} ms";
    }

    /// <summary>
    /// The <see cref="IDataConnection"/> the TestKit builders configure every endpoint with. It holds the same settings as a
    /// <see cref="SimpleDbConnection"/> and computes the same retry delays, but records every delay the connection manager asks
    /// for (<see cref="RetryDelayRequests"/>) and can substitute the delay actually waited (<see cref="RetryDelayOverride"/>),
    /// so a test can assert the spec's 250 ms / 750 ms progression without sleeping for it.
    /// </summary>
    public class FakeConnectionSettings : IDataConnection
    {
        private readonly SimpleDbConnection _inner = new SimpleDbConnection();
        private readonly object _sync = new object();
        private readonly List<RetryDelayRequest> _retryDelays = new List<RetryDelayRequest>();

        public string ConnectionDescription { get => _inner.ConnectionDescription; set => _inner.ConnectionDescription = value; }

        public string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }

        public int? RetryCount { get => _inner.RetryCount; set => _inner.RetryCount = value; }

        public int? CircuitBreakerFailureCount { get => _inner.CircuitBreakerFailureCount; set => _inner.CircuitBreakerFailureCount = value; }

        public int? CircuitBreakerTestInterval { get => _inner.CircuitBreakerTestInterval; set => _inner.CircuitBreakerTestInterval = value; }

        public SequenceLengthening? RetryLengthening { get => _inner.RetryLengthening; set => _inner.RetryLengthening = value; }

        public int? RetryInterval { get => _inner.RetryInterval; set => _inner.RetryInterval = value; }

        /// <summary>
        /// When set, replaces the delay the connection manager actually waits: it receives the attempt number and the computed
        /// delay and returns the delay to apply. The computed delay is still recorded. <see cref="WithoutWaiting"/> sets it to zero.
        /// </summary>
        public Func<int, TimeSpan, TimeSpan>? RetryDelayOverride { get; set; }

        /// <summary>Every retry delay the connection manager asked for, in the order it asked (a snapshot).</summary>
        public IReadOnlyList<RetryDelayRequest> RetryDelayRequests
        {
            get { lock (_sync) { return _retryDelays.ToArray(); } }
        }

        /// <summary>Makes every retry wait zero time while still recording the delay the settings computed.</summary>
        public FakeConnectionSettings WithoutWaiting()
        {
            RetryDelayOverride = (_, __) => TimeSpan.Zero;
            return this;
        }

        /// <summary>Clears the recorded retry delays.</summary>
        public void ClearRetryDelayRequests()
        {
            lock (_sync) { _retryDelays.Clear(); }
        }

        public string GetConnectionString(ILogger? logger) => _inner.GetConnectionString(logger);

        public TimeSpan GetRetryTimespan(int attempt)
        {
            var computed = _inner.GetRetryTimespan(attempt);
            var applied = RetryDelayOverride is null ? computed : RetryDelayOverride(attempt, computed);
            lock (_sync)
            {
                _retryDelays.Add(new RetryDelayRequest(attempt, computed, applied));
            }
            return applied;
        }

        public void SetAmbientConfiguration(DataConnectionConfigurationBase? globalProperties, DataConnectionConfigurationBase? shardSetProperties, DataConnectionConfigurationBase? readWriteProperties, DataConnectionConfigurationBase? shardProperties)
        {
            // Like SimpleDbConnection, the fake settings do not inherit from ambient configuration levels.
        }

        public override string ToString() => ConnectionString ?? string.Empty;
    }
}
