using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Couchmark.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalSubtitlePath = table.Column<string>(type: "TEXT", nullable: true),
                    FileAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: false),
                    PosterPath = table.Column<string>(type: "TEXT", nullable: true),
                    LocalPosterPath = table.Column<string>(type: "TEXT", nullable: true),
                    BackdropPath = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseYear = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    TmdbRating = table.Column<double>(type: "REAL", nullable: false),
                    ImdbRating = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastWatched = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TvShows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: false),
                    PosterPath = table.Column<string>(type: "TEXT", nullable: true),
                    LocalPosterPath = table.Column<string>(type: "TEXT", nullable: true),
                    BackdropPath = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    FirstAirYear = table.Column<int>(type: "INTEGER", nullable: false),
                    TmdbRating = table.Column<double>(type: "REAL", nullable: false),
                    ImdbRating = table.Column<string>(type: "TEXT", nullable: false),
                    TotalSeasonsOnTmdb = table.Column<int>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastWatched = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TvShows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TvShowId = table.Column<long>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: false),
                    AirYear = table.Column<int>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_TvShows_TvShowId",
                        column: x => x.TvShowId,
                        principalTable: "TvShows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalSubtitlePath = table.Column<string>(type: "TEXT", nullable: true),
                    FileAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: false),
                    AirDate = table.Column<string>(type: "TEXT", nullable: false),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntroTimestamps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<long>(type: "INTEGER", nullable: true),
                    IntroStartSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    IntroEndSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    OutroStartSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    OutroEndSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntroTimestamps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntroTimestamps_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IntroTimestamps_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WatchProgress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaItemId = table.Column<long>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<long>(type: "INTEGER", nullable: true),
                    PositionSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastWatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchProgress_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WatchProgress_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_FilePath",
                table: "Episodes",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeasonId",
                table: "Episodes",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_IntroTimestamps_EpisodeId",
                table: "IntroTimestamps",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_IntroTimestamps_MediaItemId",
                table: "IntroTimestamps",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_FilePath",
                table: "MediaItems",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_TvShowId_SeasonNumber",
                table: "Seasons",
                columns: new[] { "TvShowId", "SeasonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_TmdbId",
                table: "TvShows",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchProgress_EpisodeId",
                table: "WatchProgress",
                column: "EpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchProgress_LastWatchedAt",
                table: "WatchProgress",
                column: "LastWatchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WatchProgress_MediaItemId",
                table: "WatchProgress",
                column: "MediaItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntroTimestamps");

            migrationBuilder.DropTable(
                name: "WatchProgress");

            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "MediaItems");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "TvShows");
        }
    }
}
