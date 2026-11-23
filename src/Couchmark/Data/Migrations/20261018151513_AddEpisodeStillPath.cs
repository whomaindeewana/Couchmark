using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couchmark.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodeStillPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TvShows_TmdbId",
                table: "TvShows");

            migrationBuilder.AddColumn<string>(
                name: "StillPath",
                table: "Episodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchProgress_MediaItemId_EpisodeId",
                table: "WatchProgress",
                columns: new[] { "MediaItemId", "EpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_Title",
                table: "TvShows",
                column: "Title",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_TmdbId",
                table: "TvShows",
                column: "TmdbId",
                unique: true,
                filter: "\"TmdbId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchProgress_MediaItemId_EpisodeId",
                table: "WatchProgress");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_Title",
                table: "TvShows");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_TmdbId",
                table: "TvShows");

            migrationBuilder.DropColumn(
                name: "StillPath",
                table: "Episodes");

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_TmdbId",
                table: "TvShows",
                column: "TmdbId",
                unique: true);
        }
    }
}
