-- ObjectIdentifiers Table
CREATE TABLE dbo.ObjectIdentifiers (
    Id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_ObjectIdentifiers PRIMARY KEY,
    Timestamp DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    ObjectId NVARCHAR(255) NOT NULL
        CONSTRAINT UQ_ObjectIdentifiers_ObjectId UNIQUE,
    AdditionalObjectId NVARCHAR(255) NULL,
    DriveId NVARCHAR(100) NOT NULL,
    DriveItemId NVARCHAR(100) NOT NULL,
    SiteId NVARCHAR(50) NOT NULL,
    ListId NVARCHAR(50) NOT NULL,
    ListItemId NVARCHAR(50) NOT NULL
);

-- Automatically create a new ID for record
ALTER TABLE dbo.ObjectIdentifiers
    ADD CONSTRAINT DF_ObjectIdentifiers_Id DEFAULT NEWID() FOR Id;

-- Automatically use current UTC time for Timestamp
ALTER TABLE dbo.ObjectIdentifiers
    ADD CONSTRAINT DF_ObjectIdentifiers_Timestamp DEFAULT SYSUTCDATETIME() FOR Timestamp;

-- Nonclustered indexes to speed up lookups by common identifiers
CREATE INDEX IX_ObjectIdentifiers_AdditionalObj  ON dbo.ObjectIdentifiers (AdditionalObjectId);
CREATE INDEX IX_ObjectIdentifiers_DriveItem      ON dbo.ObjectIdentifiers (DriveId, DriveItemId);
CREATE INDEX IX_ObjectIdentifiers_ListItem       ON dbo.ObjectIdentifiers (SiteId, ListId, ListItemId);

-- Settings Table
CREATE TABLE dbo.Settings (
    Id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Settings PRIMARY KEY,
    SequenceNumber BIGINT NOT NULL
);

-- Automatically create a new ID for record
ALTER TABLE dbo.Settings
    ADD CONSTRAINT DF_Settings_Id DEFAULT NEWID() FOR Id;

-- ToBePublished Table
CREATE TABLE dbo.ToBePublished (
    Id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_ToBePublished PRIMARY KEY,
    ObjectId NVARCHAR(255) NOT NULL,
    PublicationDate DATETIME2(7) NOT NULL
);

-- Automatically create a new ID for record
ALTER TABLE dbo.ToBePublished
    ADD CONSTRAINT DF_ToBePublished_Id DEFAULT NEWID() FOR Id;

-- Nonclustered indexes to speed up lookups by common identifiers
CREATE INDEX IX_ToBePublished_ObjectId       ON dbo.ToBePublished (ObjectId);

-- WebhookSubscription Table
CREATE TABLE dbo.WebhookSubscription (
    Id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_WebhookSubscription PRIMARY KEY,
    LastChangeToken NVARCHAR(100) NULL,
    SubscriptionExpirationDateTime DATETIME2(7) NOT NULL,
    SubscriptionId NVARCHAR(50) NOT NULL,
    WebhookType TINYINT NOT NULL
        CONSTRAINT CK_WebhookSubscription_WebhookType_Enum
        CHECK (WebhookType IN (0,1))
);

-- Automatically create a new ID for record
ALTER TABLE dbo.WebhookSubscription
    ADD CONSTRAINT DF_WebhookSubscription_Id DEFAULT NEWID() FOR Id;