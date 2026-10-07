using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaMoney.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSplitCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransactionSplits_TransactionId",
                table: "TransactionSplits",
                column: "TransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionSplits_Transactions_TransactionId",
                table: "TransactionSplits",
                column: "TransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionSplits_Transactions_TransactionId",
                table: "TransactionSplits");

            migrationBuilder.DropIndex(
                name: "IX_TransactionSplits_TransactionId",
                table: "TransactionSplits");
        }
    }
}
