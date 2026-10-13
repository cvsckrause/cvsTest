CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'erp') THEN
            CREATE SCHEMA erp;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.articles (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Sku" character varying(80) NOT NULL,
        "Name" character varying(240) NOT NULL,
        "SalesPrice" numeric(18,4) NOT NULL,
        CONSTRAINT "PK_articles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.audit_entries (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "UserId" character varying(200) NOT NULL,
        "Action" character varying(100) NOT NULL,
        "EntityType" character varying(100) NOT NULL,
        "EntityId" uuid NOT NULL,
        "Data" text,
        "CorrelationId" character varying(100) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_audit_entries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.customers (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Number" character varying(40) NOT NULL,
        "Name" character varying(240) NOT NULL,
        CONSTRAINT "PK_customers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.deliveries (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SalesOrderId" uuid NOT NULL,
        "Number" character varying(40) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_deliveries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.idempotency_records (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Key" character varying(200) NOT NULL,
        "Operation" character varying(100) NOT NULL,
        "ResourceId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_idempotency_records" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.inbox_messages (
        "MessageId" uuid NOT NULL,
        "ProcessedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_inbox_messages" PRIMARY KEY ("MessageId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.invoices (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SalesOrderId" uuid NOT NULL,
        "Number" character varying(40) NOT NULL,
        "NetAmount" numeric(18,4) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_invoices" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.outbox_messages (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "EventType" character varying(200) NOT NULL,
        "Payload" text NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        "ProcessedAt" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "LastError" text,
        CONSTRAINT "PK_outbox_messages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.sales_orders (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "OrderNumber" character varying(40) NOT NULL,
        "Status" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ReleasedAt" timestamp with time zone,
        CONSTRAINT "PK_sales_orders" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.stock_balances (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ArticleId" uuid NOT NULL,
        "WarehouseCode" character varying(40) NOT NULL,
        "OnHand" numeric(18,4) NOT NULL,
        "Reserved" numeric(18,4) NOT NULL,
        CONSTRAINT "PK_stock_balances" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE TABLE erp.sales_order_lines (
        "Id" uuid NOT NULL,
        "SalesOrderId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ArticleId" uuid NOT NULL,
        "Sku" character varying(80) NOT NULL,
        "Quantity" numeric(18,4) NOT NULL,
        "UnitPrice" numeric(18,4) NOT NULL,
        CONSTRAINT "PK_sales_order_lines" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_sales_order_lines_sales_orders_SalesOrderId" FOREIGN KEY ("SalesOrderId") REFERENCES erp.sales_orders ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_articles_TenantId_Sku" ON erp.articles ("TenantId", "Sku");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE INDEX "IX_audit_entries_TenantId_CreatedAt" ON erp.audit_entries ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_customers_TenantId_Number" ON erp.customers ("TenantId", "Number");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_deliveries_TenantId_Number" ON erp.deliveries ("TenantId", "Number");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_idempotency_records_TenantId_Operation_Key" ON erp.idempotency_records ("TenantId", "Operation", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_invoices_TenantId_Number" ON erp.invoices ("TenantId", "Number");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE INDEX "IX_outbox_messages_ProcessedAt_OccurredAt" ON erp.outbox_messages ("ProcessedAt", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE INDEX "IX_sales_order_lines_SalesOrderId" ON erp.sales_order_lines ("SalesOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE INDEX "IX_sales_order_lines_TenantId_SalesOrderId" ON erp.sales_order_lines ("TenantId", "SalesOrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_sales_orders_TenantId_OrderNumber" ON erp.sales_orders ("TenantId", "OrderNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    CREATE UNIQUE INDEX "IX_stock_balances_TenantId_ArticleId_WarehouseCode" ON erp.stock_balances ("TenantId", "ArticleId", "WarehouseCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."articles" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."articles" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."articles"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."audit_entries" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."audit_entries" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."audit_entries"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."customers" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."customers" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."customers"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."deliveries" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."deliveries" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."deliveries"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."idempotency_records" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."idempotency_records" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."idempotency_records"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."invoices" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."invoices" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."invoices"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."outbox_messages" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."outbox_messages" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."outbox_messages"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."sales_order_lines" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."sales_order_lines" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."sales_order_lines"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."sales_orders" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."sales_orders" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."sales_orders"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    ALTER TABLE erp."stock_balances" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE erp."stock_balances" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON erp."stock_balances"
    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013130016_InitialTradingSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261013130016_InitialTradingSchema', '10.0.4');
    END IF;
END $EF$;
COMMIT;

