using Microsoft.Data.SqlClient;

namespace MediBridge.TestInfrastructure;

internal sealed class LocalDbTestDatabase
{
    private const string DeleteDatabaseCommand = """
        DECLARE @statement nvarchar(max);

        IF DB_ID(@databaseName) IS NOT NULL
        BEGIN
            SET @statement =
                CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM sys.databases
                        WHERE name = @databaseName AND state_desc = N'ONLINE')
                    THEN N'ALTER DATABASE ' + QUOTENAME(@databaseName)
                        + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; '
                    ELSE N''
                END
                + N'DROP DATABASE ' + QUOTENAME(@databaseName) + N';';

            EXEC sys.sp_executesql @statement;
        END;
        """;

    private readonly string masterConnectionString;
    private int deletionStarted;

    public LocalDbTestDatabase(string namePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);

        if (namePrefix.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.'))
        {
            throw new ArgumentException(
                "The database name prefix may contain only ASCII letters, digits, and periods.",
                nameof(namePrefix));
        }

        DatabaseName = $"{namePrefix}_{Guid.NewGuid():N}";
        ConnectionString = BuildConnectionString(DatabaseName, pooling: true);
        masterConnectionString = BuildConnectionString("master", pooling: false);
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public void Delete()
    {
        if (Interlocked.CompareExchange(ref deletionStarted, 1, 0) != 0)
        {
            return;
        }

        try
        {
            ClearDatabasePool();

            using var connection = new SqlConnection(masterConnectionString);
            connection.Open();

            using var command = CreateDeleteCommand(connection);
            command.ExecuteNonQuery();
        }
        catch
        {
            Volatile.Write(ref deletionStarted, 0);
            throw;
        }
    }

    public async ValueTask DeleteAsync()
    {
        if (Interlocked.CompareExchange(ref deletionStarted, 1, 0) != 0)
        {
            return;
        }

        try
        {
            ClearDatabasePool();

            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();

            await using var command = CreateDeleteCommand(connection);
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            Volatile.Write(ref deletionStarted, 0);
            throw;
        }
    }

    private SqlCommand CreateDeleteCommand(SqlConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = DeleteDatabaseCommand;
        command.Parameters.AddWithValue("@databaseName", DatabaseName);
        return command;
    }

    private void ClearDatabasePool()
    {
        using var databaseConnection = new SqlConnection(ConnectionString);
        SqlConnection.ClearPool(databaseConnection);
    }

    private static string BuildConnectionString(string databaseName, bool pooling)
    {
        return new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            Pooling = pooling
        }.ConnectionString;
    }
}
