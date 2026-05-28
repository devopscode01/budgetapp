using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class AddBillPaymentTransactionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkedTransactionId",
                table: "BillPayments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillPayments_LinkedTransactionId",
                table: "BillPayments",
                column: "LinkedTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_BillPayments_ParsedTransactions_LinkedTransactionId",
                table: "BillPayments",
                column: "LinkedTransactionId",
                principalTable: "ParsedTransactions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillPayments_ParsedTransactions_LinkedTransactionId",
                table: "BillPayments");

            migrationBuilder.DropIndex(
                name: "IX_BillPayments_LinkedTransactionId",
                table: "BillPayments");

            migrationBuilder.DropColumn(
                name: "LinkedTransactionId",
                table: "BillPayments");
        }
    }
}
