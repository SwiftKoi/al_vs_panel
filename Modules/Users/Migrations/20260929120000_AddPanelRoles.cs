using AlegacyWebPanel.Modules.Users.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlegacyWebPanel.Users.Migrations
{
    /// <summary>
    /// Data-only migration: creates the Admin and Moderator roles and makes every
    /// account that existed before roles were introduced an Admin, which is what
    /// those accounts could already do.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260929120000_AddPanelRoles")]
    public partial class AddPanelRoles : Migration
    {
        private const string AdminRoleId = "5f0b6c1e-2d7a-4c1b-9a61-3f1e0c7d2a01";
        private const string ModeratorRoleId = "5f0b6c1e-2d7a-4c1b-9a61-3f1e0c7d2a02";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT OR IGNORE INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                VALUES ('{AdminRoleId}', 'Admin', 'ADMIN', '{AdminRoleId}'),
                       ('{ModeratorRoleId}', 'Moderator', 'MODERATOR', '{ModeratorRoleId}');
                """);

            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO AspNetUserRoles (UserId, RoleId)
                SELECT Id, (SELECT Id FROM AspNetRoles WHERE NormalizedName = 'ADMIN') FROM AspNetUsers;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM AspNetUserRoles WHERE RoleId IN ('{AdminRoleId}', '{ModeratorRoleId}');");
            migrationBuilder.Sql($"DELETE FROM AspNetRoles WHERE Id IN ('{AdminRoleId}', '{ModeratorRoleId}');");
        }
    }
}
