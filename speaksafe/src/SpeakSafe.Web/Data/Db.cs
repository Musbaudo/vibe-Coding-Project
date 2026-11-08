using System.Data;
using System.Data.Common;

namespace SpeakSafe.Web.Data;

/// <summary>Thin ADO.NET helper over the registered SQL Server provider (Microsoft.Data.SqlClient).</summary>
public sealed class Db(DbProviderFactory factory, string connectionString)
{
    public DbProviderFactory Factory => factory;
    public string ConnectionString => connectionString;

    public async Task<DbConnection> OpenAsync(string? cs = null)
    {
        var c = factory.CreateConnection() ?? throw new InvalidOperationException("Provider returned no connection.");
        c.ConnectionString = cs ?? connectionString;
        await c.OpenAsync();
        return c;
    }

    public static DbCommand Cmd(DbConnection c, string sql, DbTransaction? tx = null)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = 30;
        return cmd;
    }

    public static DbCommand P(DbCommand cmd, string name, object? value, DbType? type = null)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        if (type is not null) p.DbType = type.Value;
        else if (value is DateTime) p.DbType = DbType.DateTime2;
        else if (value is string s) { p.DbType = DbType.String; p.Size = s.Length <= 4000 ? 4000 : -1; }
        cmd.Parameters.Add(p);
        return cmd;
    }

    public static string? Str(DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
    public static DateTime Utc(DbDataReader r, string col) => DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal(col)), DateTimeKind.Utc);
    public static DateTime? UtcN(DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc); }
    public static decimal? DecN(DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetDecimal(i); }
    public static long? LongN(DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetInt64(i); }
    public static int? IntN(DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetInt32(i); }
}
