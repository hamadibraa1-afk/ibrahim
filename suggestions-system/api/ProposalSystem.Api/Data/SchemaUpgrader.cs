using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace ProposalSystem.Api.Data;

/// <summary>
/// EnsureCreated builds a schema only for an empty database; on an existing one it adds nothing.
/// This step creates any table the model has but the database lacks (for example Departments or
/// JobLeases on a database from an earlier version), using the provider's own DDL from the
/// model. It never alters or drops existing tables.
/// </summary>
public static partial class SchemaUpgrader
{
    public static async Task<IReadOnlyList<string>> CreateMissingTablesAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct().ToList();
        var missing = new List<string>();
        foreach (var table in tables)
        {
            if (!await TableExistsAsync(db, table, ct))
                missing.Add(table);
        }
        if (missing.Count == 0)
            return missing;

        // Statements are separated by "GO" (SQL Server) or ";" at line end (SQLite).
        var statements = GoSeparator().Split(db.Database.GenerateCreateScript())
            .SelectMany(chunk => db.Database.IsSqlServer() ? [chunk] : chunk.Split(";\n"))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        foreach (var table in missing)
        {
            var quoted = new[] { $"\"{table}\"", $"[{table}]" };
            var create = statements.Where(s => s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase) && quoted.Any(q => s.Contains($"TABLE {q}", StringComparison.Ordinal)));
            var indexes = statements.Where(s => s.Contains("INDEX", StringComparison.OrdinalIgnoreCase) && quoted.Any(q => s.Contains($" ON {q}", StringComparison.Ordinal)));
            foreach (var sql in create.Concat(indexes))
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            logger.LogWarning("Database upgrade: created missing table {Table}.", table);
        }
        return missing;
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table, CancellationToken ct)
    {
        var quoted = db.Database.IsSqlServer() ? $"[{table}]" : $"\"{table}\"";
        try
        {
            // The table name comes from our own EF model, never from user input.
            var probe = "SELECT COUNT(*) FROM " + quoted + " WHERE 1 = 0";
            await db.Database.ExecuteSqlRawAsync(probe, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
