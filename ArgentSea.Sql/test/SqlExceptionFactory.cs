using System;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Builds <see cref="SqlException"/> instances with a chosen error number. Microsoft.Data.SqlClient exposes no public
    /// constructor, so this reflects into the internal SqlError / SqlErrorCollection / SqlException.CreateException members
    /// the driver itself uses to surface server errors.
    /// </summary>
    internal static class SqlExceptionFactory
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static SqlException WithNumber(int number, string message = "test error")
        {
            var errorCollection = Activator.CreateInstance(typeof(SqlErrorCollection), true)
                ?? throw new InvalidOperationException("SqlErrorCollection could not be created.");

            // Pick the richest internal constructor and fill it by parameter type; the first int is the error number.
            var errorCtor = typeof(SqlError).GetConstructors(Hidden).OrderByDescending(c => c.GetParameters().Length).First();
            var numberAssigned = false;
            var args = errorCtor.GetParameters().Select(p =>
            {
                if (p.ParameterType == typeof(int) && !numberAssigned)
                {
                    numberAssigned = true;
                    return (object?)number;
                }
                if (p.ParameterType == typeof(int)) return 0;
                if (p.ParameterType == typeof(byte)) return (byte)0;
                if (p.ParameterType == typeof(uint)) return 0u;
                if (p.ParameterType == typeof(string)) return p.Name == "errorMessage" ? message : "test";
                if (p.ParameterType == typeof(bool)) return false;
                return null;
            }).ToArray();
            var error = errorCtor.Invoke(args);

            var add = typeof(SqlErrorCollection).GetMethod("Add", Hidden, null, new[] { typeof(SqlError) }, null)
                ?? throw new InvalidOperationException("SqlErrorCollection.Add was not found.");
            add.Invoke(errorCollection, new[] { error });

            var create = typeof(SqlException).GetMethod("CreateException", Hidden, null, new[] { typeof(SqlErrorCollection), typeof(string) }, null)
                ?? throw new InvalidOperationException("SqlException.CreateException(SqlErrorCollection, string) was not found.");
            return (SqlException)create.Invoke(null, new[] { errorCollection, (object)"16.0.0" })!;
        }
    }
}
