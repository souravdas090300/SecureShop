using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixLegacyPaymentIntentClientSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Early versions stored the Stripe client secret ("pi_..._secret_...")
            // in StripePaymentIntentId instead of the bare intent ID. Keep only
            // the part before "_secret_" so webhooks and the abandonment sweep
            // can match the order to its PaymentIntent.
            migrationBuilder.Sql("""
                UPDATE "Orders"
                SET "StripePaymentIntentId" = split_part("StripePaymentIntentId", '_secret_', 1)
                WHERE position('_secret_' in "StripePaymentIntentId") > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: the stripped client-secret suffix cannot be recovered.
        }
    }
}
