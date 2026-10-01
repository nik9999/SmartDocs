using Rag.Infrastructure;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class InfrastructureConstantsTests
{
    [Fact]
    public void ProjectName_Should_NotBeEmpty()
    {
        // Arrange & Act
        var projectName = InfrastructureConstants.ProjectName;

        // Assert
        Assert.Equal("Rag.Infrastructure", projectName);
    }
}
