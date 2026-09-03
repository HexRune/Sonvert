using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonvert.App.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageToCharacterEmotionClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "CharacterEmotionClips",
                type: "TEXT",
                nullable: false,
                defaultValue: "zh");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "CharacterEmotionClips");
        }
    }
}
