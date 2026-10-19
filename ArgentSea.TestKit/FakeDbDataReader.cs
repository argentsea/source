// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace ArgentSea.TestKit
{
    /// <summary>One typed read made against a <see cref="FakeDbDataReader"/>: which result set and row it was on, the ordinal and the type requested.</summary>
    public sealed class FieldValueRequest
    {
        internal FieldValueRequest(int resultSet, int row, int ordinal, Type requestedType)
        {
            ResultSet = resultSet;
            Row = row;
            Ordinal = ordinal;
            RequestedType = requestedType;
        }

        /// <summary>The 0-based result set index.</summary>
        public int ResultSet { get; }

        /// <summary>The 0-based row index within the result set.</summary>
        public int Row { get; }

        public int Ordinal { get; }

        /// <summary>The generic argument the reader was asked for (<c>T</c> in <c>GetFieldValue&lt;T&gt;</c>).</summary>
        public Type RequestedType { get; }

        public override string ToString() => $"result {ResultSet} row {Row} ordinal {Ordinal} as {RequestedType.Name}";
    }

    /// <summary>
    /// A forward-only reader over scripted <see cref="FakeResultSet"/>s. Values are returned as scripted; <see cref="GetFieldValue{T}"/>
    /// converts between compatible primitive types (an <c>int</c> literal can be read as a <c>long</c> or <c>short</c>) and treats
    /// null and <see cref="DBNull"/> alike.
    /// </summary>
    public class FakeDbDataReader : DbDataReader
    {
        private readonly IReadOnlyList<FakeResultSet> _resultSets;
        private readonly List<FieldValueRequest> _fieldValueRequests = new List<FieldValueRequest>();
        private readonly List<int> _isDbNullRequests = new List<int>();
        private int _resultIndex;
        private int _rowIndex = -1;
        private bool _closed;

        /// <summary>Every typed read (<see cref="GetFieldValue{T}"/> and the Get* methods built on it) made against this reader, in order, with the requested type.</summary>
        public IReadOnlyList<FieldValueRequest> FieldValueRequests => _fieldValueRequests;

        /// <summary>Every ordinal passed to <see cref="IsDBNull"/>, in order.</summary>
        public IReadOnlyList<int> IsDBNullRequests => _isDbNullRequests;

        /// <summary>The 0-based index of the result set the reader is positioned on (equal to the result-set count once the reader has moved past the last one).</summary>
        public int CurrentResultSet => _resultIndex;

        /// <summary>How many times <see cref="NextResult"/> (or <see cref="NextResultAsync"/>) has been called.</summary>
        public int NextResultCalls { get; private set; }

        public FakeDbDataReader(IReadOnlyList<FakeResultSet> resultSets, int recordsAffected = -1)
        {
            _resultSets = resultSets ?? Array.Empty<FakeResultSet>();
            RecordsAffected = recordsAffected;
        }

        private FakeResultSet Current => _resultIndex < _resultSets.Count ? _resultSets[_resultIndex] : FakeResultSet.Empty;

        private object?[] CurrentRow
        {
            get
            {
                if (_rowIndex < 0 || _rowIndex >= Current.Rows.Count)
                {
                    throw new InvalidOperationException("No data exists for the row/column; call Read() first.");
                }
                return Current.Rows[_rowIndex];
            }
        }

        public override int Depth => 0;

        public override int FieldCount => Current.Columns.Count;

        public override bool HasRows => Current.Rows.Count > 0;

        public override bool IsClosed => _closed;

        public override int RecordsAffected { get; }

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        public override string GetName(int ordinal) => Current.Columns[ordinal];

        public override int GetOrdinal(string name)
        {
            var columns = Current.Columns;
            for (var i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            for (var i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            throw new IndexOutOfRangeException($"The column '{name}' is not in the scripted result set.");
        }

        public override Type GetFieldType(int ordinal)
        {
            var declared = Current.ColumnTypes[ordinal];
            if (!(declared is null))
            {
                return declared;
            }
            foreach (var row in Current.Rows)
            {
                var value = row[ordinal];
                if (!(value is null) && !(value is DBNull))
                {
                    return value.GetType();
                }
            }
            return typeof(object);
        }

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override object GetValue(int ordinal)
        {
            var value = CurrentRow[ordinal];
            return value ?? DBNull.Value;
        }

        public override int GetValues(object[] values)
        {
            var row = CurrentRow;
            var count = Math.Min(values.Length, row.Length);
            for (var i = 0; i < count; i++)
            {
                values[i] = row[i] ?? DBNull.Value;
            }
            return count;
        }

        public override bool IsDBNull(int ordinal)
        {
            _isDbNullRequests.Add(ordinal);
            var value = CurrentRow[ordinal];
            return value is null || value is DBNull;
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            _fieldValueRequests.Add(new FieldValueRequest(_resultIndex, _rowIndex, ordinal, typeof(T)));
            var value = CurrentRow[ordinal];
            var targetType = typeof(T);
            if (value is null || value is DBNull)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                {
                    return default!; // DbDataReader.GetFieldValue<T> returns default for a null column when T can hold null (mirrors SqlDataReader)
                }
                throw new System.Data.SqlTypes.SqlNullValueException($"Column {GetName(ordinal)} is null and cannot be read as {targetType.Name}.");
            }
            if (value is T typed)
            {
                return typed;
            }
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying.IsEnum)
            {
                return (T)Enum.ToObject(underlying, value);
            }
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying))
            {
                try
                {
                    return (T)Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
                {
                    // A real provider reader throws InvalidCastException when the column's type does not match the requested type.
                    throw new InvalidCastException($"Column {GetName(ordinal)} holds a {value.GetType().Name} ({value}) and cannot be read as {targetType.Name}.", ex);
                }
            }
            if (value is T)
            {
                return (T)value;
            }
            throw new InvalidCastException($"Column {GetName(ordinal)} holds a {value.GetType().Name} and cannot be read as {targetType.Name}.");
        }

        public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);
        public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);
        public override char GetChar(int ordinal) => GetFieldValue<char>(ordinal);
        public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);
        public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);
        public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);
        public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);
        public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);
        public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);
        public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);
        public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);
        public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        {
            var bytes = GetFieldValue<byte[]>(ordinal);
            if (buffer is null)
            {
                return bytes.Length;
            }
            var count = (int)Math.Min(length, bytes.Length - dataOffset);
            Array.Copy(bytes, dataOffset, buffer, bufferOffset, count);
            return count;
        }

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        {
            var chars = GetFieldValue<string>(ordinal).ToCharArray();
            if (buffer is null)
            {
                return chars.Length;
            }
            var count = (int)Math.Min(length, chars.Length - dataOffset);
            Array.Copy(chars, dataOffset, buffer, bufferOffset, count);
            return count;
        }

        public override IEnumerator GetEnumerator() => new DbEnumerator(this);

        public override bool NextResult()
        {
            NextResultCalls++;
            if (_resultIndex + 1 < _resultSets.Count)
            {
                _resultIndex++;
                _rowIndex = -1;
                return true;
            }
            _resultIndex = _resultSets.Count;
            _rowIndex = -1;
            return false;
        }

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(NextResult());
        }

        public override bool Read()
        {
            if (_rowIndex + 1 < Current.Rows.Count)
            {
                _rowIndex++;
                return true;
            }
            _rowIndex = Current.Rows.Count;
            return false;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read());
        }

        public override void Close() => _closed = true;

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
