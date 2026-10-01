using Xunit;

namespace Rag.IntegrationTests;

public class HealthEndpointTests
{
    [Fact]
    public void Assembly_Should_Discover()
    {
        // Arrange & Act
        var assemblyName = typeof(HealthEndpointTests).Assembly.GetName().Name;

        // Assert
        Assert.Equal("Rag.IntegrationTests", assemblyName);
    }
}
