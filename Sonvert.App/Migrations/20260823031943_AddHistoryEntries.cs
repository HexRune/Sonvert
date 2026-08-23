using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonvert.App.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceText = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedText = table.Column<string>(type: "TEXT", nullable: true),
                    Emotion = table.Column<string>(type: "TEXT", nullable: true),
                    Event = table.Column<string>(type: "TEXT", nullable: true),
                    CharacterId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    SourceAudioRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedAudioRelativePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_Timestamp",
                table: "HistoryEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoryEntries");
        }
    }
}
