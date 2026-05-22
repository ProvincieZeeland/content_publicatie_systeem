-- ObjectIdentifiers Table
CREATE TABLE dbo.ObjectIdentifiers (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ObjectIdentifiers PRIMARY KEY CLUSTERED,
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

-- Nonclustered indexes to speed up lookups by common identifiers
CREATE INDEX IX_ObjectIdentifiers_AdditionalObj  ON dbo.ObjectIdentifiers (AdditionalObjectId);
CREATE INDEX IX_ObjectIdentifiers_DriveItem      ON dbo.ObjectIdentifiers (DriveId, DriveItemId);
CREATE INDEX IX_ObjectIdentifiers_ListItem       ON dbo.ObjectIdentifiers (SiteId, ListId, ListItemId);

-- Settings Table
CREATE TABLE dbo.Settings (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Settings PRIMARY KEY CLUSTERED,
    SequenceNumber BIGINT NOT NULL
);

-- ToBePublished Table
CREATE TABLE dbo.ToBePublished (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ToBePublished PRIMARY KEY CLUSTERED,
    ObjectId NVARCHAR(255) NOT NULL,
    PublicationDate DATETIME2(7) NOT NULL
);

-- Nonclustered indexes to speed up lookups by common identifiers
CREATE INDEX IX_ToBePublished_ObjectId ON dbo.ToBePublished (ObjectId);

-- WebhookSubscription Table
CREATE TABLE dbo.WebhookSubscription (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WebhookSubscription PRIMARY KEY CLUSTERED,
    LastChangeToken NVARCHAR(100) NULL,
    SubscriptionExpirationDateTime DATETIME2(7) NOT NULL,
    SubscriptionId NVARCHAR(50) NOT NULL,
    WebhookType TINYINT NOT NULL
        CONSTRAINT CK_WebhookSubscription_WebhookType_Enum
        CHECK (WebhookType IN (0,1))
);