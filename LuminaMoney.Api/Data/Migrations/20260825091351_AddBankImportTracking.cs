using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaMoney.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankImportTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalSourceId",
                table: "Transactions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Transactions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportedAccountCount",
                table: "BankConnections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImportedTransactionCount",
                table: "BankConnections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSourceId",
                table: "Accounts",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Accounts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_Source_ExternalSourceId",
                table: "Transactions",
                columns: new[] { "UserId", "Source", "ExternalSourceId" },
                unique: true,
                filter: "[Source] IS NOT NULL AND [ExternalSourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId_Source_ExternalSourceId",
                table: "Accounts",
                columns: new[] { "UserId", "Source", "ExternalSourceId" },
                unique: true,
                filter: "[Source] IS NOT NULL AND [ExternalSourceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_Source_ExternalSourceId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserId_Source_ExternalSourceId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ExternalSourceId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ImportedAccountCount",
                table: "BankConnections");

            migrationBuilder.DropColumn(
                name: "ImportedTransactionCount",
                table: "BankConnections");

            migrationBuilder.DropColumn(
                name: "ExternalSourceId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Accounts");
        }
    }
}
