using Diligencia.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;

namespace Diligencia.UnitTests.Configuration;

public class ConfigurationExtensionsTests
{
    [Fact]
    public void GetRequired_MissingKey_NamesKeyWithoutValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Other:Secret"] = "s3cr3t" })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => config.GetRequired("ConnectionStrings:diligencia"));

        Assert.Equal("Falta la configuración obligatoria 'ConnectionStrings:diligencia'.", ex.Message);
        Assert.DoesNotContain("s3cr3t", ex.Message);
    }

    [Fact]
    public void GetRequired_PresentKey_ReturnsValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["A"] = "v" }).Build();

        Assert.Equal("v", config.GetRequired("A"));
    }
}
