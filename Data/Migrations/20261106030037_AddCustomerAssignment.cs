using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcxiomCRM.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "Customers",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AssignedTo",
                table: "Customers",
                column: "AssignedTo");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_AspNetUsers_AssignedTo",
                table: "Customers",
                column: "AssignedTo",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_AspNetUsers_AssignedTo",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_AssignedTo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "Customers");
        }
    }
}
