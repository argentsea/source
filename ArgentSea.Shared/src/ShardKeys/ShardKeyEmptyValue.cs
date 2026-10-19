// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;

namespace ArgentSea
{
    /// <summary>
    /// The "empty" value of a single ShardKey key part. Every ShardKey arity uses this, so Empty and IsEmpty agree for every key type.
    /// String and binary keys use a zero-length value rather than null; a null key part (as in an uninitialized struct) also counts as empty.
    /// </summary>
    internal static class ShardKeyEmptyValue<T> where T : IComparable
    {
        private static readonly Lazy<T> _lazyValue = new Lazy<T>(() =>
            ShardKeySerialization.GetKeyDataType(typeof(T)) switch
            {
                KeyDataType.String => (T)(object)string.Empty,
                KeyDataType.Blob => (T)(object)Array.Empty<byte>(),
                _ => default!, // the only reference-type key parts, string and byte[], are handled above
            }
        );

        public static T Value => _lazyValue.Value;

        public static bool IsEmpty(T keyPart) => keyPart is null || keyPart.CompareTo(_lazyValue.Value) == 0;
    }
}
