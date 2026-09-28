using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sonvert.App.Migrations
{
    /// <inheritdoc />
    public partial class AddGlossaryDictionaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlossaryDictionaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<System.DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryDictionaries", x => x.Id);
                });

            // 新列先允许为空插入默认值 0，稍后用下面的数据迁移语句把已有
            // 词条指向一个新建的"默认词典"，再没有机会补齐的（比如这个
            // 数据库压根没有任何词条）不会留下悬空引用——因为压根不会有
            // DictionaryId=0 却真实存在的词条行需要处理。
            migrationBuilder.AddColumn<int>(
                name: "DictionaryId",
                table: "GlossaryEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // 数据迁移：这次改动之前已经存在的术语条目（老用户升级过来的）
            // 不能因为加了这个必填的外键就凭空消失或者在查询时因为找不到
            // 对应词典而被过滤掉——只有在真的存在旧词条时才建这个"默认
            // 词典"，全新安装、从来没加过术语的用户不会平白多出一个空词典。
            migrationBuilder.Sql(
                "INSERT INTO GlossaryDictionaries (Name, IsEnabled, CreatedAt) " +
                "SELECT '默认词典', 1, datetime('now') " +
                "WHERE EXISTS (SELECT 1 FROM GlossaryEntries);");

            migrationBuilder.Sql(
                "UPDATE GlossaryEntries SET DictionaryId = " +
                "(SELECT Id FROM GlossaryDictionaries WHERE Name = '默认词典' ORDER BY Id DESC LIMIT 1) " +
                "WHERE EXISTS (SELECT 1 FROM GlossaryDictionaries WHERE Name = '默认词典');");

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryEntries_DictionaryId",
                table: "GlossaryEntries",
                column: "DictionaryId");

            migrationBuilder.AddForeignKey(
                name: "FK_GlossaryEntries_GlossaryDictionaries_DictionaryId",
                table: "GlossaryEntries",
                column: "DictionaryId",
                principalTable: "GlossaryDictionaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GlossaryEntries_GlossaryDictionaries_DictionaryId",
                table: "GlossaryEntries");

            migrationBuilder.DropIndex(
                name: "IX_GlossaryEntries_DictionaryId",
                table: "GlossaryEntries");

            migrationBuilder.DropColumn(
                name: "DictionaryId",
                table: "GlossaryEntries");

            migrationBuilder.DropTable(
                name: "GlossaryDictionaries");
        }
    }
}