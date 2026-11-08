namespace SpeakSafe.Web.Data;

/// <summary>
/// Schema applied at startup by <see cref="DbInitializer"/>. Every entry is its own batch: SQL Server parses a whole
/// batch before running it, so DDL and anything that depends on it must never share one.
/// Narratives, message bodies and evidence files are stored encrypted (ASP.NET Data Protection).
/// </summary>
public static class Schema
{
    public const int Version = 1;

    public static readonly (string Name, string Sql)[] Batches =
    [
        ("SchemaVersion", """
            IF OBJECT_ID(N'dbo.SchemaVersion', N'U') IS NULL
            CREATE TABLE dbo.SchemaVersion (
                Version     INT          NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY,
                AppliedUtc  DATETIME2(3) NOT NULL CONSTRAINT DF_SchemaVersion_AppliedUtc DEFAULT SYSUTCDATETIME()
            );
            """),

        ("Survivors", """
            IF OBJECT_ID(N'dbo.Survivors', N'U') IS NULL
            CREATE TABLE dbo.Survivors (
                Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Survivors PRIMARY KEY,
                Nickname      NVARCHAR(20)  NOT NULL,
                PassHash      NVARCHAR(200) NOT NULL,
                RecoveryHash  NVARCHAR(200) NOT NULL,
                Email         NVARCHAR(200) NULL,
                IsSample      BIT           NOT NULL CONSTRAINT DF_Survivors_IsSample DEFAULT 0,
                CreatedUtc    DATETIME2(3)  NOT NULL
            );
            """),

        ("UX_Survivors_Nickname", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Survivors_Nickname' AND object_id = OBJECT_ID(N'dbo.Survivors'))
            CREATE UNIQUE INDEX UX_Survivors_Nickname ON dbo.Survivors (Nickname);
            """),

        ("Organisations", """
            IF OBJECT_ID(N'dbo.Organisations', N'U') IS NULL
            CREATE TABLE dbo.Organisations (
                Id            NVARCHAR(20)  NOT NULL CONSTRAINT PK_Organisations PRIMARY KEY,
                Name          NVARCHAR(150) NOT NULL,
                Initials      NVARCHAR(3)   NOT NULL,
                Color         NVARCHAR(7)   NOT NULL,
                ServicesJson  NVARCHAR(MAX) NOT NULL,
                StatesJson    NVARCHAR(MAX) NOT NULL,
                Description   NVARCHAR(500) NOT NULL,
                ResponseNote  NVARCHAR(80)  NOT NULL,
                Languages     NVARCHAR(120) NOT NULL,
                IsActive      BIT           NOT NULL,
                IsSample      BIT           NOT NULL CONSTRAINT DF_Organisations_IsSample DEFAULT 0,
                UpdatedUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Organisations_UpdatedUtc DEFAULT SYSUTCDATETIME()
            );
            """),

        ("Officers", """
            IF OBJECT_ID(N'dbo.Officers', N'U') IS NULL
            CREATE TABLE dbo.Officers (
                Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Officers PRIMARY KEY,
                OrgId      NVARCHAR(20)  NOT NULL CONSTRAINT FK_Officers_Org REFERENCES dbo.Organisations (Id),
                Name       NVARCHAR(100) NOT NULL,
                Username   NVARCHAR(40)  NOT NULL,
                PassHash   NVARCHAR(200) NOT NULL,
                IsActive   BIT           NOT NULL,
                IsSample   BIT           NOT NULL CONSTRAINT DF_Officers_IsSample DEFAULT 0,
                CreatedUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_Officers_CreatedUtc DEFAULT SYSUTCDATETIME()
            );
            """),

        ("UX_Officers_Username", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Officers_Username' AND object_id = OBJECT_ID(N'dbo.Officers'))
            CREATE UNIQUE INDEX UX_Officers_Username ON dbo.Officers (Username);
            """),

        ("Reports", """
            IF OBJECT_ID(N'dbo.Reports', N'U') IS NULL
            CREATE TABLE dbo.Reports (
                Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reports PRIMARY KEY,
                Code          NVARCHAR(10)  NOT NULL,
                PinHash       NVARCHAR(200) NOT NULL,
                SurvivorId    INT           NULL CONSTRAINT FK_Reports_Survivor REFERENCES dbo.Survivors (Id),
                TypesJson     NVARCHAR(400) NOT NULL,
                Place         NVARCHAR(20)  NOT NULL,
                State         NVARCHAR(40)  NULL,
                IncidentDate  DATE          NULL,
                Frequency     NVARCHAR(40)  NOT NULL,
                Responsible   NVARCHAR(60)  NOT NULL,
                Narrative     NVARCHAR(MAX) NULL,
                Witnesses     NVARCHAR(20)  NOT NULL,
                SupportJson   NVARCHAR(400) NOT NULL,
                IdentityMode  NVARCHAR(20)  NOT NULL,
                OrgId         NVARCHAR(20)  NULL CONSTRAINT FK_Reports_Org REFERENCES dbo.Organisations (Id),
                Status        NVARCHAR(20)  NOT NULL,
                Urgent        BIT           NOT NULL,
                IsSample      BIT           NOT NULL CONSTRAINT DF_Reports_IsSample DEFAULT 0,
                CreatedUtc    DATETIME2(3)  NOT NULL,
                UpdatedUtc    DATETIME2(3)  NOT NULL
            );
            """),

        ("UX_Reports_Code", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Reports_Code' AND object_id = OBJECT_ID(N'dbo.Reports'))
            CREATE UNIQUE INDEX UX_Reports_Code ON dbo.Reports (Code);
            """),

        ("IX_Reports_Survivor", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reports_Survivor' AND object_id = OBJECT_ID(N'dbo.Reports'))
            CREATE INDEX IX_Reports_Survivor ON dbo.Reports (SurvivorId) WHERE SurvivorId IS NOT NULL;
            """),

        ("IX_Reports_Org", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reports_Org' AND object_id = OBJECT_ID(N'dbo.Reports'))
            CREATE INDEX IX_Reports_Org ON dbo.Reports (OrgId) WHERE OrgId IS NOT NULL;
            """),

        ("ReportEvents", """
            IF OBJECT_ID(N'dbo.ReportEvents', N'U') IS NULL
            CREATE TABLE dbo.ReportEvents (
                Id        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReportEvents PRIMARY KEY,
                ReportId  BIGINT        NOT NULL CONSTRAINT FK_ReportEvents_Report REFERENCES dbo.Reports (Id),
                Kind      NVARCHAR(20)  NOT NULL,
                Note      NVARCHAR(300) NULL,
                ByWho     NVARCHAR(150) NOT NULL,
                Utc       DATETIME2(3)  NOT NULL
            );
            """),

        ("IX_ReportEvents_Report", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReportEvents_Report' AND object_id = OBJECT_ID(N'dbo.ReportEvents'))
            CREATE INDEX IX_ReportEvents_Report ON dbo.ReportEvents (ReportId, Utc);
            """),

        ("Evidence", """
            IF OBJECT_ID(N'dbo.Evidence', N'U') IS NULL
            CREATE TABLE dbo.Evidence (
                Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Evidence PRIMARY KEY,
                ReportId     BIGINT        NOT NULL CONSTRAINT FK_Evidence_Report REFERENCES dbo.Reports (Id),
                FileName     NVARCHAR(120) NOT NULL,
                ContentType  NVARCHAR(60)  NOT NULL,
                SizeBytes    BIGINT        NOT NULL,
                StoredName   NVARCHAR(40)  NOT NULL,
                Utc          DATETIME2(3)  NOT NULL
            );
            """),

        ("IX_Evidence_Report", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Evidence_Report' AND object_id = OBJECT_ID(N'dbo.Evidence'))
            CREATE INDEX IX_Evidence_Report ON dbo.Evidence (ReportId);
            """),

        ("Conversations", """
            IF OBJECT_ID(N'dbo.Conversations', N'U') IS NULL
            CREATE TABLE dbo.Conversations (
                Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Conversations PRIMARY KEY,
                SurvivorId       INT           NULL CONSTRAINT FK_Conversations_Survivor REFERENCES dbo.Survivors (Id),
                ReportId         BIGINT        NULL CONSTRAINT FK_Conversations_Report REFERENCES dbo.Reports (Id),
                OrgId            NVARCHAR(20)  NOT NULL CONSTRAINT FK_Conversations_Org REFERENCES dbo.Organisations (Id),
                IsSample         BIT           NOT NULL CONSTRAINT DF_Conversations_IsSample DEFAULT 0,
                CreatedUtc       DATETIME2(3)  NOT NULL,
                LastUtc          DATETIME2(3)  NOT NULL,
                SurvivorReadUtc  DATETIME2(3)  NOT NULL,
                OrgReadUtc       DATETIME2(3)  NOT NULL
            );
            """),

        ("IX_Conversations_Survivor", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_Survivor' AND object_id = OBJECT_ID(N'dbo.Conversations'))
            CREATE INDEX IX_Conversations_Survivor ON dbo.Conversations (SurvivorId) WHERE SurvivorId IS NOT NULL;
            """),

        ("IX_Conversations_Report", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_Report' AND object_id = OBJECT_ID(N'dbo.Conversations'))
            CREATE INDEX IX_Conversations_Report ON dbo.Conversations (ReportId) WHERE ReportId IS NOT NULL;
            """),

        ("IX_Conversations_Org", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_Org' AND object_id = OBJECT_ID(N'dbo.Conversations'))
            CREATE INDEX IX_Conversations_Org ON dbo.Conversations (OrgId, LastUtc DESC);
            """),

        ("Messages", """
            IF OBJECT_ID(N'dbo.Messages', N'U') IS NULL
            CREATE TABLE dbo.Messages (
                Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Messages PRIMARY KEY,
                ConversationId  BIGINT        NOT NULL CONSTRAINT FK_Messages_Conversation REFERENCES dbo.Conversations (Id),
                Sender          NVARCHAR(10)  NOT NULL,
                OfficerId       INT           NULL CONSTRAINT FK_Messages_Officer REFERENCES dbo.Officers (Id),
                Body            NVARCHAR(MAX) NOT NULL,
                Utc             DATETIME2(3)  NOT NULL
            );
            """),

        ("IX_Messages_Conversation", """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Messages_Conversation' AND object_id = OBJECT_ID(N'dbo.Messages'))
            CREATE INDEX IX_Messages_Conversation ON dbo.Messages (ConversationId, Id);
            """),

        ("SchemaVersion row", $"""
            IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = {Version})
            INSERT INTO dbo.SchemaVersion (Version) VALUES ({Version});
            """),
    ];
}
