using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WorkoutLogger.Migrations
{
    /// <inheritdoc />
    public partial class AddGarminTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GarminActivityDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    GarminActivityId = table.Column<long>(type: "bigint", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivityType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    AverageHeartRate = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRate = table.Column<int>(type: "integer", nullable: true),
                    Calories = table.Column<double>(type: "double precision", nullable: true),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: true),
                    AerobicTrainingEffect = table.Column<double>(type: "double precision", nullable: true),
                    AnaerobicTrainingEffect = table.Column<double>(type: "double precision", nullable: true),
                    AverageWatts = table.Column<double>(type: "double precision", nullable: true),
                    ActiveSetCount = table.Column<int>(type: "integer", nullable: true),
                    TotalReps = table.Column<int>(type: "integer", nullable: true),
                    TotalWeightKg = table.Column<double>(type: "double precision", nullable: true),
                    ActivityId = table.Column<int>(type: "integer", nullable: true),
                    RawSummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    RawSetsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarminActivityDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GarminActivityDetails_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GarminActivityDetails_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GarminDailyMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RestingHeartRate = table.Column<int>(type: "integer", nullable: true),
                    HrvLastNightAvg = table.Column<int>(type: "integer", nullable: true),
                    HrvStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BodyBatteryHigh = table.Column<int>(type: "integer", nullable: true),
                    BodyBatteryLow = table.Column<int>(type: "integer", nullable: true),
                    SleepScore = table.Column<int>(type: "integer", nullable: true),
                    SleepSeconds = table.Column<int>(type: "integer", nullable: true),
                    TrainingReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    TrainingStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Vo2MaxRunning = table.Column<double>(type: "double precision", nullable: true),
                    AverageStress = table.Column<int>(type: "integer", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarminDailyMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GarminDailyMetrics_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GarminActivityDetails_ActivityId",
                table: "GarminActivityDetails",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_GarminActivityDetails_UserId_GarminActivityId",
                table: "GarminActivityDetails",
                columns: new[] { "UserId", "GarminActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GarminActivityDetails_UserId_StartTime",
                table: "GarminActivityDetails",
                columns: new[] { "UserId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_GarminDailyMetrics_UserId_Date",
                table: "GarminDailyMetrics",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GarminActivityDetails");

            migrationBuilder.DropTable(
                name: "GarminDailyMetrics");
        }
    }
}
