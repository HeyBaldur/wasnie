using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B41_AssistantReplyLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ★ INTENTIONALLY EMPTY. AssistantMessages.Content is already nvarchar(max): its declared max length moved
            // from 8,000 (a user's message) to 20,000 (an assistant's reply), and both map to nvarchar(max) on SQL
            // Server. Only the model snapshot changes; there is no SQL to run. Without this migration the model would
            // report pending changes forever.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
