using CriatorioVirtual.Infrastructure.Persistence;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Persistence;

public sealed class RelationalNamesTests
{
    [Fact]
    public void Names_AreStableAndLowercase()
    {
        Assert.Equal("pk_flocks", RelationalNames.PrimaryKey("Flocks"));
        Assert.Equal("fk_birds_flocks_flock_id", RelationalNames.ForeignKey("Birds", "Flocks", "Flock_Id"));
        Assert.Equal("ix_birds_tenant_id_ring_number", RelationalNames.Index("Birds", "Tenant_Id", "Ring_Number"));
        Assert.Equal("ck_birds_ring_number_present", RelationalNames.Check("Birds", "Ring_Number_Present"));
    }

    [Fact]
    public void Index_RequiresAtLeastOneColumn()
    {
        Assert.Throws<ArgumentException>(() => RelationalNames.Index("birds"));
    }
}
