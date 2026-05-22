# Content Publicatie Systeem

.NET 8.0 Web Application that processes files in SharePoint, developed by _I-Experts_ for _Provincie Zeeland_.

The Content Publicatie Systeem is supplied with content from various sources. Archive management and preparatory actions such as anonymization and OCR of documents are performed in the source systems, for example, Zaaksysteem.

The goal of the Content Publicatie Systeem is to make official content available to internal and external channels during the required publication period: from the addition of new content to its destruction date.

## Prerequisites

- Azure App Registration
- Azure Key Vault
- Azure Storage account
- Azure SQL Database

## Storage Account

- File contents and metadata are stored in the Storage Account.

## Database

- The ObjectId and its related SharePoint IDs are stored in the SQL database.
- Publication details are stored in the SQL database.
- Webhook data is stored in the SQL database.

## Getting files as user

With this Web Application an authenticated user can get a file and its metadata.

## Synchronisation

SharePoint sends a webhook notification when changes occur on the public library ([HandleSharePointNotification](#handlesharepointnotification)). When a file is ready for publication, it is stored in the Storage Account and sent to the callback URL.
It is possible to have multilple public libraries in SharePoint. Each library has its own webhook.

## Publication

An Azure Function checks daily for files to publish, based on the publication date in SharePoint. Publication status and scheduling are tracked in the SQL database.

## DropOff

SharePoint sends a webhook notification when changes occur on the DropOff library ([HandleSharePointNotification](#handlesharepointnotification)). The application places this notification on a queue in the Storage account. Notifications are processed one by one by the Azure Function, which calls the application to handle them ([HandleDropOffNotification](#handledropoffnotification)).
It is possible to have multiple DropOff libraries in SharePoint. Each library has its own webhook.

## API Endpoints

The API endpoints are grouped into four categories: objectId, files, export, and webhook.

- ObjectId
  - [CreateId](#createid)
- Files
  - [GetFileUrl](#getfileurl)
  - [GetFileMetadata](#getfilemetadata)
  - [CreateFile](#createfile)
  - [CreateLargeFile](#createlargefile)
  - [UpdateFileContent](#updatefilecontent)
  - [UpdateFileMetadata](#updatefilemetadata)
  - [UpdateLargeFileContent](#updatelargefilecontent)
  - [UpdateFileName](#updatefilename)
- Export
  - [SynchroniseToBePublishedDocuments](#synchronisetobepublisheddocuments)
- Webhook
  - [CreateWebhook](#createwebhook)
  - [HandleWebhookNotificationFromQueue](#handlewebhooknotificationfromqueue)

### CreateId

Create a new ObjectId.  
Requires either DriveId and DriveItemId, or SiteId, ListId, and ListItemId.  
The ObjectId is stored in the SQL database.

### GetFileUrl

Retrieve a file URL by objectId or additionalObjectId. Allowed for authenticated users.

### GetFileMetadata

Retrieve metadata for a file by ObjectId. Allowed for authenticated users.

### CreateFile

Create a file with content and metadata.

### CreateLargeFile

Create a large file by uploading its content.

### UpdateFileContent

Update the content of a file by objectId.

### UpdateLargeFileContent

Update the content of a large file by objectId.

### UpdateFileMetadata

Update the metadata of a file by objectId.

### UpdateFileName

Update the file name of a file by objectId.

### SynchroniseToBePublishedDocuments

Synchronize documents ready for publication:

- Upload content and metadata to the storage container.
- Notify the callback with the new file.

### CreateWebhook

Create a new webhook for a SharePoint list.

### HandleWebhookNotificationFromQueue

Handle webhook notifications from the queue (triggered by the Azure Webhook Function):

- Retrieve files to process
- For each file:
  - Create ObjectId
  - Move the file to the correct site and list

## Author

_I-Experts_ commissioned by _Provincie Zeeland_

## License

[MIT](LICENSE)
