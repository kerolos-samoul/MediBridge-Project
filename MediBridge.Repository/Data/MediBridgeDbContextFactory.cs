using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediBridge.Repository.Data;

public sealed class MediBridgeDbContextFactory : IDesignTimeDbContextFactory<MediBridgeDbContext>
{
    public MediBridgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=(localdb)\\mssqllocaldb;Database=MediBridgeDesignTime;Trusted_Connection=True;TrustServerCertificate=True";
        }

        var options = new DbContextOptionsBuilder<MediBridgeDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new MediBridgeDbContext(options);
    }
}
