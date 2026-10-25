using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingIntakeRetriesFormModeDismissReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DismissReason",
                schema: "clinical",
                table: "MatchSuggestions",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FormMode",
                schema: "clinical",
                table: "IntakeSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientMessageId",
                schema: "clinical",
                table: "IntakeMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeMessages_IntakeId_ClientMessageId",
                schema: "clinical",
                table: "IntakeMessages",
                columns: new[] { "IntakeId", "ClientMessageId" },
                unique: true,
                filter: "[ClientMessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntakeMessages_IntakeId_ClientMessageId",
                schema: "clinical",
                table: "IntakeMessages");

            migrationBuilder.DropColumn(
                name: "DismissReason",
                schema: "clinical",
                table: "MatchSuggestions");

            migrationBuilder.DropColumn(
                name: "FormMode",
                schema: "clinical",
                table: "IntakeSessions");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                schema: "clinical",
                table: "IntakeMessages");
        }
    }
}
