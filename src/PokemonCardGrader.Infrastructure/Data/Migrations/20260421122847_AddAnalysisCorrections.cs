using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonCardGrader.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrectedScores = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Correction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalOverlay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalScores = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCorrections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCorrections_CardSubmissionId",
                table: "AnalysisCorrections",
                column: "CardSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCorrections_CreatedAt",
                table: "AnalysisCorrections",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisCorrections");
        }
    }
}
