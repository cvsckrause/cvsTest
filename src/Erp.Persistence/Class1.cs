using Erp.Billing.Domain;
using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Platform.Domain;
using Erp.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Persistence;

public sealed class ErpDbContext(
    DbContextOptions<ErpDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("erp");

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(240);
            entity.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("addresses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(240);
            entity.Property(x => x.Street).HasMaxLength(240);
            entity.Property(x => x.PostalCode).HasMaxLength(20);
            entity.Property(x => x.City).HasMaxLength(120);
            entity.Property(x => x.CountryCode).HasMaxLength(2);
            entity.Property(x => x.AdditionalLine).HasMaxLength(240);
            entity.HasIndex(x => new { x.TenantId, x.Name });
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.ToTable("customer_addresses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            entity.HasIndex(x => new { x.TenantId, x.CustomerId, x.AddressId, x.Role }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CustomerId, x.Role });
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Article>(entity =>
        {
            entity.ToTable("articles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(240);
            entity.Property(x => x.SalesPrice).HasPrecision(18, 4);
            entity.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<StockBalance>(entity =>
        {
            entity.ToTable("stock_balances");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WarehouseCode).HasMaxLength(40);
            entity.Property(x => x.OnHand).HasPrecision(18, 4).IsConcurrencyToken();
            entity.Property(x => x.Reserved).HasPrecision(18, 4).IsConcurrencyToken();
            entity.Ignore(x => x.Available);
            entity.HasIndex(x => new { x.TenantId, x.ArticleId, x.WarehouseCode }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SalesOrder>(entity =>
        {
            entity.ToTable("sales_orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderNumber).HasMaxLength(40);
            entity.Property(x => x.ShipToName).HasMaxLength(240);
            entity.Property(x => x.ShipToStreet).HasMaxLength(240);
            entity.Property(x => x.ShipToPostalCode).HasMaxLength(20);
            entity.Property(x => x.ShipToCity).HasMaxLength(120);
            entity.Property(x => x.ShipToCountryCode).HasMaxLength(2);
            entity.Property(x => x.ShipToAdditionalLine).HasMaxLength(240);
            entity.Ignore(x => x.NetAmount);
            entity.Ignore(x => x.ShipToSnapshot);
            entity.HasIndex(x => new { x.TenantId, x.OrderNumber }).IsUnique();
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SalesOrderId);
            entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SalesOrderLine>(entity =>
        {
            entity.ToTable("sales_order_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(80);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.HasIndex(x => new { x.TenantId, x.SalesOrderId });
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.ToTable("deliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(40);
            entity.Property(x => x.ShipToName).HasMaxLength(240);
            entity.Property(x => x.ShipToStreet).HasMaxLength(240);
            entity.Property(x => x.ShipToPostalCode).HasMaxLength(20);
            entity.Property(x => x.ShipToCity).HasMaxLength(120);
            entity.Property(x => x.ShipToCountryCode).HasMaxLength(2);
            entity.Property(x => x.ShipToAdditionalLine).HasMaxLength(240);
            entity.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(40);
            entity.Property(x => x.NetAmount).HasPrecision(18, 4);
            entity.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(200);
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.EntityType).HasMaxLength(100);
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(200);
            entity.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(x => x.MessageId);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.Operation).HasMaxLength(100);
            entity.HasIndex(x => new { x.TenantId, x.Operation, x.Key }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });
    }
}

public sealed class DesignTimeErpDbContextFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Erp")
            ?? "Host=localhost;Port=5432;Database=erp_dev;Username=erp;Password=erp";
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ErpDbContext(options, new TenantContext(Guid.Empty, "migration"));
    }
}
