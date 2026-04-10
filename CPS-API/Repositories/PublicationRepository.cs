using System.Net;
using Azure;
using CPS_API.Database;
using CPS_API.Models;
using CPS_API.Services;
using Microsoft.EntityFrameworkCore;

namespace CPS_API.Repositories
{
    public interface IPublicationRepository
    {
        Task<List<ToBePublished>> GetItemsFromQueueAsync(int maxRetries = 2);

        Task AddToQueueAsync(string objectId, DateTime publicationDate);

        Task RemoveFromQueueIfExistsAsync(string objectId);

        Task RemoveFromQueueAsync(ToBePublished item);
    }

    public class PublicationRepository : IPublicationRepository
    {
        private readonly CpsDbContext _dbContext;
        private readonly IDatabaseHealthService _databaseHealthService;

        public PublicationRepository(
            CpsDbContext dbContext,
            IDatabaseHealthService databaseHealthService)
        {
            _dbContext = dbContext;
            _databaseHealthService = databaseHealthService;
        }

        #region Get

        public async Task<List<ToBePublished>> GetItemsFromQueueAsync(int maxRetries = 2)
        {
            return await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ToBePublished.ToListAsync(),
                nameof(GetItemsFromQueueAsync)
            );
        }

        private async Task<ToBePublished?> GetToBePublishedAsync(string objectId)
        {
            return await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.ToBePublished.FirstOrDefaultAsync(tp => tp.ObjectId.Equals(objectId)),
                nameof(GetToBePublishedAsync)
            );
        }

        #endregion

        #region Save and Delete

        public async Task AddToQueueAsync(string objectId, DateTime publicationDate)
        {
            _dbContext.ToBePublished.Add(new ToBePublished
            {
                ObjectId = objectId,
                PublicationDate = publicationDate
            });
            await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.SaveChangesAsync(),
                nameof(AddToQueueAsync)
            );
        }

        public async Task RemoveFromQueueIfExistsAsync(string objectId)
        {
            ToBePublished? item;
            try
            {
                item = await GetToBePublishedAsync(objectId);
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
                return;
            }
            if (item == null)
            {
                return;
            }
            await RemoveFromQueueAsync(item);
        }

        public async Task RemoveFromQueueAsync(ToBePublished item)
        {
            _dbContext.ToBePublished.Remove(item);
            await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.SaveChangesAsync(),
                nameof(RemoveFromQueueAsync)
            );
        }

        #endregion
    }
}