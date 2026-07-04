using FluentAssertions;
using MediBridge.Repository.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.TestHost;

public sealed class ConfiguredWebAppFactoryLifecycleTests
{
    [Fact]
    public async Task DisposeAsync_DropsGeneratedDatabase()
    {
        var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var databaseName = GetDatabaseName(factory);

        await factory.DisposeAsync();

        (await DatabaseExistsAsync(databaseName)).Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_DropsGeneratedDatabase()
    {
        var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var databaseName = GetDatabaseName(factory);

        factory.Dispose();

        (await DatabaseExistsAsync(databaseName)).Should().BeFalse();
    }

    private static string GetDatabaseName(ConfiguredWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return context.Database.GetDbConnection().Database;
    }

    private static async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        await using var connection = new SqlConnection(
            "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sys.databases WHERE name = @databaseName;";
        command.Parameters.AddWithValue("@databaseName", databaseName);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}
