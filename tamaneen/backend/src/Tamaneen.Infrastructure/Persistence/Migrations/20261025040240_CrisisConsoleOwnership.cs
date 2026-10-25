using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrisisConsoleOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CoSignedByUserId",
                schema: "crisis",
                table: "CrisisEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandoverFromUserId",
                schema: "crisis",
                table: "CrisisEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandoverRequestedAtUtc",
                schema: "crisis",
                table: "CrisisEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandoverToUserId",
                schema: "crisis",
                table: "CrisisEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingCloseAtUtc",
                schema: "crisis",
                table: "CrisisEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingCloseByUserId",
                schema: "crisis",
                table: "CrisisEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingCloseOutcome",
                schema: "crisis",
                table: "CrisisEvents",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingCloseReason",
                schema: "crisis",
                table: "CrisisEvents",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewDueAtUtc",
                schema: "crisis",
                table: "CrisisEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                schema: "crisis",
                table: "CrisisActions",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                schema: "crisis",
                table: "CrisisActions",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoSignedByUserId",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "HandoverFromUserId",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "HandoverRequestedAtUtc",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "HandoverToUserId",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "PendingCloseAtUtc",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "PendingCloseByUserId",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "PendingCloseOutcome",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "PendingCloseReason",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "ReviewDueAtUtc",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "Detail",
                schema: "crisis",
                table: "CrisisActions");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                schema: "crisis",
                table: "CrisisActions");
        }
    }
}
