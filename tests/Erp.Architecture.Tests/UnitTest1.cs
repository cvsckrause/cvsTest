using System.Reflection;

namespace Erp.Architecture.Tests;

public sealed class ModuleBoundaryTests
{
    public static TheoryData<Assembly> Modules =>
        new(
            typeof(Erp.Platform.PlatformModule).Assembly,
            typeof(Erp.MasterData.MasterDataModule).Assembly,
            typeof(Erp.Inventory.InventoryModule).Assembly,
            typeof(Erp.Sales.SalesModule).Assembly,
            typeof(Erp.Billing.BillingModule).Assembly);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Modules_do_not_reference_each_other(Assembly module)
    {
        var moduleReferences = module
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("Erp.", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(moduleReferences, name => name != "Erp.BuildingBlocks");
    }
}
