using Microsoft.EntityFrameworkCore;
using PayBridge.PaymentApi.Domain;

namespace PayBridge.PaymentApi.Infrastructure;

public class PayBridgeDbContext : DbContext
{
    public PayBridgeDbContext(DbContextOptions<PayBridgeDbContext> options)
        : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.Property(e => e.MerchantId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(200);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.Property(e => e.TraceParent).HasMaxLength(200);

            entity.HasIndex(e => new { e.MerchantId, e.IdempotencyKey })
                  .IsUnique()
                  .HasDatabaseName("UX_Payment_Idempotency");
        });
    }
}
