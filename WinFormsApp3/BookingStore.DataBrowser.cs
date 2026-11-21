using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>One column of a table as the database browser shows it.</summary>
public sealed record DataColumnInfo(string Name, string Type, bool NotNull, bool HasDefault, bool IsKey, bool Masked)
{
    /// <summary>Numeric affinity decides how typed text is converted before it is written.</summary>
    public bool IsInteger => Type.Contains("INT", StringComparison.OrdinalIgnoreCase);

    public bool IsReal => !IsInteger && ("REAL FLOA DOUB DEC NUM".Split(' ')
        .Any(t => Type.Contains(t, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>A table's columns. <see cref="ReadOnly"/> tables can be browsed but not changed here.</summary>
public sealed record DataTableInfo(string Name, IReadOnlyList<DataColumnInfo> Columns, bool ReadOnly, bool UsesRowId);

public sealed partial class BookingStore
{
    /// <summary>What the grid shows instead of a secret.</summary>
    public const string MaskedValue = "••••••";

    // Tables other code owns: accounts need hashing, the audit trail must stay append-only, migrations are bookkeeping.
    private static readonly HashSet<string> BrowserReadOnlyTables = new(StringComparer.OrdinalIgnoreCase)
        { "staff_users", "audit_log", "schema_migrations" };

    private static bool IsMaskedColumn(string name)
        => name.Contains("password", StringComparison.OrdinalIgnoreCase)
           || name.Contains("hash", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public List<string> ListTables()
    {
        lock (_lock)
        {
            var list = new List<string>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name");
                using var rs = cmd.ExecuteReader();
                while (rs.Read()) list.Add(Str(rs, "name"));
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to list the tables: " + e.Message, e);
            }
            return list;
        }
    }

    /// <summary>Describes a table. The name must be an existing table, so it is safe to quote into SQL afterwards.</summary>
    public DataTableInfo DescribeTable(string table)
    {
        lock (_lock)
        {
            string? real = ListTables().FirstOrDefault(t => string.Equals(t, table, StringComparison.OrdinalIgnoreCase));
            if (real is null) throw new ArgumentException($"There is no table called '{table}'.");
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT name, type, \"notnull\" AS not_null, dflt_value, pk FROM pragma_table_info(@t) ORDER BY cid", null, ("@t", real));
                using var rs = cmd.ExecuteReader();
                var cols = new List<DataColumnInfo>();
                while (rs.Read())
                {
                    string name = Str(rs, "name");
                    cols.Add(new DataColumnInfo(name, Str(rs, "type"), Int(rs, "not_null") != 0,
                        !rs.IsDBNull(rs.GetOrdinal("dflt_value")), Int(rs, "pk") > 0, IsMaskedColumn(name)));
                }
                bool usesRowId = !cols.Any(c => c.IsKey);
                return new DataTableInfo(real, cols, BrowserReadOnlyTables.Contains(real), usesRowId);
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to read the table layout: " + e.Message, e);
            }
        }
    }

    /// <summary>
    /// Rows of a table as text (secrets masked). <paramref name="search"/> matches any column. Tables with no primary key get
    /// their SQLite rowid as an extra first column so a row can still be picked.
    /// </summary>
    public DataTable QueryTable(string table, string? search = null, int limit = 1000)
    {
        lock (_lock)
        {
            DataTableInfo info = DescribeTable(table);
            var result = new DataTable(info.Name);
            var shown = new List<string>();
            if (info.UsesRowId) { shown.Add("rowid"); result.Columns.Add("rowid", typeof(string)); }
            foreach (var c in info.Columns) { shown.Add(c.Name); result.Columns.Add(c.Name, typeof(string)); }

            var args = new List<(string, object?)> { ("@limit", Math.Clamp(limit, 1, 5000)) };
            string where = "";
            if (!string.IsNullOrWhiteSpace(search))
            {
                // Masked columns are left out so a secret cannot be guessed one search at a time.
                var parts = info.Columns.Where(c => !c.Masked).Select(c => $"CAST({Quote(c.Name)} AS TEXT) LIKE @s ESCAPE '\\'").ToList();
                if (parts.Count > 0)
                {
                    where = " WHERE " + string.Join(" OR ", parts);
                    args.Add(("@s", "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
                }
            }
            string select = string.Join(", ", shown.Select(Quote));
            string order = info.UsesRowId ? "rowid" : string.Join(", ", info.Columns.Where(c => c.IsKey).Select(c => Quote(c.Name)));
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, $"SELECT {select} FROM {Quote(info.Name)}{where} ORDER BY {order} LIMIT @limit", null, args.ToArray());
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                {
                    var row = result.NewRow();
                    for (int i = 0; i < shown.Count; i++)
                    {
                        bool masked = !(info.UsesRowId && i == 0) && info.Columns[info.UsesRowId ? i - 1 : i].Masked;
                        row[i] = masked ? MaskedValue : rs.IsDBNull(i) ? "" : Convert.ToString(rs.GetValue(i), CultureInfo.InvariantCulture) ?? "";
                    }
                    result.Rows.Add(row);
                }
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to read the table: " + e.Message, e);
            }
            return result;
        }
    }

    /// <summary>Adds a row. Blank fields are left out so the column's default (or NULL) applies.</summary>
    public string InsertRow(string table, IReadOnlyDictionary<string, string?> values)
    {
        lock (_lock)
        {
            DataTableInfo info = RequireWritable(table);
            var names = new List<string>();
            var added = new List<string>();
            var args = new List<(string, object?)>();
            foreach (var c in info.Columns.Where(c => !c.Masked))
            {
                values.TryGetValue(c.Name, out string? text);
                if (string.IsNullOrEmpty(text))
                {
                    if (!c.NotNull || c.HasDefault) continue;
                    if (c.IsInteger || c.IsReal) throw new ArgumentException($"'{c.Name}' is required.");
                    text = "";
                }
                names.Add(Quote(c.Name));
                added.Add($"{c.Name}={AuditValue(c, text)}");
                args.Add(("@v" + args.Count, ToDbValue(c, text)));
            }
            string sql = names.Count == 0
                ? $"INSERT INTO {Quote(info.Name)} DEFAULT VALUES"
                : $"INSERT INTO {Quote(info.Name)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", args.Select(a => a.Item1))})";
            Write(sql, args);
            return string.Join("; ", added);
        }
    }

    /// <summary>Changes one row, found by its key (primary-key columns, or rowid). Key and secret columns are not changed.</summary>
    public string UpdateRow(string table, IReadOnlyDictionary<string, string?> key, IReadOnlyDictionary<string, string?> values)
    {
        lock (_lock)
        {
            DataTableInfo info = RequireWritable(table);
            Dictionary<string, string?>? before = ReadRowValues(info, key);
            var changes = new List<string>();
            var sets = new List<string>();
            var args = new List<(string, object?)>();
            foreach (var c in info.Columns.Where(c => !c.Masked && !c.IsKey))
            {
                if (!values.TryGetValue(c.Name, out string? text)) continue;
                if (string.IsNullOrEmpty(text))
                {
                    if (c.NotNull && (c.IsInteger || c.IsReal)) throw new ArgumentException($"'{c.Name}' is required.");
                    text = c.NotNull ? "" : null;
                }
                sets.Add($"{Quote(c.Name)} = @v{args.Count}");
                string? old = null;
                bool hadOld = before is not null && before.TryGetValue(c.Name, out old);
                if (!hadOld || !string.Equals(old ?? "", text ?? "", StringComparison.Ordinal))
                    changes.Add($"{c.Name}: {AuditValue(c, old)} → {AuditValue(c, text)}");
                args.Add(("@v" + args.Count, ToDbValue(c, text)));
            }
            if (sets.Count == 0) throw new ArgumentException("There is nothing to change.");
            string where = KeyClause(info, key, args);
            if (Write($"UPDATE {Quote(info.Name)} SET {string.Join(", ", sets)} WHERE {where}", args) == 0)
                throw new InvalidOperationException("That row no longer exists. Refresh and try again.");
            return changes.Count == 0 ? "no values changed" : string.Join("; ", changes);
        }
    }

    /// <summary>Deletes one row and returns what it held (secret and customer-detail columns hidden), for the audit trail.</summary>
    public string DeleteRow(string table, IReadOnlyDictionary<string, string?> key)
    {
        lock (_lock)
        {
            DataTableInfo info = RequireWritable(table);
            Dictionary<string, string?>? before = ReadRowValues(info, key);
            var args = new List<(string, object?)>();
            string where = KeyClause(info, key, args);
            if (Write($"DELETE FROM {Quote(info.Name)} WHERE {where}", args) == 0)
                throw new InvalidOperationException("That row no longer exists. Refresh and try again.");
            return before is null ? "" : Describe(info, before);
        }
    }

    // Customer details that are logged as "(hidden)": the audit trail shows that the column changed, not what it held.
    private static bool IsSensitiveColumn(string name)
        => new[] { "id_number", "passport", "phone", "email", "address", "birth", "card" }
            .Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));

    internal static string AuditValue(DataColumnInfo c, string? value)
    {
        if (c.Masked || IsSensitiveColumn(c.Name)) return "(hidden)";
        if (value is null) return "NULL";
        string v = value.Replace('\r', ' ').Replace('\n', ' ');
        return "'" + (v.Length > 30 ? v[..30] + "…" : v) + "'";
    }

    /// <summary>The current values of one row (non-secret columns as text), or null when it does not exist. Caller holds the lock.</summary>
    private Dictionary<string, string?>? ReadRowValues(DataTableInfo info, IReadOnlyDictionary<string, string?> key)
    {
        var args = new List<(string, object?)>();
        string where = KeyClause(info, key, args);
        var columns = info.Columns.Where(c => !c.Masked).ToList();
        if (columns.Count == 0) return new Dictionary<string, string?>();
        try
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con, $"SELECT {string.Join(", ", columns.Select(c => Quote(c.Name)))} FROM {Quote(info.Name)} WHERE {where}", null, args.ToArray());
            using var rs = cmd.ExecuteReader();
            if (!rs.Read()) return null;
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
                row[columns[i].Name] = rs.IsDBNull(i) ? null : Convert.ToString(rs.GetValue(i), CultureInfo.InvariantCulture);
            return row;
        }
        catch (SqliteException e)
        {
            LogError(e);
            throw new IOException("Unable to read the row: " + e.Message, e);
        }
    }

    private static string Describe(DataTableInfo info, Dictionary<string, string?> row)
        => string.Join("; ", info.Columns.Where(c => !c.Masked && row.ContainsKey(c.Name)).Select(c => $"{c.Name}={AuditValue(c, row[c.Name])}"));

    private DataTableInfo RequireWritable(string table)
    {
        DataTableInfo info = DescribeTable(table);
        if (info.ReadOnly)
            throw new InvalidOperationException($"'{info.Name}' can be viewed here but not changed; use its own screen instead.");
        return info;
    }

    private static string KeyClause(DataTableInfo info, IReadOnlyDictionary<string, string?> key, List<(string, object?)> args)
    {
        var parts = new List<string>();
        var keyColumns = info.UsesRowId
            ? new[] { new DataColumnInfo("rowid", "INTEGER", true, false, true, false) }
            : info.Columns.Where(c => c.IsKey).ToArray();
        foreach (var c in keyColumns)
        {
            if (!key.TryGetValue(c.Name, out string? text) || text is null)
                throw new ArgumentException($"Pick a row first ('{c.Name}' is missing).");
            string p = "@k" + args.Count;
            parts.Add($"{Quote(c.Name)} = {p}");
            args.Add((p, ToDbValue(c, text)));
        }
        return string.Join(" AND ", parts);
    }

    private int Write(string sql, List<(string, object?)> args)
    {
        try
        {
            using var con = OpenConnection();
            Exec(con, "PRAGMA foreign_keys = ON");
            using var tx = con.BeginTransaction();
            using var cmd = Cmd(con, sql, tx, args.ToArray());
            int rows = cmd.ExecuteNonQuery();
            tx.Commit();
            return rows;
        }
        catch (SqliteException e)
        {
            LogError(e);
            throw new InvalidOperationException("The database rejected the change: " + e.Message, e);
        }
    }

    private static object? ToDbValue(DataColumnInfo c, string? text)
    {
        if (text is null) return null;
        string t = text.Trim();
        if (c.IsInteger)
            return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)
                ? n : throw new ArgumentException($"'{c.Name}' must be a whole number.");
        if (c.IsReal)
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d : throw new ArgumentException($"'{c.Name}' must be a number.");
        return text;
    }
}
