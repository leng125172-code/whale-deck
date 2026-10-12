using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhaleDeck.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var connectionString = builder.Configuration.GetConnectionString("WhaleDeck")
    ?? throw new InvalidOperationException("ConnectionStrings:WhaleDeck is required.");
builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5)));

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var database = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
// Data-preserving metric repartitioning has a bounded five-minute statement budget.
database.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
await database.Database.MigrateAsync();
Console.WriteLine("Whale Deck database migrations completed.");
