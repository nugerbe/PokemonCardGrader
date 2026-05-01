using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonCardGrader.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRowVersionToSubmissionAndImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CardSubmissions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CardImages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CardSubmissions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CardImages");
        }
    }
}
