// Copyright John Hicks. Licensed under the MIT license.
using System;

namespace ArgentSea
{
    /// <summary>A non-null binary value has the wrong width for its numeric target.</summary>
    public class InvalidBinaryValueLengthException : InvalidOperationException
    {
        public InvalidBinaryValueLengthException(string name, Type targetType, int expectedWidth, int actualLength)
            : base($"Binary value {name} ({targetType}) requires {expectedWidth} bytes; received {actualLength}.")
        {
            Name = name;
            TargetType = targetType;
            ExpectedWidth = expectedWidth;
            ActualLength = actualLength;
        }
        public string Name { get; }
        public Type TargetType { get; }
        public int ExpectedWidth { get; }
        public int ActualLength { get; }
    }
}
