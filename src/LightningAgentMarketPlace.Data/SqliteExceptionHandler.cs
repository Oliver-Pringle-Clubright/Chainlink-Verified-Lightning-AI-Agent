namespace LightningAgentMarketPlace.Data;

using Microsoft.Data.Sqlite;

public static class SqliteExceptionHandler
{
    public static bool IsUniqueConstraintViolation(SqliteException ex)
        => ex.SqliteErrorCode == 19 && ex.Message.Contains("UNIQUE");

    public static bool IsForeignKeyViolation(SqliteException ex)
        => ex.SqliteErrorCode == 19 && ex.Message.Contains("FOREIGN KEY");
}
