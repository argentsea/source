using System;
using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Data.SqlClient;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Asserts on the keys of a rendered connection string rather than on the whole string, so that a driver upgrade that
    /// changes how SqlClient renders (or omits) a setting does not break the configuration tests.
    /// </summary>
    internal static class ConnectionStringAssertions
    {
        /// <summary>
        /// Parses <paramref name="connectionString"/> with <see cref="SqlConnectionStringBuilder"/> and asserts that each
        /// expected keyword has the expected value (compared as invariant strings; booleans as "True"/"False", enums by name).
        /// </summary>
        public static void ShouldHaveSqlSettings(this string connectionString, string because, params (string Key, string Value)[] expected)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            using (new AssertionScope($"connection string \"{connectionString}\""))
            {
                foreach (var (key, value) in expected)
                {
                    Convert.ToString(builder[key], CultureInfo.InvariantCulture).Should().Be(value, $"{key} should be {value} because {because}");
                }
            }
        }
    }
}
