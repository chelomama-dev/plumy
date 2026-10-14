// Creates a database user for a managed identity and gives it read/write access (no schema changes).
// Idempotent: safe to run on every deployment.
//
// Usage: dotnet run infra/scripts/grant-sql-access.cs -- <sql-server-fqdn> <database> <identity-name>
// Authenticates as whoever runs it (Entra ID, via DefaultAzureCredential): it must be the SQL server admin.
#:package Microsoft.Data.SqlClient@7.0.2
#:package Microsoft.Data.SqlClient.Extensions.Azure@7.0.2

using Microsoft.Data.SqlClient;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: grant-sql-access.cs <sql-server-fqdn> <database> <identity-name>");
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

// The user name can't be a parameter in CREATE USER / ALTER ROLE, so it goes through QUOTENAME inside dynamic SQL
const string sql = """
    DECLARE @user sysname = QUOTENAME(@identity);

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @identity)
        EXEC (N'CREATE USER ' + @user + N' FROM EXTERNAL PROVIDER');

    IF IS_ROLEMEMBER('db_datareader', @identity) = 0
        EXEC (N'ALTER ROLE db_datareader ADD MEMBER ' + @user);

    IF IS_ROLEMEMBER('db_datawriter', @identity) = 0
        EXEC (N'ALTER ROLE db_datawriter ADD MEMBER ' + @user);
    """;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

await using var command = new SqlCommand(sql, connection);
command.Parameters.AddWithValue("@identity", identity);
await command.ExecuteNonQueryAsync();

Console.WriteLine($"'{identity}' can read and write data in {database}.");
return 0;
