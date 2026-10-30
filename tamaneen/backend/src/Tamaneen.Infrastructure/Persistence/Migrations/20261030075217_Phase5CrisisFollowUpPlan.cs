using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CrisisFollowUpPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FollowUpCallAtUtc",
                schema: "crisis",
                table: "CrisisEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportPerson",
                schema: "crisis",
                table: "CrisisEvents",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowUpCallAtUtc",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "SupportPerson",
                schema: "crisis",
                table: "CrisisEvents");
        }
    }
}
