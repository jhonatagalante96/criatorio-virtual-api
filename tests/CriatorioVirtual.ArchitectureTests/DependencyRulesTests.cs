using CriatorioVirtual.Application;
using CriatorioVirtual.Domain.Primitives;
using CriatorioVirtual.Infrastructure;
using Xunit;

namespace CriatorioVirtual.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_DoesNotReferenceEntityFrameworkOrMediatR()
    {
        var forbiddenReferences = new[] { "Microsoft.EntityFrameworkCore", "MediatR" };
        var references = typeof(Entity).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => forbiddenReferences.Contains(reference.Name));
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
    {
        var references = typeof(InfrastructureAssemblyMarker).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == "CriatorioVirtual.Api");
    }

    [Fact]
    public void Api_IsTheOutermostCompositionBoundary()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.Contains("CriatorioVirtual.Application", references);
        Assert.Contains("CriatorioVirtual.Infrastructure", references);
    }
}
