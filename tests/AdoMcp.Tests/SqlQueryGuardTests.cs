using AdoMcp.Models;
using AdoMcp.Services;

namespace AdoMcp.Tests;

public class SqlQueryGuardTests
{
    [Theory]
    [InlineData(DbType.SqlServer, "SELECT 'DELETE; DROP TABLE t', [UPDATE] FROM [table]; -- DELETE")]
    [InlineData(DbType.SqlServer, "/* outer /* DROP */ comment */ SELECT 1")]
    [InlineData(DbType.SqlServer, "SELECT 'it''s an UPDATE', \"DELETE\"")]
    [InlineData(DbType.MySql, "# DROP\nSELECT `UPDATE`, 'DELETE' /* INSERT */;")]
    [InlineData(DbType.PostgreSql, "SELECT $$DELETE; 'UPDATE'$$, $tag$DROP$tag$")]
    [InlineData(DbType.Oracle, "SELECT q'[it's DELETE; DROP]', q'!UPDATE!' FROM dual")]
    [InlineData(DbType.Sqlite, "WITH c AS (SELECT 1 AS n) SELECT n FROM c;")]
    [InlineData(DbType.Sqlite, "SELECT 'DELETE' AS [UPDATE] UNION ALL SELECT 'INSERT'")]
    public void AcceptsQueriesWithoutMistakingQuotedTextForWrites(DbType type, string sql) => SqlQueryGuard.Validate(sql, type);

    [Theory]
    [InlineData(DbType.Sqlite, "DELETE FROM t")]
    [InlineData(DbType.Sqlite, "SELECT 1; DELETE FROM t")]
    [InlineData(DbType.Sqlite, "SELECT 1; SELECT 2")]
    [InlineData(DbType.Sqlite, "SELECT 1;;")]
    [InlineData(DbType.Sqlite, "WITH c AS (SELECT 1) DELETE FROM t")]
    [InlineData(DbType.PostgreSql, "WITH c AS (DELETE FROM t RETURNING *) SELECT * FROM c")]
    [InlineData(DbType.SqlServer, "SELECT * INTO backup FROM t")]
    [InlineData(DbType.SqlServer, "SELECT 1\nGO\nUPDATE t SET n=2")]
    [InlineData(DbType.MySql, "SELECT * FROM t INTO OUTFILE '/tmp/result'")]
    [InlineData(DbType.MySql, "SELECT 1 /*! UNION SELECT 2 */")]
    [InlineData(DbType.MySql, "SELECT 1 /*M! UNION SELECT 2 */")]
    [InlineData(DbType.MySql, "SELECT 1--2 UPDATE t SET n=2")]
    [InlineData(DbType.PostgreSql, "SELECT * FROM t FOR UPDATE")]
    [InlineData(DbType.PostgreSql, "SELECT * FROM t FOR SHARE")]
    [InlineData(DbType.Sqlite, "PRAGMA query_only=OFF")]
    [InlineData(DbType.Sqlite, "CALL write_data()")]
    [InlineData(DbType.Sqlite, "SELECT 'unterminated")]
    [InlineData(DbType.Sqlite, "SELECT 1 /* unterminated")]
    [InlineData(DbType.PostgreSql, "SELECT $tag$unterminated")]
    [InlineData(DbType.Oracle, "SELECT q'[unterminated' FROM dual")]
    [InlineData(DbType.Sqlite, "SELECT (1")]
    [InlineData(DbType.MySql, "SELECT 'a\\b'")]
    [InlineData(DbType.Sqlite, "/* comments only */")]
    public void RejectsWritesBatchesAndAmbiguousInput(DbType type, string sql) => Assert.Throws<InvalidOperationException>(() => SqlQueryGuard.Validate(sql, type));
}
