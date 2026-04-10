using CPS_API.Database;
using CPS_API.Models;
using CPS_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CPS_API.Repositories
{
    public interface ISettingsRepository
    {
        Task<long> IncreaseSequenceNumberAsync();
    }

    public class SettingsRepository : ISettingsRepository
    {
        private readonly CpsDbContext _dbContext;
        private readonly IDatabaseHealthService _databaseHealthService;

        public SettingsRepository(
            CpsDbContext dbContext,
            IDatabaseHealthService databaseHealthService)
        {
            _dbContext = dbContext;
            _databaseHealthService = databaseHealthService;
        }

        private async Task<Settings> GetCurrentSettings()
        {
            Settings? currentSetting = await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.Settings.FirstOrDefaultAsync(),
                nameof(GetCurrentSettings)
            );
            if (currentSetting == null) return new Settings();
            return currentSetting;
        }

        public async Task<long> IncreaseSequenceNumberAsync()
        {
            var settings = await ExecuteWithTableLockAsync(async () =>
            {
                Settings settings = await GetCurrentSettings();
                settings.SequenceNumber++;

                await _databaseHealthService.ExecuteWithWarmupAsync(
                    async () => await _dbContext.SaveChangesAsync(),
                    nameof(IncreaseSequenceNumberAsync)
                );
                return settings;
            });
            return settings.SequenceNumber;
        }

        private async Task<Settings> ExecuteWithTableLockAsync(Func<Task<Settings>> action)
        {
            // Lock the settings row in the database to prevent concurrent updates.
            using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync();
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT * FROM [Settings] WITH (TABLOCKX)");

            Settings settings = await action();

            // Release the lock by committing the transaction.
            await transaction.CommitAsync();

            return settings;
        }
    }
}