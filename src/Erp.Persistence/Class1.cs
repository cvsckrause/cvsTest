using Erp.Billing.Domain;
using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Platform.Domain;
using Erp.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

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
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<NumberRange> NumberRanges => Set<NumberRange>();
    public DbSet<Mandant> Mandanten => Set<Mandant>();
    public DbSet<MandantUser> MandantUsers => Set<MandantUser>();

    // Unqualified, the history table follows search_path: erp_owner resolves it to public, where it
    // has no CREATE, while databases migrated as the erp master user keep it in erp.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "erp"));

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
            entity.Property(x => x.TaxCode).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TaxCode.Standard19);
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

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WarehouseCode).HasMaxLength(40);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.Reference).HasMaxLength(120);
            entity.Property(x => x.CreatedBy).HasMaxLength(200);
            entity.HasIndex(x => new { x.TenantId, x.OccurredAt });
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<SalesOrder>(entity =>
        {
            entity.ToTable("sales_orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderNumber).HasMaxLength(40);
            entity.Property(x => x.WarehouseCode).HasMaxLength(40).HasDefaultValue("MAIN");
            entity.Property(x => x.ShipToName).HasMaxLength(240);
            entity.Property(x => x.ShipToStreet).HasMaxLength(240);
            entity.Property(x => x.ShipToPostalCode).HasMaxLength(20);
            entity.Property(x => x.ShipToCity).HasMaxLength(120);
            entity.Property(x => x.ShipToCountryCode).HasMaxLength(2);
            entity.Property(x => x.ShipToAdditionalLine).HasMaxLength(240);
            entity.Property(x => x.BuyerName).HasMaxLength(240);
            entity.Property(x => x.BuyerNumber).HasMaxLength(40);
            entity.Property(x => x.BillToName).HasMaxLength(240);
            entity.Property(x => x.BillToStreet).HasMaxLength(240);
            entity.Property(x => x.BillToPostalCode).HasMaxLength(20);
            entity.Property(x => x.BillToCity).HasMaxLength(120);
            entity.Property(x => x.BillToCountryCode).HasMaxLength(2);
            entity.Property(x => x.BillToAdditionalLine).HasMaxLength(240);
            entity.Ignore(x => x.NetAmount);
            entity.Ignore(x => x.ShipToSnapshot);
            entity.Ignore(x => x.BillTo);
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
            entity.Property(x => x.Description).HasMaxLength(240);
            entity.Property(x => x.TaxCode).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TaxCode.Standard19);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Ignore(x => x.NetAmount);
            entity.HasIndex(x => new { x.TenantId, x.SalesOrderId, x.Position }).IsUnique();
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
            entity.Property(x => x.CorrelationId).HasMaxLength(100).HasDefaultValue(string.Empty);
            entity.Property(x => x.UserId).HasMaxLength(200).HasDefaultValue(string.Empty);
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
            entity.Property(x => x.Fingerprint).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.Operation, x.Key }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.ToTable("tenant_memberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(200);
            entity.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique();
            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<NumberRange>(entity =>
        {
            entity.ToTable("number_ranges");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DocumentType).HasMaxLength(40);
            entity.Property(x => x.Prefix).HasMaxLength(20);
            entity.HasIndex(x => new { x.TenantId, x.DocumentType, x.Year }).IsUnique();
            entity.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        modelBuilder.HasSequence<long>("mandant_number_seq").StartsAt(Mandant.FirstRegisteredNumber);

        // Mandanten are the tenant registry itself, so they are read before a tenant is known and
        // carry no row-level security, like tenant_memberships.
        modelBuilder.Entity<Mandant>(entity =>
        {
            entity.ToTable("mandanten");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasDefaultValueSql("nextval('erp.mandant_number_seq')");
            entity.Property(x => x.Firmenname).HasMaxLength(Mandant.FirmennameMaxLength);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ApprovalTokenHash).HasMaxLength(64);
            entity.Property(x => x.SellerStreet).HasMaxLength(Mandant.StreetMaxLength);
            entity.Property(x => x.SellerPostalCode).HasMaxLength(Mandant.PostalCodeMaxLength);
            entity.Property(x => x.SellerCity).HasMaxLength(Mandant.CityMaxLength);
            entity.Property(x => x.SellerCountryCode).HasMaxLength(2);
            entity.Property(x => x.SellerAdditionalLine).HasMaxLength(Mandant.StreetMaxLength);
            entity.Property(x => x.Steuernummer).HasMaxLength(Mandant.SteuernummerMaxLength);
            entity.Property(x => x.UstIdNr).HasMaxLength(Mandant.UstIdNrMaxLength);
            entity.Ignore(x => x.Label);
            entity.HasIndex(x => x.Number).IsUnique();
            entity.HasIndex(x => x.ApprovalTokenHash).IsUnique();
        });

        modelBuilder.Entity<MandantUser>(entity =>
        {
            entity.ToTable("mandant_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(64);
            entity.Property(x => x.Email).HasMaxLength(MandantUser.EmailMaxLength);
            entity.Property(x => x.CognitoSub).HasMaxLength(200);
            entity.HasIndex(x => new { x.MandantId, x.UserName }).IsUnique();
            entity.HasOne<Mandant>().WithMany().HasForeignKey(x => x.MandantId).OnDelete(DeleteBehavior.Cascade);
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
