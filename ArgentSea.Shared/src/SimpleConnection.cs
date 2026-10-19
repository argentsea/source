// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ArgentSea
{
    public class SimpleDbConnection : IDataConnection
    {
        public string ConnectionDescription { get; set; } = string.Empty;

        public string ConnectionString { get; set; } = string.Empty;

        public string GetConnectionString(ILogger? logger)
        {
            return ConnectionString;
        }

        public int? RetryCount { get; set; }

        public int? CircuitBreakerFailureCount { get; set; }

        public int? CircuitBreakerTestInterval { get; set; }

        public SequenceLengthening? RetryLengthening { get; set; }

        public int? RetryInterval { get; set; }

        public TimeSpan GetRetryTimespan(int attempt)
        {
            long result;
            var retryLengthening = SequenceLengthening.Lengthening;
            int retryInterval = 250;
            if (this.RetryLengthening.HasValue)
            {
                retryLengthening = this.RetryLengthening.Value;
            }
            if (this.RetryInterval.HasValue)
            {
                retryInterval = this.RetryInterval.Value;
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

        public void SetAmbientConfiguration(DataConnectionConfigurationBase? notUsed1, DataConnectionConfigurationBase? notUsed2, DataConnectionConfigurationBase? notUsed3, DataConnectionConfigurationBase? notUsed4)
        {
            //
        }
    }
}
