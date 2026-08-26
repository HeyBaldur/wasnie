using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B26_AssistantConversationKeysetIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssistantConversations_TenantId_UserId_UpdatedAt",
                table: "AssistantConversations");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "AssistantConversations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                collation: "Latin1_General_CI_AI",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversations_TenantId_UserId_UpdatedAt_Id",
                table: "AssistantConversations",
                columns: new[] { "TenantId", "UserId", "UpdatedAt", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssistantConversations_TenantId_UserId_UpdatedAt_Id",
                table: "AssistantConversations");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "AssistantConversations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldCollation: "Latin1_General_CI_AI");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversations_TenantId_UserId_UpdatedAt",
                table: "AssistantConversations",
                columns: new[] { "TenantId", "UserId", "UpdatedAt" });
        }
    }
}
