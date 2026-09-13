using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class BadgeBatchOptions
{
    public const string SectionName = "Documents:BadgeBatch";

    public int MaxBirdCount { get; set; } = 50;
}

public sealed class BadgeBatchOptionsValidator : IValidateOptions<BadgeBatchOptions>
{
    public ValidateOptionsResult Validate(string? name, BadgeBatchOptions options) =>
        options.MaxBirdCount is > 0 and <= 500
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{BadgeBatchOptions.SectionName}:MaxBirdCount must be between 1 and 500.");
}
