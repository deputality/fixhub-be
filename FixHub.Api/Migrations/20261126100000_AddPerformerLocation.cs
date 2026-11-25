using FixHub.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixHub.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261126100000_AddPerformerLocation")]
    public partial class AddPerformerLocation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude", table: "Users",
                type: "double precision", nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude", table: "Users",
                type: "double precision", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Latitude", table: "Users");
            migrationBuilder.DropColumn(name: "Longitude", table: "Users");
        }
    }
}
