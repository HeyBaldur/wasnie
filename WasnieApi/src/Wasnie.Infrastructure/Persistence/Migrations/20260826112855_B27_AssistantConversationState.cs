using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B27_AssistantConversationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantConversationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PinnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantConversationStates_AssistantConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversationStates_ConversationId",
                table: "AssistantConversationStates",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversationStates_TenantId_UserId_PinnedAt",
                table: "AssistantConversationStates",
                columns: new[] { "TenantId", "UserId", "PinnedAt" },
                descending: new[] { false, false, true },
                filter: "[PinnedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AssistantConversationStates_UserId_ConversationId",
                table: "AssistantConversationStates",
                columns: new[] { "UserId", "ConversationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantConversationStates");
        }
    }
}
