using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkoutLogger.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workouts_UserId",
                table: "Workouts");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Workouts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Workouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Workouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Workouts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_PublicId",
                table: "Workouts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_UserId_UpdatedAt",
                table: "Workouts",
                columns: new[] { "UserId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workouts_PublicId",
                table: "Workouts");

            migrationBuilder.DropIndex(
                name: "IX_Workouts_UserId_UpdatedAt",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Workouts");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_UserId",
                table: "Workouts",
                column: "UserId");
        }
    }
}
