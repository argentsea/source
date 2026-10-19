// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// Provider-neutral mapping attributes that route through the same <see cref="ExpressionHelpers"/> builders the real
    /// providers use for strings and enums, enum-backed integers, floating-point and Guid sentinels, binary values and arrays.
    /// <see cref="FakeMapAttribute"/> covers the simple-value path; these cover the remaining builder families so the core
    /// mapper's expression builders can be asserted without a SQL Server or PostgreSQL package.
    /// </summary>
    public abstract class FakeTypedMapAttribute : ParameterMapAttributeBase
    {
        protected FakeTypedMapAttribute(string name, int sqlType, bool isRequired) : base(name, sqlType, isRequired)
        {
        }

        public override string ParameterName => FakeParameterExtensions.NormalizeParameterName(Name);

        public override string ColumnName => Name.TrimStart('@');

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendSetOutParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.OutParameterBuilder(this.ParameterName, expSprocParameters, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeOutputParameter), null, null, parameterNames, expIgnoreParameters, logger);

        protected static bool IsNullableOf(Type candidate, Func<Type, bool> test)
            => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Nullable<>) && test(Nullable.GetUnderlyingType(candidate)!);
    }

    /// <summary>A string (or enum-as-string) mapping: <see cref="ExpressionHelpers.InParameterStringExpressionBuilder"/>, <see cref="ExpressionHelpers.ReadOutParameterStringExpressions"/> and <see cref="ExpressionHelpers.ReaderStringExpressions"/>.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapStringAttribute : FakeTypedMapAttribute
    {
        public FakeMapStringAttribute(string name, int length) : this(name, length, false)
        {
        }

        public FakeMapStringAttribute(string name, int length, bool isRequired) : base(name, (int)DbType.String, isRequired)
        {
            Length = length;
        }

        public int Length { get; }

        public override string SqlTypeName => "fake-string";

        public override bool IsValidType(Type candidate)
            => candidate == typeof(string) || candidate.IsEnum || IsNullableOf(candidate, t => t.IsEnum);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterStringExpressionBuilder(this.ParameterName, Length, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeStringInputParameter), null, expressions, expSprocParameters, expIgnoreParameters, parameterNames, expProperty, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterStringExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetString), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderStringExpressions(this.ColumnName, expProperty, columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>An int (or int-backed enum) mapping: <see cref="ExpressionHelpers.InParameterEnumXIntExpressionBuilder"/>, <see cref="ExpressionHelpers.ReadOutParameterEnumXIntExpressions"/> and <see cref="ExpressionHelpers.ReaderEnumXIntExpressions"/> with an int base type.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapIntegerAttribute : FakeTypedMapAttribute
    {
        public FakeMapIntegerAttribute(string name) : this(name, false)
        {
        }

        public FakeMapIntegerAttribute(string name, bool isRequired) : base(name, (int)DbType.Int32, isRequired)
        {
        }

        public override string SqlTypeName => "fake-int";

        public override bool IsValidType(Type candidate)
            => candidate == typeof(int) || candidate.IsEnum || IsNullableOf(candidate, t => t == typeof(int) || t.IsEnum);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterEnumXIntExpressionBuilder(this.ParameterName, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), typeof(int?), expressions, expSprocParameters, expIgnoreParameters, parameterNames, expProperty, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterEnumXIntExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetInteger), nameof(DbParameterExtensions.GetNullableInteger), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderEnumXIntExpressions(this.ColumnName, expProperty, typeof(int), columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>A byte (or byte-backed enum) mapping through the enum-backed-integral builders with a byte base type.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapByteAttribute : FakeTypedMapAttribute
    {
        public FakeMapByteAttribute(string name) : base(name, (int)DbType.Byte, false)
        {
        }

        public override string SqlTypeName => "fake-byte";

        public override bool IsValidType(Type candidate)
            => candidate == typeof(byte) || candidate.IsEnum || IsNullableOf(candidate, t => t == typeof(byte) || t.IsEnum);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterEnumXIntExpressionBuilder(this.ParameterName, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), typeof(byte?), expressions, expSprocParameters, expIgnoreParameters, parameterNames, expProperty, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterEnumXIntExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetByte), nameof(DbParameterExtensions.GetNullableByte), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderEnumXIntExpressions(this.ColumnName, expProperty, typeof(byte), columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>A double mapping whose null sentinel is <see cref="double.NaN"/> (the <see cref="ExpressionHelpers.ReaderNullableValueTypeExpressions"/> path).</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapDoubleAttribute : FakeTypedMapAttribute
    {
        public FakeMapDoubleAttribute(string name) : this(name, false)
        {
        }

        public FakeMapDoubleAttribute(string name, bool isRequired) : base(name, (int)DbType.Double, isRequired)
        {
        }

        public override string SqlTypeName => "fake-double";

        public override bool IsValidType(Type candidate)
            => candidate == typeof(double) || IsNullableOf(candidate, t => t == typeof(double));

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterSimpleBuilder(this.ParameterName, propertyType, expSprocParameters, expIgnoreParameters, expProperty, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), null, null, parameterNames, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterSimpleValueExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetDouble), nameof(DbParameterExtensions.GetNullableDouble), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderNullableValueTypeExpressions(this.ColumnName, expProperty, Expression.Constant(double.NaN, typeof(double)), columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>A float mapping whose null sentinel is <see cref="float.NaN"/>.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapFloatAttribute : FakeTypedMapAttribute
    {
        public FakeMapFloatAttribute(string name) : base(name, (int)DbType.Single, false)
        {
        }

        public override string SqlTypeName => "fake-float";

        public override bool IsValidType(Type candidate)
            => candidate == typeof(float) || IsNullableOf(candidate, t => t == typeof(float));

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterSimpleBuilder(this.ParameterName, propertyType, expSprocParameters, expIgnoreParameters, expProperty, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), null, null, parameterNames, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterSimpleValueExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetFloat), nameof(DbParameterExtensions.GetNullableFloat), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderNullableValueTypeExpressions(this.ColumnName, expProperty, Expression.Constant(float.NaN, typeof(float)), columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>A Guid mapping whose null sentinel is <see cref="Guid.Empty"/>.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapGuidAttribute : FakeTypedMapAttribute
    {
        public FakeMapGuidAttribute(string name) : this(name, false)
        {
        }

        public FakeMapGuidAttribute(string name, bool isRequired) : base(name, (int)DbType.Guid, isRequired)
        {
        }

        public override string SqlTypeName => "fake-guid";

        public override bool IsValidType(Type candidate)
            => candidate == typeof(Guid) || IsNullableOf(candidate, t => t == typeof(Guid));

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterSimpleBuilder(this.ParameterName, propertyType, expSprocParameters, expIgnoreParameters, expProperty, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), null, null, parameterNames, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterSimpleValueExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetGuid), nameof(DbParameterExtensions.GetNullableGuid), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderNullableValueTypeExpressions(this.ColumnName, expProperty, Expression.Constant(Guid.Empty, typeof(Guid)), columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>A byte[] mapping: <see cref="ExpressionHelpers.InParameterBinaryBuilder"/> and <see cref="ExpressionHelpers.ReadOutParameterBinaryExpressions"/>.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapBinaryAttribute : FakeTypedMapAttribute
    {
        public FakeMapBinaryAttribute(string name) : base(name, (int)DbType.Binary, false)
        {
        }

        public override string SqlTypeName => "fake-binary";

        public override bool IsValidType(Type candidate) => candidate == typeof(byte[]);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterBinaryBuilder(this.ParameterName, propertyType, expSprocParameters, expIgnoreParameters, expProperty, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), null, null, parameterNames, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterBinaryExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetBytes), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderSimpleValueExpressions(this.ColumnName, expProperty, columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>An array (int[] or string[]) mapping: <see cref="ExpressionHelpers.ReadOutParameterArrayExpressions"/> on output and the array branch of <see cref="ExpressionHelpers.ReaderSimpleValueExpressions"/> on read.</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapArrayAttribute : FakeTypedMapAttribute
    {
        public FakeMapArrayAttribute(string name) : base(name, (int)DbType.Object, false)
        {
        }

        public override string SqlTypeName => "fake-array";

        public override bool IsValidType(Type candidate) => candidate == typeof(int[]) || candidate == typeof(string[]);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterSimpleBuilder(this.ParameterName, propertyType, expSprocParameters, expIgnoreParameters, expProperty, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), null, null, parameterNames, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReadOutParameterArrayExpressions(this.ParameterName, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.GetFakeArray), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderSimpleValueExpressions(this.ColumnName, expProperty, columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);
    }

    /// <summary>A provider-neutral shard-key mapping attribute; the shard id is carried as a <see cref="FakeMapAttribute"/> on a short.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class FakeMapShardKeyAttribute : MapShardKeyAttribute
    {
        public FakeMapShardKeyAttribute(string recordIdName) : this(null, recordIdName, null, null, null, true)
        {
        }

        public FakeMapShardKeyAttribute(string? shardIdName, string recordIdName) : this(shardIdName, recordIdName, null, null, null, true)
        {
        }

        public FakeMapShardKeyAttribute(string? shardIdName, string recordIdName, string? childIdName) : this(shardIdName, recordIdName, childIdName, null, null, true)
        {
        }

        public FakeMapShardKeyAttribute(string? shardIdName, string recordIdName, string? childIdName, string? grandChildIdName) : this(shardIdName, recordIdName, childIdName, grandChildIdName, null, true)
        {
        }

        public FakeMapShardKeyAttribute(string? shardIdName, string recordIdName, string? childIdName, string? grandChildIdName, string? greatGrandChildIdName, bool isRecordIdentifier)
            : base(shardIdName is null ? null : new FakeMapAttribute(shardIdName), recordIdName, childIdName, grandChildIdName, greatGrandChildIdName, isRecordIdentifier)
        {
        }
    }
}
