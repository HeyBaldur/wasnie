using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wasnie.Infrastructure.Persistence;

#nullable disable

namespace Wasnie.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260612084512_A10_AddCancelAtPeriodEnd")]
    /// <inheritdoc />
    public partial class A10_AddCancelAtPeriodEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelAtPeriodEnd",
                table: "UserSubscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelAt",
                table: "UserSubscriptions",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelAtPeriodEnd",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "CancelAt",
                table: "UserSubscriptions");
        }
    }
}
