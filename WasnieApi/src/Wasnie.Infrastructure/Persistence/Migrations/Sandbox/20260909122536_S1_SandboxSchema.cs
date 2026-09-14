using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations.Sandbox
{
    /// <inheritdoc />
    public partial class S1_SandboxSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Sandbox");

            migrationBuilder.CreateTable(
                name: "CategoryMappings",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InputValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompensationPlans",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EffectiveStart = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PeriodType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClawbackMaturationDays = table.Column<int>(type: "int", nullable: true),
                    ClawbackCapPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompensationTransactions",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProductSku = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SelectedPlanAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IngestedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Credits",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    OriginalCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreditedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreditedCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    SplitPercentage = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AllocatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AllocatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersededBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedByPayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClosureNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CalculationTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RateRefusal = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayeeBalances",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BalanceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayeeBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayeeLedgerEntries",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourcePayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceExternalDealId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourcePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceCommissionAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DaysActive = table.Column<int>(type: "int", nullable: true),
                    MaturationDays = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayeeLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payees",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TerminationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmploymentType = table.Column<int>(type: "int", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AccountClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AccountClosedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payees_Payees_ManagerId",
                        column: x => x.ManagerId,
                        principalSchema: "Sandbox",
                        principalTable: "Payees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayRuns",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SupplementalSequence = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PayeeCount = table.Column<int>(type: "int", nullable: false),
                    PaidPayeeCount = table.Column<int>(type: "int", nullable: false),
                    ZeroPayoutCount = table.Column<int>(type: "int", nullable: false),
                    TotalAmounts = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayRunSettlements",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    GrossCommission = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    GrossCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ClawbackWithheld = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WithheldCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    NetPaid = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    NetCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CarryoverRemaining = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CarryoverCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRunSettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanAssignments",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotPayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotFullName = table.Column<string>(type: "nvarchar(201)", maxLength: 201, nullable: false),
                    SnapshotEmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EffectiveStart = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quotas",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotaAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuotaCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MeasurementType = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationClosures",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryKind = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FactOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FactKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ClosedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationClosures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanRules",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Measurement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RateTable = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cap = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Floor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectivePeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectivePeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(50)", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StopReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRules_CompensationPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "Sandbox",
                        principalTable: "CompensationPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompensationPayouts",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotPayeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotFullName = table.Column<string>(type: "nvarchar(201)", maxLength: 201, nullable: false),
                    SnapshotEmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalCommissionAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalCommissionCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DiscardedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DiscardedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DiscardReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CalculatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompensationPayouts_PayRuns_PayRunId",
                        column: x => x.PayRunId,
                        principalSchema: "Sandbox",
                        principalTable: "PayRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayoutLines",
                schema: "Sandbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BaseCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CommissionCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    AppliedModifiers = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutLines_CompensationPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalSchema: "Sandbox",
                        principalTable: "CompensationPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryMappings_TenantId_InputField_InputValue",
                schema: "Sandbox",
                table: "CategoryMappings",
                columns: new[] { "TenantId", "InputField", "InputValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPayouts_PayRunId",
                schema: "Sandbox",
                table: "CompensationPayouts",
                column: "PayRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPayouts_Tenant_Status_PaidAt",
                schema: "Sandbox",
                table: "CompensationPayouts",
                columns: new[] { "TenantId", "Status", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPayouts_TenantId_PayeeId",
                schema: "Sandbox",
                table: "CompensationPayouts",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPlans_TenantId_Name",
                schema: "Sandbox",
                table: "CompensationPlans",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPlans_TenantId_Status",
                schema: "Sandbox",
                table: "CompensationPlans",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_IngestedAt",
                schema: "Sandbox",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "IngestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_PayeeId",
                schema: "Sandbox",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_ReferenceNumber",
                schema: "Sandbox",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "ReferenceNumber" },
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_Source_ExternalId",
                schema: "Sandbox",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL AND [Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_Status",
                schema: "Sandbox",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationTransactions_TenantId_TransactionDate",
                schema: "Sandbox",
                table: "CompensationTransactions",
                columns: new[] { "TenantId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Credits_ConsumedByPayoutId",
                schema: "Sandbox",
                table: "Credits",
                column: "ConsumedByPayoutId",
                filter: "[ConsumedByPayoutId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_ConsumedAt",
                schema: "Sandbox",
                table: "Credits",
                columns: new[] { "TenantId", "ConsumedAt" },
                filter: "[ConsumedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_PayeeId_Outstanding",
                schema: "Sandbox",
                table: "Credits",
                columns: new[] { "TenantId", "PayeeId" },
                filter: "[SupersededAt] IS NULL AND [ConsumedAt] IS NULL AND [ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_RateRefusal",
                schema: "Sandbox",
                table: "Credits",
                columns: new[] { "TenantId", "RateRefusal" },
                filter: "[RateRefusal] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_SupersededAt",
                schema: "Sandbox",
                table: "Credits",
                columns: new[] { "TenantId", "SupersededAt" },
                filter: "[SupersededAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Credits_TenantId_TransactionId_PayeeId",
                schema: "Sandbox",
                table: "Credits",
                columns: new[] { "TenantId", "TransactionId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "UX_Credits_Tenant_Transaction_Plan_Rule_Live",
                schema: "Sandbox",
                table: "Credits",
                columns: new[] { "TenantId", "TransactionId", "PlanId", "RuleId" },
                unique: true,
                filter: "[SupersededAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PayeeBalances_Tenant_Payee_Currency",
                schema: "Sandbox",
                table: "PayeeBalances",
                columns: new[] { "TenantId", "PayeeId", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayeeLedgerEntries_SourceTransaction",
                schema: "Sandbox",
                table: "PayeeLedgerEntries",
                column: "SourceTransactionId",
                filter: "[SourceTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayeeLedgerEntries_Tenant_Payee_CreatedAt",
                schema: "Sandbox",
                table: "PayeeLedgerEntries",
                columns: new[] { "TenantId", "PayeeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PayeeLedgerEntries_ChurnPerTransactionPlan",
                schema: "Sandbox",
                table: "PayeeLedgerEntries",
                columns: new[] { "SourceTransactionId", "SourcePlanId" },
                unique: true,
                filter: "[SourceType] = 'DealChurn' AND [SourceTransactionId] IS NOT NULL AND [SourcePlanId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_ManagerId",
                schema: "Sandbox",
                table: "Payees",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Email",
                schema: "Sandbox",
                table: "Payees",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_EmployeeCode",
                schema: "Sandbox",
                table: "Payees",
                columns: new[] { "TenantId", "EmployeeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_FullName",
                schema: "Sandbox",
                table: "Payees",
                columns: new[] { "TenantId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Payees_TenantId_Status",
                schema: "Sandbox",
                table: "Payees",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Payees_Tenant_UserId",
                schema: "Sandbox",
                table: "Payees",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutLines_PayoutId",
                schema: "Sandbox",
                table: "PayoutLines",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_TenantId",
                schema: "Sandbox",
                table: "PayRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_Unique",
                schema: "Sandbox",
                table: "PayRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "SupplementalSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayRunSettlements_Tenant_Payee_AppliedAt",
                schema: "Sandbox",
                table: "PayRunSettlements",
                columns: new[] { "TenantId", "PayeeId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PayRunSettlements_Run_Payee_Currency",
                schema: "Sandbox",
                table: "PayRunSettlements",
                columns: new[] { "TenantId", "PayRunId", "PayeeId", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_TenantId_PayeeId",
                schema: "Sandbox",
                table: "PlanAssignments",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_TenantId_PlanId_PayeeId",
                schema: "Sandbox",
                table: "PlanAssignments",
                columns: new[] { "TenantId", "PlanId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_TenantId_Status",
                schema: "Sandbox",
                table: "PlanAssignments",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanRules_PlanId_SortOrder",
                schema: "Sandbox",
                table: "PlanRules",
                columns: new[] { "PlanId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotas_TenantId_PayeeId",
                schema: "Sandbox",
                table: "Quotas",
                columns: new[] { "TenantId", "PayeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotas_TenantId_PayeeId_PlanId",
                schema: "Sandbox",
                table: "Quotas",
                columns: new[] { "TenantId", "PayeeId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotas_TenantId_Status",
                schema: "Sandbox",
                table: "Quotas",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationClosures_Tenant_Entry_Reason_Fact",
                schema: "Sandbox",
                table: "ReconciliationClosures",
                columns: new[] { "TenantId", "EntryKind", "EntityId", "Reason", "FactOccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryMappings",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "CompensationTransactions",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "Credits",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PayeeBalances",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PayeeLedgerEntries",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "Payees",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PayoutLines",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PayRunSettlements",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PlanAssignments",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PlanRules",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "Quotas",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "ReconciliationClosures",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "CompensationPayouts",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "CompensationPlans",
                schema: "Sandbox");

            migrationBuilder.DropTable(
                name: "PayRuns",
                schema: "Sandbox");
        }
    }
}
