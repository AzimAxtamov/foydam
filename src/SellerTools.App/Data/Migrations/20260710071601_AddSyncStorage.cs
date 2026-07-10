using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SellerTools.App.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DateCreated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DateAccepted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalAccepted = table.Column<int>(type: "integer", nullable: false),
                    TotalToStock = table.Column<int>(type: "integer", nullable: false),
                    IsTerminal = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Returns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DateCreated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssembledDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceledDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalAmount = table.Column<int>(type: "integer", nullable: false),
                    TotalPackedAmount = table.Column<int>(type: "integer", nullable: false),
                    UseAmount = table.Column<bool>(type: "boolean", nullable: false),
                    IsTerminal = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Returns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShopSyncStates",
                columns: table => new
                {
                    ShopId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastFullSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDeltaSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCardsSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    Inactive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopSyncStates", x => x.ShopId);
                });

            migrationBuilder.CreateTable(
                name: "Skus",
                columns: table => new
                {
                    ShopId = table.Column<long>(type: "bigint", nullable: false),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    SkuTitle = table.Column<string>(type: "text", nullable: true),
                    SkuFullTitle = table.Column<string>(type: "text", nullable: true),
                    SkuKeyNorm = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    ProductTitle = table.Column<string>(type: "text", nullable: true),
                    QuantitySold = table.Column<int>(type: "integer", nullable: false),
                    QuantityReturned = table.Column<int>(type: "integer", nullable: false),
                    QuantityDefected = table.Column<int>(type: "integer", nullable: false),
                    QuantityActive = table.Column<int>(type: "integer", nullable: false),
                    Ikpu = table.Column<string>(type: "text", nullable: true),
                    Article = table.Column<string>(type: "text", nullable: true),
                    SellerItemCode = table.Column<string>(type: "text", nullable: true),
                    Barcode = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    PurchasePriceDirty = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skus", x => new { x.ShopId, x.SkuId });
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    SkuTitleRaw = table.Column<string>(type: "text", nullable: true),
                    SkuKeyNorm = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    QuantityToStock = table.Column<int>(type: "integer", nullable: false),
                    QuantityAccepted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnId = table.Column<long>(type: "bigint", nullable: false),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    SkuTitleRaw = table.Column<string>(type: "text", nullable: true),
                    SkuKeyNorm = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    PackedAmount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnItems_Returns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "Returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InvoiceId",
                table: "InvoiceItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_SkuKeyNorm",
                table: "InvoiceItems",
                column: "SkuKeyNorm");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ShopId",
                table: "Invoices",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ShopId_DateCreated",
                table: "Invoices",
                columns: new[] { "ShopId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ShopId_IsTerminal",
                table: "Invoices",
                columns: new[] { "ShopId", "IsTerminal" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_ReturnId",
                table: "ReturnItems",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_SkuKeyNorm",
                table: "ReturnItems",
                column: "SkuKeyNorm");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_ShopId",
                table: "Returns",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_ShopId_DateCreated",
                table: "Returns",
                columns: new[] { "ShopId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_Returns_ShopId_IsTerminal",
                table: "Returns",
                columns: new[] { "ShopId", "IsTerminal" });

            migrationBuilder.CreateIndex(
                name: "IX_Skus_ShopId_SkuKeyNorm",
                table: "Skus",
                columns: new[] { "ShopId", "SkuKeyNorm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceItems");

            migrationBuilder.DropTable(
                name: "ReturnItems");

            migrationBuilder.DropTable(
                name: "ShopSyncStates");

            migrationBuilder.DropTable(
                name: "Skus");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Returns");
        }
    }
}
