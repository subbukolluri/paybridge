using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PayBridge.Contracts.Enums;
using PayBridge.PaymentApi.Domain;
using PayBridge.PaymentApi.Infrastructure;

namespace PayBridge.Tests.Integration;

/// <summary>
/// Verifies that EF Core can talk to the real SQL Server running in Docker.
/// Prerequisites: docker-compose up sqlserver -d
/// </summary>
[Collection("Database")]
public class DbIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1434;Database=PayBridge_Test;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True";

    private PayBridgeDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<PayBridgeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _db = new PayBridgeDbContext(options);
        await _db.Database.EnsureDeletedAsync();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_ShouldApplySuccessfully()
    {
        var pendingMigrations = await _db.Database.GetPendingMigrationsAsync();
        pendingMigrations.Should().BeEmpty("all migrations should have been applied in setup");
    }

    [Fact]
    public async Task Repository_CreateAndRetrieve_ShouldRoundTrip()
    {
        var repo = new PaymentRepository(_db);
        var payment = CreatePayment();

        await repo.CreateAsync(payment);

        var retrieved = await repo.GetByIdAsync(payment.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(payment.Id);
        retrieved.MerchantId.Should().Be(payment.MerchantId);
        retrieved.Amount.Should().Be(payment.Amount);
        retrieved.Currency.Should().Be(payment.Currency);
        retrieved.Status.Should().Be(PaymentStatus.Created);
    }

    [Fact]
    public async Task Repository_GetByIdempotencyKey_ShouldFindPayment()
    {
        var repo = new PaymentRepository(_db);
        var payment = CreatePayment(idempotencyKey: "idem-key-123");

        await repo.CreateAsync(payment);

        var found = await repo.GetByIdempotencyKeyAsync(payment.MerchantId, "idem-key-123");
        found.Should().NotBeNull();
        found!.Id.Should().Be(payment.Id);
    }

    [Fact]
    public async Task Repository_DuplicateIdempotencyKey_ShouldThrowDbUpdateException()
    {
        var repo = new PaymentRepository(_db);
        var payment1 = CreatePayment(idempotencyKey: "dup-key");
        var payment2 = CreatePayment(idempotencyKey: "dup-key");

        await repo.CreateAsync(payment1);

        var act = () => repo.CreateAsync(payment2);
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Repository_Update_ShouldPersistChanges()
    {
        var repo = new PaymentRepository(_db);
        var payment = CreatePayment();
        await repo.CreateAsync(payment);

        payment.TransitionTo(PaymentStatus.FraudChecking);
        await repo.UpdateAsync(payment);

        var updated = await repo.GetByIdAsync(payment.Id);
        updated!.Status.Should().Be(PaymentStatus.FraudChecking);
    }

    [Fact]
    public async Task Repository_GetById_NonExistent_ShouldReturnNull()
    {
        var repo = new PaymentRepository(_db);

        var result = await repo.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DecimalPrecision_ShouldPreserve()
    {
        var repo = new PaymentRepository(_db);
        var payment = CreatePayment();
        payment.Amount = 1234.56m;

        await repo.CreateAsync(payment);

        var retrieved = await repo.GetByIdAsync(payment.Id);
        retrieved!.Amount.Should().Be(1234.56m);
    }

    private static Payment CreatePayment(string idempotencyKey = null!)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            MerchantId = "merchant1",
            TenantId = "merchant1",
            IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString(),
            Amount = 99.99m,
            Currency = "USD",
            Method = PaymentMethod.CreditCard,
            Status = PaymentStatus.Created,
            CreatedAt = DateTime.UtcNow
        };
    }
}
