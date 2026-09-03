using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonvert.App.Migrations
{
    /// <inheritdoc />
    public partial class AddLatencyToHistoryEntries : Migration
    {
        /// <inheritdoc />
         protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 三段都是可空列，不给默认值——老数据这三列会是 NULL，
            // 语义上正确（这次改动之前根本没测过延迟，NULL 就是如实反映
            // "这条记录没有延迟数据"，不需要像 CharacterEmotionClips 加
            // Language 列那次一样给个兜底默认值）。
            migrationBuilder.AddColumn<int>(
                name: "AsrLatencyMs",
                table: "HistoryEntries",
                type: "INTEGER",
                nullable: true);
 
            migrationBuilder.AddColumn<int>(
                name: "TranslationLatencyMs",
                table: "HistoryEntries",
                type: "INTEGER",
                nullable: true);
 
            migrationBuilder.AddColumn<int>(
                name: "TtsLatencyMs",
                table: "HistoryEntries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
         protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AsrLatencyMs", table: "HistoryEntries");
            migrationBuilder.DropColumn(name: "TranslationLatencyMs", table: "HistoryEntries");
            migrationBuilder.DropColumn(name: "TtsLatencyMs", table: "HistoryEntries");
        }
    }
}
