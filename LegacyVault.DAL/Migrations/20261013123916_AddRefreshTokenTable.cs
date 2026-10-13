using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONVERSATIONS",
                columns: table => new
                {
                    ConversationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Direct"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONVERSATIONS", x => x.ConversationId);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReplacedByToken = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ROLES",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLES", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "SUBSCRIPTION_PLANS",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxVaults = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    MaxStorageBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1073741824L),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SUBSCRIPTION_PLANS", x => x.PlanId);
                });

            migrationBuilder.CreateTable(
                name: "USERS",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    IdentityNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "AUDIT_LOGS",
                columns: table => new
                {
                    LogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    IPAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    Result = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Success")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUDIT_LOGS", x => x.LogId);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AUTHENTICATIONS",
                columns: table => new
                {
                    AuthId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUTHENTICATIONS", x => x.AuthId);
                    table.ForeignKey(
                        name: "FK_Auth_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DIGITAL_VAULTS",
                columns: table => new
                {
                    VaultId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    VaultName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DIGITAL_VAULTS", x => x.VaultId);
                    table.ForeignKey(
                        name: "FK_Vaults_Users",
                        column: x => x.OwnerId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "MESSAGES",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    SenderId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MessageType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Text"),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    IsRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MESSAGES", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations",
                        column: x => x.ConversationId,
                        principalTable: "CONVERSATIONS",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Users",
                        column: x => x.SenderId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "PROOF_OF_LIFE",
                columns: table => new
                {
                    CheckId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FrequencyDays = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false, defaultValue: 14),
                    LastPingAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    NextPingDue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    MissedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROOF_OF_LIFE", x => x.CheckId);
                    table.ForeignKey(
                        name: "FK_ProofOfLife_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TWO_FACTOR_AUTHS",
                columns: table => new
                {
                    TwoFactorId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Secret = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TWO_FACTOR_AUTHS", x => x.TwoFactorId);
                    table.ForeignKey(
                        name: "FK_2FA_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "USER_ROLES",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ROLES", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles",
                        column: x => x.RoleId,
                        principalTable: "ROLES",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "USER_SUBSCRIPTIONS",
                columns: table => new
                {
                    SubscriptionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_SUBSCRIPTIONS", x => x.SubscriptionId);
                    table.ForeignKey(
                        name: "FK_UserSub_Plans",
                        column: x => x.PlanId,
                        principalTable: "SUBSCRIPTION_PLANS",
                        principalColumn: "PlanId");
                    table.ForeignKey(
                        name: "FK_UserSub_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DIGITAL_ASSETS",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VaultId = table.Column<int>(type: "int", nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssetType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DIGITAL_ASSETS", x => x.AssetId);
                    table.ForeignKey(
                        name: "FK_Assets_Vaults",
                        column: x => x.VaultId,
                        principalTable: "DIGITAL_VAULTS",
                        principalColumn: "VaultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EXECUTOR_ASSIGNMENTS",
                columns: table => new
                {
                    ExecutorAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VaultId = table.Column<int>(type: "int", nullable: false),
                    ExecutorId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Pending"),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EXECUTOR_ASSIGNMENTS", x => x.ExecutorAssignmentId);
                    table.ForeignKey(
                        name: "FK_ExecAssign_Users",
                        column: x => x.ExecutorId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_ExecAssign_Vaults",
                        column: x => x.VaultId,
                        principalTable: "DIGITAL_VAULTS",
                        principalColumn: "VaultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HANDOVER_REQUESTS",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VaultId = table.Column<int>(type: "int", nullable: false),
                    ExecutorId = table.Column<int>(type: "int", nullable: false),
                    RequestType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Draft"),
                    InitiatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HANDOVER_REQUESTS", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_HandoverReq_Users",
                        column: x => x.ExecutorId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_HandoverReq_Vaults",
                        column: x => x.VaultId,
                        principalTable: "DIGITAL_VAULTS",
                        principalColumn: "VaultId");
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT_TRANSACTIONS",
                columns: table => new
                {
                    TransactionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    TransactionCode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_TRANSACTIONS", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_PayTrans_UserSub",
                        column: x => x.SubscriptionId,
                        principalTable: "USER_SUBSCRIPTIONS",
                        principalColumn: "SubscriptionId");
                    table.ForeignKey(
                        name: "FK_PayTrans_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ASSET_DOCUMENTS",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsEncrypted = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ASSET_DOCUMENTS", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_AssetDocs_Assets",
                        column: x => x.AssetId,
                        principalTable: "DIGITAL_ASSETS",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetDocs_Users",
                        column: x => x.UploadedBy,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "BENEFICIARY_ASSIGNMENTS",
                columns: table => new
                {
                    AssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    BeneficiaryId = table.Column<int>(type: "int", nullable: false),
                    Allocation = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 100.00m),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BENEFICIARY_ASSIGNMENTS", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_BenAssign_Assets",
                        column: x => x.AssetId,
                        principalTable: "DIGITAL_ASSETS",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenAssign_Users",
                        column: x => x.BeneficiaryId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ENCRYPTION_KEYS",
                columns: table => new
                {
                    KeyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    EncryptedKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Algorithm = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "AES-256"),
                    KeyVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    RotatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ENCRYPTION_KEYS", x => x.KeyId);
                    table.ForeignKey(
                        name: "FK_EncKeys_Assets",
                        column: x => x.AssetId,
                        principalTable: "DIGITAL_ASSETS",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HANDOVER_DOCUMENTS",
                columns: table => new
                {
                    HandoverDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsEncrypted = table.Column<bool>(type: "bit", nullable: false),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HANDOVER_DOCUMENTS", x => x.HandoverDocumentId);
                    table.ForeignKey(
                        name: "FK_HandoverDocs_Requests",
                        column: x => x.RequestId,
                        principalTable: "HANDOVER_REQUESTS",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HandoverDocs_Users",
                        column: x => x.UploadedBy,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "LEGAL_VERIFICATIONS",
                columns: table => new
                {
                    VerificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    VerifierId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Pending"),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LEGAL_VERIFICATIONS", x => x.VerificationId);
                    table.ForeignKey(
                        name: "FK_LegalVerif_Requests",
                        column: x => x.RequestId,
                        principalTable: "HANDOVER_REQUESTS",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegalVerif_Users",
                        column: x => x.VerifierId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "HANDOVER_CASES",
                columns: table => new
                {
                    HandoverId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    AssignmentId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "In_Progress"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HANDOVER_CASES", x => x.HandoverId);
                    table.ForeignKey(
                        name: "FK_HandoverCases_Assignments",
                        column: x => x.AssignmentId,
                        principalTable: "BENEFICIARY_ASSIGNMENTS",
                        principalColumn: "AssignmentId");
                    table.ForeignKey(
                        name: "FK_HandoverCases_Requests",
                        column: x => x.RequestId,
                        principalTable: "HANDOVER_REQUESTS",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DIGITAL_SIGNATURES",
                columns: table => new
                {
                    SignatureId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VerificationId = table.Column<int>(type: "int", nullable: false),
                    SignerId = table.Column<int>(type: "int", nullable: false),
                    SignatureData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Valid"),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DIGITAL_SIGNATURES", x => x.SignatureId);
                    table.ForeignKey(
                        name: "FK_Signatures_Users",
                        column: x => x.SignerId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Signatures_Verif",
                        column: x => x.VerificationId,
                        principalTable: "LEGAL_VERIFICATIONS",
                        principalColumn: "VerificationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BENEFICIARY_RECEIPTS",
                columns: table => new
                {
                    ReceiptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HandoverId = table.Column<int>(type: "int", nullable: false),
                    BeneficiaryId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Confirmed"),
                    ConfirmationNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BENEFICIARY_RECEIPTS", x => x.ReceiptId);
                    table.ForeignKey(
                        name: "FK_Receipts_HandoverCases",
                        column: x => x.HandoverId,
                        principalTable: "HANDOVER_CASES",
                        principalColumn: "HandoverId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Receipts_Users",
                        column: x => x.BeneficiaryId,
                        principalTable: "USERS",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATIONS",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    HandoverId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(sysdatetime())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATIONS", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notif_HandoverCases",
                        column: x => x.HandoverId,
                        principalTable: "HANDOVER_CASES",
                        principalColumn: "HandoverId");
                    table.ForeignKey(
                        name: "FK_Notif_Users",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ASSET_DOCUMENTS_AssetId",
                table: "ASSET_DOCUMENTS",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ASSET_DOCUMENTS_UploadedBy",
                table: "ASSET_DOCUMENTS",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AUDIT_LOGS_UserId",
                table: "AUDIT_LOGS",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AUTHENTICATIONS_UserId",
                table: "AUTHENTICATIONS",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BENEFICIARY_ASSIGNMENTS_AssetId",
                table: "BENEFICIARY_ASSIGNMENTS",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_BENEFICIARY_ASSIGNMENTS_BeneficiaryId",
                table: "BENEFICIARY_ASSIGNMENTS",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_BENEFICIARY_RECEIPTS_BeneficiaryId",
                table: "BENEFICIARY_RECEIPTS",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_BENEFICIARY_RECEIPTS_HandoverId",
                table: "BENEFICIARY_RECEIPTS",
                column: "HandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_DIGITAL_ASSETS_VaultId",
                table: "DIGITAL_ASSETS",
                column: "VaultId");

            migrationBuilder.CreateIndex(
                name: "IX_DIGITAL_SIGNATURES_SignerId",
                table: "DIGITAL_SIGNATURES",
                column: "SignerId");

            migrationBuilder.CreateIndex(
                name: "IX_DIGITAL_SIGNATURES_VerificationId",
                table: "DIGITAL_SIGNATURES",
                column: "VerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_DIGITAL_VAULTS_OwnerId",
                table: "DIGITAL_VAULTS",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ENCRYPTION_KEYS_AssetId",
                table: "ENCRYPTION_KEYS",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EXECUTOR_ASSIGNMENTS_ExecutorId",
                table: "EXECUTOR_ASSIGNMENTS",
                column: "ExecutorId");

            migrationBuilder.CreateIndex(
                name: "IX_EXECUTOR_ASSIGNMENTS_VaultId",
                table: "EXECUTOR_ASSIGNMENTS",
                column: "VaultId");

            migrationBuilder.CreateIndex(
                name: "IX_HANDOVER_CASES_AssignmentId",
                table: "HANDOVER_CASES",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_HANDOVER_CASES_RequestId",
                table: "HANDOVER_CASES",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_HANDOVER_DOCUMENTS_RequestId",
                table: "HANDOVER_DOCUMENTS",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_HANDOVER_DOCUMENTS_UploadedBy",
                table: "HANDOVER_DOCUMENTS",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_HANDOVER_REQUESTS_ExecutorId",
                table: "HANDOVER_REQUESTS",
                column: "ExecutorId");

            migrationBuilder.CreateIndex(
                name: "IX_HANDOVER_REQUESTS_VaultId",
                table: "HANDOVER_REQUESTS",
                column: "VaultId");

            migrationBuilder.CreateIndex(
                name: "IX_LEGAL_VERIFICATIONS_RequestId",
                table: "LEGAL_VERIFICATIONS",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LEGAL_VERIFICATIONS_VerifierId",
                table: "LEGAL_VERIFICATIONS",
                column: "VerifierId");

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_ConversationId",
                table: "MESSAGES",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_SenderId",
                table: "MESSAGES",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_HandoverId",
                table: "NOTIFICATIONS",
                column: "HandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_UserId",
                table: "NOTIFICATIONS",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTIONS_SubscriptionId",
                table: "PAYMENT_TRANSACTIONS",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTIONS_UserId",
                table: "PAYMENT_TRANSACTIONS",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PROOF_OF_LIFE_UserId",
                table: "PROOF_OF_LIFE",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_ROLES_RoleName",
                table: "ROLES",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TWO_FACTOR_AUTHS_UserId",
                table: "TWO_FACTOR_AUTHS",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_USER_ROLES_RoleId",
                table: "USER_ROLES",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_USER_SUBSCRIPTIONS_PlanId",
                table: "USER_SUBSCRIPTIONS",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_USER_SUBSCRIPTIONS_UserId",
                table: "USER_SUBSCRIPTIONS",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_USERS_Email",
                table: "USERS",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ASSET_DOCUMENTS");

            migrationBuilder.DropTable(
                name: "AUDIT_LOGS");

            migrationBuilder.DropTable(
                name: "AUTHENTICATIONS");

            migrationBuilder.DropTable(
                name: "BENEFICIARY_RECEIPTS");

            migrationBuilder.DropTable(
                name: "DIGITAL_SIGNATURES");

            migrationBuilder.DropTable(
                name: "ENCRYPTION_KEYS");

            migrationBuilder.DropTable(
                name: "EXECUTOR_ASSIGNMENTS");

            migrationBuilder.DropTable(
                name: "HANDOVER_DOCUMENTS");

            migrationBuilder.DropTable(
                name: "MESSAGES");

            migrationBuilder.DropTable(
                name: "NOTIFICATIONS");

            migrationBuilder.DropTable(
                name: "PAYMENT_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "PROOF_OF_LIFE");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "TWO_FACTOR_AUTHS");

            migrationBuilder.DropTable(
                name: "USER_ROLES");

            migrationBuilder.DropTable(
                name: "LEGAL_VERIFICATIONS");

            migrationBuilder.DropTable(
                name: "CONVERSATIONS");

            migrationBuilder.DropTable(
                name: "HANDOVER_CASES");

            migrationBuilder.DropTable(
                name: "USER_SUBSCRIPTIONS");

            migrationBuilder.DropTable(
                name: "ROLES");

            migrationBuilder.DropTable(
                name: "BENEFICIARY_ASSIGNMENTS");

            migrationBuilder.DropTable(
                name: "HANDOVER_REQUESTS");

            migrationBuilder.DropTable(
                name: "SUBSCRIPTION_PLANS");

            migrationBuilder.DropTable(
                name: "DIGITAL_ASSETS");

            migrationBuilder.DropTable(
                name: "DIGITAL_VAULTS");

            migrationBuilder.DropTable(
                name: "USERS");
        }
    }
}
