using System.Text.RegularExpressions;
using SpeakSafe.Core;

namespace SpeakSafe.Web.Data;

public sealed partial class DbInitializer(Db db, SqlStore store, ICipher cipher, IConfiguration config, ILogger<DbInitializer> log, TimeProvider clock)
{
    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SafeName();

    public async Task RunAsync()
    {
        if (config.GetValue("Database:CreateIfMissing", true)) await EnsureDatabaseAsync();

        await using (var c = await db.OpenAsync())
        {
            foreach (var (name, sql) in Schema.Batches)
            {
                try
                {
                    await using var cmd = Db.Cmd(c, sql);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    // Fail loudly with the batch name: a half-applied schema must never start silently.
                    log.LogCritical(ex, "Schema batch {Batch} failed", name);
                    throw new InvalidOperationException($"Schema batch '{name}' failed: {ex.Message}", ex);
                }
            }
        }
        log.LogInformation("Schema v{Version} verified ({Count} batches)", Schema.Version, Schema.Batches.Length);

        if (config.GetValue("Seed:Enabled", true) && await store.IsEmptyAsync())
        {
            var pass = config["Seed:SamplePassword"] ?? "demo1234";
            await store.SeedAsync(SeedData.Build(clock.GetUtcNow().UtcDateTime, pass, cipher));
            log.LogInformation("Seeded demo organisations, officers and sample reports");
        }
    }

    private async Task EnsureDatabaseAsync()
    {
        var b = db.Factory.CreateConnectionStringBuilder() ?? throw new InvalidOperationException("Provider has no connection string builder.");
        b.ConnectionString = db.ConnectionString;
        var key = b.ContainsKey("Initial Catalog") ? "Initial Catalog" : "Database";
        var name = b.TryGetValue(key, out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(name)) return;
        if (!SafeName().IsMatch(name)) throw new InvalidOperationException($"Database name '{name}' must be letters, digits or underscore.");

        b[key] = "master";
        try
        {
            await using var c = await db.OpenAsync(b.ConnectionString);
            await using var cmd = Db.Cmd(c, $"IF DB_ID(N'{name}') IS NULL CREATE DATABASE [{name}];");
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not check/create database {Db} via master. Create it manually if the next step fails.", name);
        }
    }
}
