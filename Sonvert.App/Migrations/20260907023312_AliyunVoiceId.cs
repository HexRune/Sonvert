using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonvert.App.Migrations
{
    /// <inheritdoc />
    public partial class AliyunVoiceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AliyunVoiceId",
                table: "Characters",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AliyunVoiceId",
                table: "Characters");
        }
    }
}
