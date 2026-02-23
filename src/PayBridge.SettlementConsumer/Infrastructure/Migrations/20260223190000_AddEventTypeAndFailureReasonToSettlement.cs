using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayBridge.SettlementConsumer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventTypeAndFailureReasonToSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "SettlementRecords",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "PaymentCompleted");

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "SettlementRecords",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EventType", table: "SettlementRecords");
            migrationBuilder.DropColumn(name: "FailureReason", table: "SettlementRecords");
        }
    }
}
