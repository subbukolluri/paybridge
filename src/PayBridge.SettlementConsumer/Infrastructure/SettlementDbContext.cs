using Microsoft.EntityFrameworkCore;

namespace PayBridge.SettlementConsumer.Infrastructure;

public class SettlementDbContext : DbContext
{
    public SettlementDbContext(DbContextOptions<SettlementDbContext> options)
        : base(options) { }

    public DbSet<SettlementRecord> SettlementRecords => Set<SettlementRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettlementRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();

            entity.Property(e => e.EventId).IsRequired();
            entity.Property(e => e.PaymentId).IsRequired();
            entity.Property(e => e.MerchantId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TenantId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(200);
            entity.Property(e => e.FailureReason).HasMaxLength(500);

            // Deduplicate at-least-once events
            entity.HasIndex(e => e.EventId)
                  .IsUnique()
                  .HasDatabaseName("UX_Settlement_EventId");
        });
    }
}

public class SettlementRecord
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public Guid PaymentId { get; set; }
    public string MerchantId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public int FinalStatus { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime EventTimestamp { get; set; }
    public DateTime PersistedAt { get; set; }
}
