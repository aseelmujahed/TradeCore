using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeCore.Console.Data;

namespace TradeCore.Tests;

public sealed class TradeCoreApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tradecore-tests-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    public TradeCoreApiFactory()
    {
        _connectionString = $"Data Source={_databasePath};Cache=Shared;Default Timeout=30";

        var options = new DbContextOptionsBuilder<TradeCoreDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        using var dbContext = new TradeCoreDbContext(options);
        dbContext.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TradeCoreDatabase", "Host=localhost;Database=tradecore_test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TradeCoreDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TradeCoreDbContext>>();
            services.RemoveAll<TradeCoreDbContext>();
            services.AddDbContext<TradeCoreDbContext>(options => options.UseSqlite(_connectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // A long-polling SignalR request can release its SQLite connection after fixture teardown.
            }
        }
    }
}
