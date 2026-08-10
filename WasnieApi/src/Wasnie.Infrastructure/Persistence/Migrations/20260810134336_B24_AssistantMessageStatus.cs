using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B24_AssistantMessageStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ★ THE DEFAULT IS `Complete`, NOT THE EMPTY STRING EF SCAFFOLDED. Every turn written before
            // this column existed did run to its end — cancelling was not possible — so `Complete` is
            // the recorded truth for all of them and not a convenient guess. An empty string would be a
            // value no client knows how to read, sitting on the whole of the existing chat history.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AssistantMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Complete");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "AssistantMessages");
        }
    }
}
