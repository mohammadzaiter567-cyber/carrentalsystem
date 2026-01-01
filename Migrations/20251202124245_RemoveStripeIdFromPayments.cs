using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace projectadvanced.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStripeIdFromPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeId",
                table: "Payments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeId",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}

