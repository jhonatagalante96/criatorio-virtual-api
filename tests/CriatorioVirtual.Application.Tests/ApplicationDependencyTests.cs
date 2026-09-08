using CriatorioVirtual.Application;
using Xunit;

namespace CriatorioVirtual.Application.Tests;

public sealed class ApplicationDependencyTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotDependOnInfrastructure()
    {
        var references = typeof(ApplicationAssemblyMarker).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == "CriatorioVirtual.Infrastructure");
    }
}
