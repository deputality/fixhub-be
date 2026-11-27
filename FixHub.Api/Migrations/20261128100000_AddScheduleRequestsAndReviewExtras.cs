using System;
using FixHub.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FixHub.Api.Migrations
{
    // Расписание и слоты, ответы и фото в отзывах, заявки с предложениями цены,
    // причина отмены заказа, дополнительные фото портфолио
    [DbContext(typeof(AppDbContext))]
    [Migration("20261128100000_AddScheduleRequestsAndReviewExtras")]
    public partial class AddScheduleRequestsAndReviewExtras : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DesiredTime", table: "OrderItems", type: "time without time zone", nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DesiredTime", table: "CartItems", type: "time without time zone", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason", table: "OrderStatusHistory", type: "character varying(500)", maxLength: 500, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyText", table: "Reviews", type: "character varying(1000)", maxLength: 1000, nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt", table: "Reviews", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestId", table: "Notifications", type: "integer", nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_DesiredDate_DesiredTime",
                table: "OrderItems",
                columns: new[] { "DesiredDate", "DesiredTime" });

            migrationBuilder.CreateTable(
                name: "ReviewPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReviewId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewPhotos_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PortfolioItemId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioPhotos_PortfolioItems_PortfolioItemId",
                        column: x => x.PortfolioItemId,
                        principalTable: "PortfolioItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkingHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingHours", x => x.Id);
                    table.CheckConstraint("CK_WorkingHours_DayOfWeek", "\"DayOfWeek\" BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_WorkingHours_Users_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DesiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceRequests_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceRequests_Users_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceOffers", x => x.Id);
                    table.CheckConstraint("CK_PriceOffers_Price", "\"Price\" > 0");
                    table.ForeignKey(
                        name: "FK_PriceOffers_PriceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "PriceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceOffers_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceOffers_Users_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_ReviewPhotos_ReviewId", table: "ReviewPhotos", column: "ReviewId");
            migrationBuilder.CreateIndex(name: "IX_PortfolioPhotos_PortfolioItemId", table: "PortfolioPhotos", column: "PortfolioItemId");
            migrationBuilder.CreateIndex(
                name: "IX_WorkingHours_PerformerId_DayOfWeek", table: "WorkingHours",
                columns: new[] { "PerformerId", "DayOfWeek" }, unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_PriceRequests_ClientId_CreatedAt", table: "PriceRequests",
                columns: new[] { "ClientId", "CreatedAt" });
            migrationBuilder.CreateIndex(
                name: "IX_PriceRequests_Status_CategoryId", table: "PriceRequests",
                columns: new[] { "Status", "CategoryId" });
            migrationBuilder.CreateIndex(name: "IX_PriceRequests_CategoryId", table: "PriceRequests", column: "CategoryId");
            migrationBuilder.CreateIndex(
                name: "IX_PriceOffers_RequestId_PerformerId", table: "PriceOffers",
                columns: new[] { "RequestId", "PerformerId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_PriceOffers_PerformerId", table: "PriceOffers", column: "PerformerId");
            migrationBuilder.CreateIndex(name: "IX_PriceOffers_ServiceId", table: "PriceOffers", column: "ServiceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PriceOffers");
            migrationBuilder.DropTable(name: "PriceRequests");
            migrationBuilder.DropTable(name: "WorkingHours");
            migrationBuilder.DropTable(name: "PortfolioPhotos");
            migrationBuilder.DropTable(name: "ReviewPhotos");

            migrationBuilder.DropIndex(name: "IX_OrderItems_DesiredDate_DesiredTime", table: "OrderItems");

            migrationBuilder.DropColumn(name: "RequestId", table: "Notifications");
            migrationBuilder.DropColumn(name: "RepliedAt", table: "Reviews");
            migrationBuilder.DropColumn(name: "ReplyText", table: "Reviews");
            migrationBuilder.DropColumn(name: "Reason", table: "OrderStatusHistory");
            migrationBuilder.DropColumn(name: "DesiredTime", table: "CartItems");
            migrationBuilder.DropColumn(name: "DesiredTime", table: "OrderItems");
        }
    }
}
