using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    private static void EnsureSecuritySchema(SqliteConnection con)
    {
        string[] ddl =
        {
            "CREATE TABLE IF NOT EXISTS staff_users (" +
            "  id INTEGER PRIMARY KEY AUTOINCREMENT," +
            "  username VARCHAR(40) NOT NULL UNIQUE COLLATE NOCASE," +
            "  display_name VARCHAR(80) NOT NULL," +
            "  password_hash VARCHAR(200) NOT NULL," +
            "  role VARCHAR(10) NOT NULL CHECK (role IN ('ADMIN','AGENT'))," +
            "  active INTEGER NOT NULL DEFAULT 1," +
            "  must_change_password INTEGER NOT NULL DEFAULT 0," +
            "  failed_attempts INTEGER NOT NULL DEFAULT 0," +
            "  locked_until DATETIME NULL," +
            "  created_at DATETIME NOT NULL," +
            "  last_login_at DATETIME NULL" +
            ")",
            "CREATE TABLE IF NOT EXISTS audit_log (" +
            "  id INTEGER PRIMARY KEY AUTOINCREMENT," +
            "  at DATETIME NOT NULL," +
            "  actor VARCHAR(80) NOT NULL," +
            "  role VARCHAR(10) NOT NULL DEFAULT ''," +
            "  action VARCHAR(40) NOT NULL," +
            "  target VARCHAR(120) NOT NULL DEFAULT ''," +
            "  detail VARCHAR(400) NOT NULL DEFAULT ''" +
            ")",
            "CREATE INDEX IF NOT EXISTS idx_audit_at ON audit_log(at)"
        };
        foreach (string sql in ddl) Exec(con, sql);
    }

    /// <summary>Removes control characters and cuts a value to fit the audit columns.</summary>
    internal static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var chars = value.Where(c => !char.IsControl(c)).ToArray();
        string text = new string(chars).Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static void WriteAuditCore(SqliteConnection con, string actor, string role, string action, string target, string detail)
        => Exec(con,
            "INSERT INTO audit_log (at, actor, role, action, target, detail) VALUES (@at, @actor, @role, @action, @target, @detail)",
            ("@at", DateTime.UtcNow), ("@actor", Clean(actor, 80)), ("@role", Clean(role, 10)),
            ("@action", Clean(action, 40)), ("@target", Clean(target, 120)), ("@detail", Clean(detail, 400)));

    /// <summary>
    /// Records who did what. Never put passwords, ID numbers or card numbers in <paramref name="detail"/>.
    /// </summary>
    public void WriteAudit(string actor, string role, string action, string target = "", string detail = "")
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                WriteAuditCore(con, actor, role, action, target, detail);
            }
            catch (SqliteException e)
            {
                // A failed audit write must never hide the original action, but it must be visible in the log.
                AppLogger.Error("Audit", e);
            }
        }
    }

    /// <summary>Newest entries first. <paramref name="search"/> matches actor, action, target or detail.</summary>
    public List<AuditEntry> LoadAudit(int limit = 500, string? search = null)
    {
        lock (_lock)
        {
            var list = new List<AuditEntry>();
            try
            {
                using var con = OpenConnection();
                string where = "";
                var args = new List<(string, object?)>();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    where = "WHERE actor LIKE @s OR action LIKE @s OR target LIKE @s OR detail LIKE @s ";
                    args.Add(("@s", "%" + search.Trim().Replace("%", "").Replace("_", "") + "%"));
                }
                args.Add(("@limit", Math.Clamp(limit, 1, 5000)));
                using var cmd = Cmd(con,
                    "SELECT id, at, actor, role, action, target, detail FROM audit_log " + where + "ORDER BY id DESC LIMIT @limit",
                    null, args.ToArray());
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    list.Add(new AuditEntry(Long(rs, "id"), DateTime.SpecifyKind(Dt(rs, "at"), DateTimeKind.Utc),
                        Str(rs, "actor"), Str(rs, "role"), Str(rs, "action"), Str(rs, "target"), Str(rs, "detail")));
            }
            catch (SqliteException e)
            {
                throw new IOException("Unable to load the audit log: " + e.Message, e);
            }
            return list;
        }
    }
}
