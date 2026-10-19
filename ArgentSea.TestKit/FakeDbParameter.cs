// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// A plain <see cref="DbParameter"/>. The fake provider copies every source parameter into one of these when it binds a command,
    /// so a command's parameters are independent of the caller's collection (as the real providers' are).
    /// </summary>
    public class FakeDbParameter : DbParameter
    {
        public FakeDbParameter() { }

        public FakeDbParameter(string parameterName, object? value, ParameterDirection direction = ParameterDirection.Input)
        {
            ParameterName = parameterName;
            Value = value;
            Direction = direction;
        }

        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override byte Precision { get; set; }
        public override byte Scale { get; set; }
        public override void ResetDbType() => DbType = DbType.Object;

        internal static FakeDbParameter CopyOf(DbParameter source)
            => new FakeDbParameter
            {
                DbType = source.DbType,
                Direction = source.Direction,
                IsNullable = source.IsNullable,
                ParameterName = source.ParameterName,
                Size = source.Size,
                SourceColumn = source.SourceColumn,
                SourceColumnNullMapping = source.SourceColumnNullMapping,
                Value = source.Value,
                Precision = source.Precision,
                Scale = source.Scale
            };

        public override string ToString() => $"{ParameterName}={Value ?? "null"} ({Direction})";
    }
}
