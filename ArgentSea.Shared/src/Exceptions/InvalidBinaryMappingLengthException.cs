// Copyright John Hicks. Licensed under the MIT license.
using System;

namespace ArgentSea
{
    /// <summary>A numeric binary mapping declares an incompatible storage length.</summary>
    public class InvalidBinaryMappingLengthException : ArgumentException
    {
        public InvalidBinaryMappingLengthException(string name, Type targetType, int expectedWidth, int configuredLength, string sqlTypeName)
            : base($"Mapping {name} ({targetType}) requires {expectedWidth} bytes; {sqlTypeName} has configured length {configuredLength}.", nameof(configuredLength))
        {
            Name = name;
            TargetType = targetType;
            ExpectedWidth = expectedWidth;
            ConfiguredLength = configuredLength;
            SqlTypeName = sqlTypeName;
        }
        public string Name { get; }
        public Type TargetType { get; }
        public int ExpectedWidth { get; }
        public int ConfiguredLength { get; }
        public string SqlTypeName { get; }
    }
}
