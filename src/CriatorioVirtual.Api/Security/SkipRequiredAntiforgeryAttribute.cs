namespace CriatorioVirtual.Api;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SkipRequiredAntiforgeryAttribute : Attribute
{
}
