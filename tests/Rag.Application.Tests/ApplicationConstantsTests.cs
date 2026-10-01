using Rag.Application;
using Xunit;

namespace Rag.Application.Tests;

public class ApplicationConstantsTests
{
    [Fact]
    public void ProjectName_Should_NotBeEmpty()
    {
        // Arrange & Act
        var projectName = ApplicationConstants.ProjectName;

        // Assert
        Assert.Equal("Rag.Application", projectName);
    }
}
