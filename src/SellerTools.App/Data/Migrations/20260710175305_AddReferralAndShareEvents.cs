using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SellerTools.App.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralAndShareEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefCode",
                table: "Sellers",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReferredBySellerId",
                table: "Sellers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShareEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SellerId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sellers_RefCode",
                table: "Sellers",
                column: "RefCode",
                unique: true,
                filter: "\"RefCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sellers_ReferredBySellerId",
                table: "Sellers",
                column: "ReferredBySellerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShareEvents_CreatedAt",
                table: "ShareEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShareEvents_SellerId",
                table: "ShareEvents",
                column: "SellerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sellers_Sellers_ReferredBySellerId",
                table: "Sellers",
                column: "ReferredBySellerId",
                principalTable: "Sellers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sellers_Sellers_ReferredBySellerId",
                table: "Sellers");

            migrationBuilder.DropTable(
                name: "ShareEvents");

            migrationBuilder.DropIndex(
                name: "IX_Sellers_RefCode",
                table: "Sellers");

            migrationBuilder.DropIndex(
                name: "IX_Sellers_ReferredBySellerId",
                table: "Sellers");

            migrationBuilder.DropColumn(
                name: "RefCode",
                table: "Sellers");

            migrationBuilder.DropColumn(
                name: "ReferredBySellerId",
                table: "Sellers");
        }
    }
}
