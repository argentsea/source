using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ArgentSea.Integration.Test.Fixtures;

/// <summary>
/// Runs a SQL Server setup script the way sqlcmd would: split into batches on GO lines, each batch executed in order on the
/// same connection (so USE statements carry forward), with the script's $Placeholder tokens substituted first.
/// </summary>
public static partial class SqlScriptRunner
{
    [GeneratedRegex(@"^\s*GO\s*(--.*)?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BatchSeparator();

    [GeneratedRegex(@"^\s*CREATE\s+DATABASE\s+\[?(?<name>[A-Za-z0-9_$]+)\]?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CreateDatabase();

    public static async Task RunAsync(SqlConnection connection, string scriptPath, IReadOnlyDictionary<string, string> substitutions)
    {
        var script = await File.ReadAllTextAsync(scriptPath, Encoding.UTF8);
        foreach (var (token, value) in substitutions)
        {
            script = script.Replace(token, value, StringComparison.Ordinal);
        }
        var batches = BatchSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        var fileName = Path.GetFileName(scriptPath);
        var index = 0;
        foreach (var batch in batches)
        {
            index++;
            var create = CreateDatabase().Match(batch);
            if (create.Success && await DatabaseExistsAsync(connection, create.Groups["name"].Value))
            {
                continue; // the database is already there (an override server reused across runs); the rest of the script is idempotent
            }
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                var preview = batch.Trim();
                preview = preview.Length > 160 ? preview.Substring(0, 160) + "…" : preview;
                throw new InvalidOperationException($"{fileName} batch {index} failed (SQL error {ex.Number}: {ex.Message}). Batch starts: {preview}", ex);
            }
        }
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection connection, string name)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END", connection);
        command.Parameters.AddWithValue("@name", name);
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }
}
