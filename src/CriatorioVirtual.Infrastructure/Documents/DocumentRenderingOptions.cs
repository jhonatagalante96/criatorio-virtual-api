namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class DocumentRenderingOptions
{
    public const string SectionName = "DocumentRendering";

    public const int MaximumConcurrentRenders = 4;

    public int MaxConcurrentRenders { get; set; } = 2;

    public int RenderTimeoutSeconds { get; set; } = 30;
}
