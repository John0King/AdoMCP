using AdoMcp.Models;

namespace AdoMcp.Services;

/// <summary>
/// Conservative token-based query filter, not a SQL parser or database permission boundary.
/// Accepts one SELECT/WITH statement and rejects write/control keywords outside quoted text.
/// </summary>
internal static class SqlQueryGuard
{
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "REPLACE", "UPSERT",
        "CREATE", "ALTER", "DROP", "TRUNCATE", "RENAME", "COMMENT",
        "GRANT", "REVOKE", "DENY", "INTO", "COPY", "LOAD",
        "EXEC", "EXECUTE", "CALL", "DO", "DECLARE", "PREPARE", "DEALLOCATE",
        "SET", "RESET", "USE", "BEGIN", "START", "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE",
        "LOCK", "UNLOCK", "SHARE", "ATTACH", "DETACH", "PRAGMA", "VACUUM", "REINDEX", "ANALYZE", "GO",
    };

    public static void Validate(string sql, DbType dbType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var position = 0;
        var ended = false;
        var depth = 0;
        string? first = null;
        var hasSelect = false;
        while (position < sql.Length)
        {
            var c = sql[position];
            if (char.IsWhiteSpace(c)) { position++; continue; }
            if (c == '-' && Peek(sql, position + 1) == '-' &&
                (dbType != DbType.MySql || position + 2 == sql.Length || sql[position + 2] <= ' '))
            {
                SkipLine(sql, ref position);
                continue;
            }
            if (dbType == DbType.MySql && c == '#')
            {
                SkipLine(sql, ref position);
                continue;
            }
            if (c == '/' && Peek(sql, position + 1) == '*')
            {
                // MySQL/MariaDB executable comments contain actual SQL, not ordinary comments.
                if (dbType == DbType.MySql && (Peek(sql, position + 2) == '!' ||
                    (Peek(sql, position + 2) is 'M' or 'm' && Peek(sql, position + 3) == '!')))
                    Reject("Executable comments are not accepted.");
                SkipComment(sql, ref position, dbType is DbType.PostgreSql or DbType.SqlServer);
                continue;
            }
            if (ended) Reject("Only one SQL statement is accepted.");
            if (c == ';')
            {
                if (depth != 0) Reject("Unexpected statement separator.");
                ended = true;
                position++;
                continue;
            }
            if (first is null && !char.IsLetter(c)) Reject("The statement must start with SELECT or WITH.");

            if (dbType == DbType.Oracle && (c is 'q' or 'Q') && Peek(sql, position + 1) == '\'')
            {
                SkipOracleLiteral(sql, ref position);
                continue;
            }
            if (dbType == DbType.PostgreSql && c == '$' && TrySkipDollarLiteral(sql, ref position))
                continue;
            if (c is '\'' or '"' || (c == '[' && dbType is DbType.SqlServer or DbType.Sqlite) ||
                (c == '`' && dbType is DbType.MySql or DbType.Sqlite))
            {
                SkipQuoted(sql, ref position, c == '[' ? ']' : c);
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = position++;
                while (position < sql.Length && (char.IsLetterOrDigit(sql[position]) || sql[position] is '_' or '$' or '#')) position++;
                var word = sql[start..position];
                if (first is null)
                {
                    first = word;
                    if (!word.Equals("SELECT", StringComparison.OrdinalIgnoreCase) && !word.Equals("WITH", StringComparison.OrdinalIgnoreCase))
                        Reject("Only SELECT and WITH queries are accepted.");
                }
                if (Blocked.Contains(word)) Reject($"Keyword '{word}' is not accepted in query_sql.");
                hasSelect |= word.Equals("SELECT", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (c == '(') depth++;
            if (c == ')' && --depth < 0) Reject("Unbalanced parentheses.");
            position++;
        }
        if (!hasSelect || depth != 0) Reject("A complete SELECT query is required.");
    }

    private static char Peek(string sql, int position) => position < sql.Length ? sql[position] : '\0';

    private static void SkipLine(string sql, ref int position)
    {
        while (position < sql.Length && sql[position] is not '\r' and not '\n') position++;
    }

    private static void SkipComment(string sql, ref int position, bool nested)
    {
        position += 2;
        var depth = 1;
        while (position < sql.Length)
        {
            if (nested && sql[position] == '/' && Peek(sql, position + 1) == '*')
            { depth++; position += 2; }
            else if (sql[position] == '*' && Peek(sql, position + 1) == '/')
            { position += 2; if (--depth == 0) return; }
            else position++;
        }
        Reject("Unterminated comment.");
    }

    private static void SkipQuoted(string sql, ref int position, char close)
    {
        position++;
        while (position < sql.Length)
        {
            // Backslash escaping depends on server modes. Reject ambiguous text conservatively.
            if (sql[position] == '\\') Reject("Backslash escapes in quoted text are not accepted; use an unambiguous query.");
            if (sql[position++] != close) continue;
            if (Peek(sql, position) != close) return;
            position++;
        }
        Reject("Unterminated quoted text.");
    }

    private static bool TrySkipDollarLiteral(string sql, ref int position)
    {
        var end = position + 1;
        if (Peek(sql, end) != '$')
        {
            if (!(char.IsLetter(Peek(sql, end)) || Peek(sql, end) == '_')) return false;
            while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_')) end++;
            if (Peek(sql, end) != '$') return false;
        }
        var delimiter = sql[position..(end + 1)];
        var close = sql.IndexOf(delimiter, end + 1, StringComparison.Ordinal);
        if (close < 0) Reject("Unterminated dollar-quoted literal.");
        position = close + delimiter.Length;
        return true;
    }

    private static void SkipOracleLiteral(string sql, ref int position)
    {
        var open = Peek(sql, position + 2);
        if (open == '\0' || char.IsWhiteSpace(open) || open == '\'') Reject("Invalid alternative-quoted literal.");
        var close = open switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', _ => open };
        position += 3;
        while (position < sql.Length)
        {
            if (sql[position] == close && Peek(sql, position + 1) == '\'') { position += 2; return; }
            position++;
        }
        Reject("Unterminated alternative-quoted literal.");
    }

    private static void Reject(string reason) => throw new InvalidOperationException($"{reason} Use execute_sql for other statements (requires write access).");
}
