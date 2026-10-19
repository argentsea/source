// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArgentSea
{
    /// <summary>
    /// Determines how the delay before each successive retry grows. Each delay is a multiple of RetryInterval; the default is <see cref="Lengthening"/>.
    /// </summary>
    public enum SequenceLengthening
    {
        /// <summary>
        /// The delay grows linearly: 1, 2, 3, 4… × RetryInterval (attempt × interval).
        /// </summary>
        Linear = 0,

        /// <summary>
        /// The default. The delay grows by odd numbers: 1, 3, 5, 7… × RetryInterval ((2 × attempt − 1) × interval).
        /// </summary>
        Lengthening = 1,

        /// <summary>
        /// The delay grows by half the square of the attempt, using integer division: 0, 2, 4, 8, 12… × RetryInterval ((attempt² / 2) × interval).
        /// </summary>
        HalfSquare = 2,

        /// <summary>
        /// The delay doubles with each attempt: 1, 2, 4, 8… × RetryInterval (2^(attempt − 1) × interval).
        /// </summary>
        Squaring = 3,

        /// <summary>
        /// Obsolete alias of <see cref="Lengthening"/>, retained so existing configuration values and code still bind.
        /// The progression was never Fibonacci; it is 1, 3, 5, 7… × RetryInterval.
        /// </summary>
        [Obsolete("Use Lengthening.")]
        Fibonacci = Lengthening
    }

    /// <summary>
    /// Abstract class implementing the data connection functionality shared by PostgreSQL and SQL Server implementations.
    /// </summary>
    public abstract class DataConnectionConfigurationBase : INotifyPropertyChanged
    {
        /// <summary>
        /// Event indicating that a connection property has changed, which should cause any future connection strings to change. This event is not raise on resiliance (non-connection) properties.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        private const int DefaultRetryInterval = 250;
        private string? _userName;
        private string? _password;
        private bool? _windowsAuth = null;

        /// <summary>
        /// The database login account, if windows auth is not used.
        /// </summary>
        public string? UserName
        {
            get { return _userName; }
            set
            {
                if (_userName != value)
                {
                    _userName = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// The database login password, if windows auth is not used.
        /// </summary>
        public string? Password
        { 
            get { return _password; }
            set
            {
                if (_password != value)
                {
                    _password = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Windows (kerberos) auth should be used, rather than username and password.
        /// </summary>
        public bool? WindowsAuth
        {
            get { return _windowsAuth; }
            set
            {
                if (_windowsAuth != value)
                {
                    _windowsAuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// The number of times to automatically retry when a transient error is encountered. The default is 6. 
        /// Does not raise PropertyChanged event.
        /// </summary>
        public int? RetryCount { get; set; }

        /// <summary>
        /// This is the number of milliseconds to wait before retrying a “retry-able” connection or command error. Default is 250 ms.
        /// This interval may be extended with each retry, depending upon the RetryLengthening setting, up to RetryCount.
        /// Does not raise PropertyChanged event.
        /// </summary>
        public int? RetryInterval { get; set; }

        /// <summary>
        /// If a connection or command fails, this setting determines how much each subsequent retry should be further delayed.
        /// The default is <see cref="SequenceLengthening.Lengthening"/> (1, 3, 5, 7… × RetryInterval).
        /// Does not raise PropertyChanged event.
        /// </summary>
        public SequenceLengthening? RetryLengthening { get; set; }

        /// <summary>
        /// If a connection or command consistantly fails, the circuit breaker will reject all further connections until one suceeds.
        /// This setting determines how many failures (after retries, if retry-able) before blocking all connections apart from a few periodic test attempts.
        /// Does not raise PropertyChanged event.
        /// </summary>
        public int? CircuitBreakerFailureCount { get; set; }

        /// <summary>
        /// If a connection or command fails, the circuit breaker will reject all further connections until one suceeds.
        /// This setting determines how long (in milliseonds) the system should wait before allowing a test connection.
        /// Does not raise PropertyChanged event.
        /// </summary>
        public int? CircuitBreakerTestInterval { get; set; }


        /// <summary>
        /// The configuration levels this connection inherits resilience settings from, most specific first.
        /// Empty unless this instance is a connection whose ambient configuration has been set.
        /// </summary>
        private DataConnectionConfigurationBase[] _ambientResilience = Array.Empty<DataConnectionConfigurationBase>();

        /// <summary>
        /// Records the ambient configuration levels from which the resilience (retry and circuit breaker) settings are inherited.
        /// The precedence matches the connection string: this instance, then the shard (or database), then the read/write settings, then the shard set, then the global settings.
        /// Null levels are ignored. The levels are read on every access, so later changes to them take effect.
        /// </summary>
        protected void SetAmbientResilience(DataConnectionConfigurationBase? globalProperties, DataConnectionConfigurationBase? shardSetProperties, DataConnectionConfigurationBase? readWriteProperties, DataConnectionConfigurationBase? shardProperties)
        {
            var levels = new List<DataConnectionConfigurationBase>(4);
            foreach (var level in new[] { shardProperties, readWriteProperties, shardSetProperties, globalProperties })
            {
                if (!(level is null) && !ReferenceEquals(level, this))
                {
                    levels.Add(level);
                }
            }
            _ambientResilience = levels.ToArray();
        }

        private T? GetInherited<T>(Func<DataConnectionConfigurationBase, T?> getter) where T : struct
        {
            var result = getter(this);
            if (result.HasValue)
            {
                return result;
            }
            foreach (var level in _ambientResilience)
            {
                result = getter(level);
                if (result.HasValue)
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// The RetryCount set on this instance or, if not set, inherited from its ambient configuration.
        /// </summary>
        protected int? InheritedRetryCount => GetInherited(c => c.RetryCount);

        /// <summary>
        /// The CircuitBreakerFailureCount set on this instance or, if not set, inherited from its ambient configuration.
        /// </summary>
        protected int? InheritedCircuitBreakerFailureCount => GetInherited(c => c.CircuitBreakerFailureCount);

        /// <summary>
        /// The CircuitBreakerTestInterval set on this instance or, if not set, inherited from its ambient configuration.
        /// </summary>
        protected int? InheritedCircuitBreakerTestInterval => GetInherited(c => c.CircuitBreakerTestInterval);

        /// <summary>
        /// Returns the delay before the given retry attempt.
        /// RetryInterval and RetryLengthening are inherited from the ambient configuration when they are not set on this instance.
        /// </summary>
        public TimeSpan GetRetryTimespan(int attempt)
        {
            long result;
            var retryLengthening = SequenceLengthening.Lengthening;
            int retryInterval = DefaultRetryInterval;
            var inheritedLengthening = GetInherited(c => c.RetryLengthening);
            if (inheritedLengthening.HasValue)
            {
                retryLengthening = inheritedLengthening.Value;
            }
            var inheritedInterval = GetInherited(c => c.RetryInterval);
            if (inheritedInterval.HasValue)
            {
                retryInterval = inheritedInterval.Value;
            }
            switch (retryLengthening)
            {
                case SequenceLengthening.HalfSquare:
                    result = ((attempt * attempt) / 2) * retryInterval;
                    break;
                case SequenceLengthening.Linear:
                    result = attempt * retryInterval;
                    break;
                case SequenceLengthening.Squaring:
                    result = retryInterval * (long)Math.Pow(2, attempt - 1);
                    break;
                default: //Lengthening (1, 3, 5, 7… × interval) is the default
                    result = (attempt + (attempt - 1)) * retryInterval;
                    break;
            }
            return TimeSpan.FromMilliseconds(result);
        }

        protected void RaisePropertyChanged([CallerMemberName] string caller = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(caller));
        }

    }
}
