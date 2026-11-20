using FixHub.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixHub.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120100000_AddPerformerProfileFields")]
    public partial class AddPerformerProfileFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Specialization", table: "Users",
                type: "character varying(100)", maxLength: 100, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExperienceYears", table: "Users",
                type: "integer", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City", table: "Users",
                type: "character varying(100)", maxLength: 100, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceArea", table: "Users",
                type: "character varying(200)", maxLength: 200, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl", table: "Users",
                type: "character varying(300)", maxLength: 300, nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified", table: "Users",
                type: "boolean", nullable: false, defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Specialization", table: "Users");
            migrationBuilder.DropColumn(name: "ExperienceYears", table: "Users");
            migrationBuilder.DropColumn(name: "City", table: "Users");
            migrationBuilder.DropColumn(name: "ServiceArea", table: "Users");
            migrationBuilder.DropColumn(name: "AvatarUrl", table: "Users");
            migrationBuilder.DropColumn(name: "IsVerified", table: "Users");
        }
    }
}
