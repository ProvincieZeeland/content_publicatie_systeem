# Content Publicatie Systeem

.NET 8.0 Web Application which processes files in SharePoint developed by _I-Experts_ for _Provincie Zeeland_.

The Content Publicatie Systeem is fed from various sources. Archive management and preparatory actions such as anonymizing and OCR-ing documents take place in the source systems, for example, Zaaksysteem.

The goal of the Content Publicatie Systeem is to make official content available to internal and external channels during the period that the content must be published: from the addition of new content to the destruction date.

## Prerequisites

- Azure App registration
- Azure Key Vault
- Azure Storage account

## Getting files as user

With this Web Application a logged in user can get a file and its metadata.

## Synchronisation

Every 15 minutes an Azure Function will run to synchronise public files. If the public file is ready for publication then these public file will be saved in the Storage account and send to the callback url.

## Publication

Every day an Azure Function will run to check files that need to be published. The publication is determined from the saves publication date in SharePoint.

## DropOff

This Web Application handles files from a DropOff location in SharePoint. SharePoint will send a webhook notification to identify that something has changed ([HandleSharePointNotification](#handlesharepointnotification)). The Application will put this notification on a queue in the Storage account. One by one these notifications will be handled by the Azure Function. The Azure Funtion will call the Application to handle the notifications ([HandleDropOffNotification](#handledropoffnotification)).

## Endpoints

The API endpoints can be divided into four parts; objectId, files, export and webhook

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
  - [SynchroniseNewDocuments](#synchronisenewdocuments)
  - [SynchroniseUpdatedDocuments](#synchroniseupdateddocuments)
  - [SynchroniseDeletedDocuments](#synchronisedeleteddocuments)
- Webhook
  - [CreateWebhook](#createwebhook)
  - [HandleSharePointNotification](#handlesharepointnotification)
  - [HandleDropOffNotification](#handledropoffnotification)

### CreateId

Create a new ObjectId.\
DriveId and driveItemId requierd. Or siteId, listId and listItemId required.

### GetFileURL

Get file url by objectId or additionalObjectId. Allowed for logged in user.

### GetFileMetadata

Get metadata for file by ObjectId. Allowed for logged in user.

### CreateFile

Create file by content and metadata.

### CreateLargeFile

Create large file by content.

### UpdateFileContent

Update content for file by objectId.

### UpdateLargeFileContent

Update content for large file by objectId.

### UpdateFileMetadata

Update metadata for file by objectId.

### UpdateFileName

Update fileName for file by objectId.

### SynchroniseNewDocuments

Synchronise new documents

- Upload content and metadata to storage container
- Send new file to callback

### SynchroniseUpdatedDocuments

Synchronise updated documents

- Update content and metadata in storage container
- Send updated file to callback

### SynchroniseDeletedDocuments

Synchronise deleted documents

- Delete content and metadata from storage container
- Send deleted file to callback

### CreateWebhook

Create a new webhook for DropOff list.

### HandleSharePointNotification

Handle Webhook notification from SharePoint

- If a validationToken is send then return this token
- If notifications are send then put them on a queue

### HandleDropOffNotification

Handle DropOff notification from queue (placed with HandleSharePointNotification).

- Get files to process
- For each file
  - Create ObjectId
  - Move file to the correct site and list

## Author

_I-Experts_ commisioned by _Provincie Zeeland_

## License

[MIT](LICENSE)
