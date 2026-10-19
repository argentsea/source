// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Data.Common;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// A provider exception the fake provider can throw on request. <see cref="IsTransient"/> is what
    /// <see cref="FakeProviderServices.GetIsErrorTransient"/> reports, so it decides whether the retry policy retries.
    /// </summary>
    public class FakeDbException : DbException
    {
        public FakeDbException(string message, bool isTransient = false, int errorCode = 0)
            : base(message, errorCode)
        {
            IsTransient = isTransient;
        }

        public FakeDbException(string message, Exception innerException, bool isTransient = false)
            : base(message, innerException)
        {
            IsTransient = isTransient;
        }

        public override bool IsTransient { get; }
    }
}
