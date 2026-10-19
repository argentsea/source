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
    /// A provider-neutral mapping attribute, so the core mapper (and every MapReaderAsync / MapOutputAsync / MapListAsync
    /// overload built on it) can be exercised against the fake provider without a SQL Server or PostgreSQL package.
    /// Supports int, short, long, bool, decimal, double, Guid, DateTime (and their nullable forms), string and byte[].
    /// Parameter names are normalized to a leading '@'; column names have no prefix, as the real providers do.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class FakeMapAttribute : ParameterMapAttributeBase
    {
        private static readonly HashSet<Type> SupportedTypes = new HashSet<Type>
        {
            typeof(int), typeof(short), typeof(long), typeof(bool), typeof(decimal), typeof(double), typeof(Guid), typeof(DateTime), typeof(string), typeof(byte[])
        };

        public FakeMapAttribute(string name) : base(name, 0)
        {
        }

        public FakeMapAttribute(string name, bool isRequired) : base(name, 0, isRequired)
        {
        }

        public override string ParameterName => FakeParameterExtensions.NormalizeParameterName(Name);

        public override string ColumnName => Name.TrimStart('@');

        public override string SqlTypeName => "fake";

        public override bool IsValidType(Type candidate)
            => SupportedTypes.Contains(Nullable.GetUnderlyingType(candidate) ?? candidate);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendInParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, Expression expProperty, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.InParameterSimpleBuilder(this.ParameterName, propertyType, expSprocParameters, expIgnoreParameters, expProperty, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeInputParameter), null, null, parameterNames, expLogger, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendSetOutParameterExpressions(IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expIgnoreParameters, HashSet<string> parameterNames, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.OutParameterBuilder(this.ParameterName, expSprocParameters, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeOutputParameter), null, null, parameterNames, expIgnoreParameters, logger);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReadOutParameterExpressions(Expression expProperty, IList<Expression> expressions, ParameterExpression expSprocParameters, ParameterExpression expPrm, Type propertyType, ParameterExpression expLogger, ILogger? logger)
        {
            var baseType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (baseType == typeof(string))
            {
                ExpressionHelpers.ReadOutParameterStringExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetString), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);
                return;
            }
            if (baseType == typeof(byte[]))
            {
                ExpressionHelpers.ReadOutParameterBinaryExpressions(this.ParameterName, typeof(DbParameterExtensions), nameof(DbParameterExtensions.GetBytes), expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);
                return;
            }
            var (getter, nullableGetter) = OutputGetters(baseType);
            ExpressionHelpers.ReadOutParameterSimpleValueExpressions(this.ParameterName, typeof(DbParameterExtensions), getter, nullableGetter, expProperty, expressions, expSprocParameters, expPrm, propertyType, expLogger, logger);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void AppendReaderExpressions(Expression expProperty, IList<MethodCallExpression> columnLookupExpressions, IList<Expression> expressions, ParameterExpression prmSqlRdr, ParameterExpression expOrdinals, ParameterExpression expOrdinal, ref int propIndex, Type propertyType, ParameterExpression expLogger, ILogger? logger)
            => ExpressionHelpers.ReaderSimpleValueExpressions(this.ColumnName, expProperty, columnLookupExpressions, expressions, prmSqlRdr, expOrdinals, expOrdinal, ref propIndex, propertyType, expLogger, logger);

        private static (string getter, string nullableGetter) OutputGetters(Type baseType)
        {
            if (baseType == typeof(int)) return (nameof(DbParameterExtensions.GetInteger), nameof(DbParameterExtensions.GetNullableInteger));
            if (baseType == typeof(short)) return (nameof(DbParameterExtensions.GetShort), nameof(DbParameterExtensions.GetNullableShort));
            if (baseType == typeof(long)) return (nameof(DbParameterExtensions.GetLong), nameof(DbParameterExtensions.GetNullableLong));
            if (baseType == typeof(bool)) return (nameof(DbParameterExtensions.GetBoolean), nameof(DbParameterExtensions.GetNullableBoolean));
            if (baseType == typeof(decimal)) return (nameof(DbParameterExtensions.GetDecimal), nameof(DbParameterExtensions.GetNullableDecimal));
            if (baseType == typeof(double)) return (nameof(DbParameterExtensions.GetDouble), nameof(DbParameterExtensions.GetNullableDouble));
            if (baseType == typeof(Guid)) return (nameof(DbParameterExtensions.GetGuid), nameof(DbParameterExtensions.GetNullableGuid));
            if (baseType == typeof(DateTime)) return (nameof(DbParameterExtensions.GetDateTime), nameof(DbParameterExtensions.GetNullableDateTime));
            throw new NotSupportedException($"FakeMapAttribute does not support output parameters of type {baseType.Name}.");
        }
    }

    /// <summary>
    /// The parameter factory <see cref="FakeMapAttribute"/> compiles calls to. One input overload per supported property type
    /// (the mapper resolves the overload by the property's exact type) and one output overload.
    /// </summary>
    public static class FakeParameterExtensions
    {
        public static string NormalizeParameterName(string name)
            => string.IsNullOrEmpty(name) ? name : (name.StartsWith("@", StringComparison.Ordinal) ? name : "@" + name);

        public static DbParameterCollection AddFakeOutputParameter(this DbParameterCollection prms, string parameterName)
        {
            prms.Add(new FakeDbParameter(NormalizeParameterName(parameterName), null, ParameterDirection.Output));
            return prms;
        }

        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, int value) => AddInput(prms, parameterName, value, DbType.Int32);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, int? value) => AddInput(prms, parameterName, value, DbType.Int32);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, short value) => AddInput(prms, parameterName, value, DbType.Int16);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, short? value) => AddInput(prms, parameterName, value, DbType.Int16);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, long value) => AddInput(prms, parameterName, value, DbType.Int64);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, long? value) => AddInput(prms, parameterName, value, DbType.Int64);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, bool value) => AddInput(prms, parameterName, value, DbType.Boolean);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, bool? value) => AddInput(prms, parameterName, value, DbType.Boolean);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, decimal value) => AddInput(prms, parameterName, value, DbType.Decimal);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, decimal? value) => AddInput(prms, parameterName, value, DbType.Decimal);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, double value) => AddInput(prms, parameterName, value, DbType.Double);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, double? value) => AddInput(prms, parameterName, value, DbType.Double);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, Guid value) => AddInput(prms, parameterName, value, DbType.Guid);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, Guid? value) => AddInput(prms, parameterName, value, DbType.Guid);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, DateTime value) => AddInput(prms, parameterName, value, DbType.DateTime);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, DateTime? value) => AddInput(prms, parameterName, value, DbType.DateTime);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, string value) => AddInput(prms, parameterName, value, DbType.String);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, byte[] value) => AddInput(prms, parameterName, value, DbType.Binary);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, byte value) => AddInput(prms, parameterName, value, DbType.Byte);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, byte? value) => AddInput(prms, parameterName, value, DbType.Byte);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, float value) => AddInput(prms, parameterName, value, DbType.Single);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, float? value) => AddInput(prms, parameterName, value, DbType.Single);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, int[] value) => AddInput(prms, parameterName, value, DbType.Object);
        public static DbParameterCollection AddFakeInputParameter(this DbParameterCollection prms, string parameterName, string[] value) => AddInput(prms, parameterName, value, DbType.Object);

        /// <summary>Adds a string input parameter; the length is recorded as the parameter's <see cref="DbParameter.Size"/>.</summary>
        public static DbParameterCollection AddFakeStringInputParameter(this DbParameterCollection prms, string parameterName, string? value, int length)
        {
            prms.Add(new FakeDbParameter(NormalizeParameterName(parameterName), value ?? (object)DBNull.Value, ParameterDirection.Input) { DbType = DbType.String, Size = length });
            return prms;
        }

        /// <summary>Adds a parameter carrying a whole collection, standing in for a provider's structured (table-valued) parameter.</summary>
        public static DbParameterCollection AddFakeCollectionInputParameter(DbParameterCollection prms, string parameterName, object? collection)
        {
            prms.Add(new FakeDbParameter(parameterName, collection ?? DBNull.Value, ParameterDirection.Input) { DbType = DbType.Object });
            return prms;
        }

        /// <summary>Reads an array output parameter; the generic argument is the array's element type.</summary>
        public static T[]? GetFakeArray<T>(this DbParameter prm) => prm.Value as T[];

        private static DbParameterCollection AddInput(DbParameterCollection prms, string parameterName, object? value, DbType dbType)
        {
            prms.Add(new FakeDbParameter(NormalizeParameterName(parameterName), value ?? DBNull.Value, ParameterDirection.Input) { DbType = dbType });
            return prms;
        }
    }
}

namespace ArgentSea.TestKit
{
    /// <summary>
    /// Marks a collection property as populated from a subsequent result set by MapReaderWithCollectionsAsync. On the input side
    /// (where a real provider would build a table-valued parameter) the fake provider adds one parameter whose value is the
    /// collection itself, so a test can see that the property was mapped.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class FakeCollectionMapAttribute : CollectionMapAttributeBase
    {
        public FakeCollectionMapAttribute(string parameterName) : base(parameterName)
        {
        }

        public override void AppendCollectionInParameterExpressions(List<Expression> expressions, ParameterExpression prmSqlPrms, Expression expCollection, Type elementType, ParameterExpression expLogger, ILogger? logger)
        {
            var method = typeof(FakeParameterExtensions).GetMethod(nameof(FakeParameterExtensions.AddFakeCollectionInputParameter))!;
            expressions.Add(Expression.Call(method, prmSqlPrms, Expression.Constant(FakeParameterExtensions.NormalizeParameterName(ParameterName)), Expression.Convert(expCollection, typeof(object))));
        }
    }
}
