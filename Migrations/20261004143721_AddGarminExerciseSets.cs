using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WorkoutLogger.Migrations
{
    /// <inheritdoc />
    public partial class AddGarminExerciseSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GarminExerciseSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GarminActivityDetailId = table.Column<int>(type: "integer", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    GarminReps = table.Column<int>(type: "integer", nullable: true),
                    GarminCategory = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    GarminCategoryProbability = table.Column<double>(type: "double precision", nullable: true),
                    AverageHeartRate = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRate = table.Column<int>(type: "integer", nullable: true),
                    MinHeartRate = table.Column<int>(type: "integer", nullable: true),
                    RestSecondsAfter = table.Column<int>(type: "integer", nullable: true),
                    ExerciseSetId = table.Column<int>(type: "integer", nullable: true),
                    MatchConfidence = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarminExerciseSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GarminExerciseSets_ExerciseSets_ExerciseSetId",
                        column: x => x.ExerciseSetId,
                        principalTable: "ExerciseSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GarminExerciseSets_GarminActivityDetails_GarminActivityDeta~",
                        column: x => x.GarminActivityDetailId,
                        principalTable: "GarminActivityDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GarminExerciseSets_ExerciseSetId",
                table: "GarminExerciseSets",
                column: "ExerciseSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GarminExerciseSets_GarminActivityDetailId_SetIndex",
                table: "GarminExerciseSets",
                columns: new[] { "GarminActivityDetailId", "SetIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GarminExerciseSets");
        }
    }
}
