using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Repositories
{
    public interface IDocumentsRepository
    {

        Task<DocumentsEntity?> GetContentIdsAsync(string contentId);

        Task<bool> SaveContentIdsAsync(string contentId, Drive? drive, DriveItem? driveItem, ContentIds contentIds);
    }

    public class DocumentsRepository : IDocumentsRepository
    {
        private readonly StorageTableService _storageTableService;

        public DocumentsRepository(StorageTableService storageTableService)
        {
            this._storageTableService = storageTableService;
        }

        private CloudTable? GetDocumentsTable()
        {
            var table = this._storageTableService.GetTable("documents");
            return table;
        }

        public async Task<DocumentsEntity?> GetContentIdsAsync(string contentId)
        {
            var documentsTable = this.GetDocumentsTable();
            if (documentsTable == null)
            {
                return null;
            }

            var documentsEntity = await this._storageTableService.GetAsync<DocumentsEntity>(contentId, contentId, documentsTable);
            return documentsEntity;
        }

        public async Task<bool> SaveContentIdsAsync(string contentId, Drive? drive, DriveItem? driveItem, ContentIds contentIds)
        {
            var documentsTable = this.GetDocumentsTable();
            if (documentsTable == null)
            {
                return false;
            }

            var document = new DocumentsEntity(contentId, drive, driveItem, contentIds);
            await this._storageTableService.SaveAsync(documentsTable, document);
            return true;
        }
    }
}
