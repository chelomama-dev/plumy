// Creates a database user for a managed identity and gives it read/write access (no schema changes).
// Idempotent: safe to run on every deployment.
//
// Usage: dotnet run infra/scripts/grant-sql-access.cs -- <sql-server-fqdn> <database> <identity-name> <identity-client-id>
// Authenticates as whoever runs it (Entra ID, via DefaultAzureCredential): it must be a SQL server admin.
#:package Microsoft.Data.SqlClient@7.0.2
#:package Microsoft.Data.SqlClient.Extensions.Azure@7.0.2

using System.Data;
using Microsoft.Data.SqlClient;

if (args.Length != 4 || !Guid.TryParse(args[3], out var clientId))
{
    Console.Error.WriteLine("Usage: grant-sql-access.cs <sql-server-fqdn> <database> <identity-name> <identity-client-id>");
    return 1;
}

var (server, database, identity) = (args[0], args[1], args[2]);

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = $"tcp:{server},1433",
    InitialCatalog = database,
    Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
    Encrypt = SqlConnectionEncryptOption.Mandatory,
    // A serverless database may be resuming from auto-pause
    ConnectTimeout = 60,
}.ConnectionString;

// WITH SID instead of FROM EXTERNAL PROVIDER: FROM EXTERNAL PROVIDER makes the SQL server look the name up in
// Microsoft Graph, which fails when the caller is a service principal (the pipeline) unless the server has its own
// identity with Directory Readers. With SID the lookup is skipped: for a managed identity the SID is its client ID.
// If the identity was recreated (new client ID, same name), the stale user is dropped and created again.
// User names and SIDs can't be parameters in DDL, so they go through QUOTENAME / CONVERT inside dynamic SQL.
const string sql = """
    DECLARE @sid varbinary(16) = CAST(@clientId AS varbinary(16));
    DECLARE @user nvarchar(258) = QUOTENAME(@identity);
    DECLARE @stmt nvarchar(max);

    IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @identity AND sid <> @sid)
    BEGIN
        SET @stmt = N'DROP USER ' + @user;
        EXEC sp_executesql @stmt;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @identity)
    BEGIN
        SET @stmt = N'CREATE USER ' + @user + N' WITH SID = ' + CONVERT(nvarchar(34), @sid, 1) + N', TYPE = E';
        EXEC sp_executesql @stmt;
    END

    IF IS_ROLEMEMBER('db_datareader', @identity) = 0
    BEGIN
        SET @stmt = N'ALTER ROLE db_datareader ADD MEMBER ' + @user;
        EXEC sp_executesql @stmt;
    END

    IF IS_ROLEMEMBER('db_datawriter', @identity) = 0
    BEGIN
        SET @stmt = N'ALTER ROLE db_datawriter ADD MEMBER ' + @user;
        EXEC sp_executesql @stmt;
    END
    """;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

await using var command = new SqlCommand(sql, connection);
command.Parameters.Add("@identity", SqlDbType.NVarChar, 128).Value = identity;
command.Parameters.Add("@clientId", SqlDbType.UniqueIdentifier).Value = clientId;
await command.ExecuteNonQueryAsync();

Console.WriteLine($"'{identity}' can read and write data in {database}.");
return 0;
