using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: DbProbe <database> <query|@script-file> [server]");
    return 2;
}

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = args.Length >= 3
        ? args[2]
        : Environment.GetEnvironmentVariable("PROXYTYPE_SQL_SERVER") ?? "DESKTOP-DQ0868S",
    InitialCatalog = args[0],
    IntegratedSecurity = true,
    Encrypt = false,
    TrustServerCertificate = true,
    ConnectTimeout = 15,
    ApplicationName = "ProxyType-Database-Tool"
}.ConnectionString;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

if (args[1].StartsWith('@'))
{
    var scriptPath = Path.GetFullPath(args[1][1..]);
    var script = await File.ReadAllTextAsync(scriptPath);
    var batches = Regex.Split(script, @"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .Where(batch => !string.IsNullOrWhiteSpace(batch))
        .ToArray();

    for (var index = 0; index < batches.Length; index++)
    {
        await using var batchCommand = new SqlCommand(batches[index], connection)
        {
            CommandTimeout = 120
        };
        await batchCommand.ExecuteNonQueryAsync();
    }

    Console.WriteLine(JsonSerializer.Serialize(new { script = scriptPath, batches = batches.Length, status = "ok" }));
    return 0;
}

await using var command = new SqlCommand(args[1], connection)
{
    CommandTimeout = 60
};

await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
var resultSets = new List<List<Dictionary<string, object?>>>
();

do
{
    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            row[reader.GetName(i)] = value switch
            {
                byte[] bytes => Convert.ToHexString(bytes),
                DateTime dateTime => dateTime.ToString("O"),
                _ => value
            };
        }

        rows.Add(row);
    }

    resultSets.Add(rows);
} while (await reader.NextResultAsync());

Console.WriteLine(JsonSerializer.Serialize(resultSets, new JsonSerializerOptions
{
    WriteIndented = true
}));
return 0;
