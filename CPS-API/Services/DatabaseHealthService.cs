using System.ComponentModel;
using CPS_API.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CPS_API.Services
{
    public interface IDatabaseHealthService
    {
        Task<T> ExecuteWithWarmupAsync<T>(Func<Task<T>> operation, string operationName);

        Task ExecuteWithWarmupAsync(Func<Task> operation, string operationName);
    }

    public class DatabaseHealthService : IDatabaseHealthService
    {
        private readonly CpsDbContext _dbContext;
        private readonly ILogger<DatabaseHealthService> _logger;
        private const int MaxRetries = 2;
        private const int WarmupDelayMs = 1000;

        public DatabaseHealthService(CpsDbContext dbContext, ILogger<DatabaseHealthService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<T> ExecuteWithWarmupAsync<T>(Func<Task<T>> operation, string operationName)
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (IsColdDatabaseException(ex) && attempt < MaxRetries)
                {
                    _logger.LogWarning(ex, "Database appears cold during {OperationName}. Attempt {Attempt}/{MaxRetries}. Warming up...",
                        operationName, attempt + 1, MaxRetries);

                    await WarmupDatabaseAsync();
                    await Task.Delay(WarmupDelayMs);
                }
                catch (Exception ex)//NOSONAR
                {
                    _logger.LogError(ex, "Error executing {OperationName} after {Attempts} attempts",
                        operationName, attempt + 1);
                    throw;
                }
            }

            throw new InvalidOperationException($"Failed to execute {operationName} after {MaxRetries} retries");
        }

        public async Task ExecuteWithWarmupAsync(Func<Task> operation, string operationName)
        {
            await ExecuteWithWarmupAsync(async () =>
            {
                await operation();
                return 0;
            }, operationName);
        }

        private async Task WarmupDatabaseAsync()
        {
            try
            {
                // Simple query to wake up the database
                await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
                _logger.LogInformation("Database warmed up successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up database");
            }
        }

        private static bool IsColdDatabaseException(Exception ex)
        {
            return (ex is SqlException || ex is Win32Exception) &&
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }
    }
}