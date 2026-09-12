using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Storage;

public sealed class PrivateStorageOptions
{
    public const string SectionName = "Storage";

    public string PrivateRootPath { get; set; } = string.Empty;
}

public sealed class PrivateStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<PrivateStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, PrivateStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PrivateRootPath))
        {
            return IsLocalEnvironment(environment)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Storage:PrivateRootPath is required outside Development and Testing environments.");
        }

        try
        {
            if (!Path.IsPathRooted(options.PrivateRootPath) ||
                string.IsNullOrWhiteSpace(Path.GetFullPath(options.PrivateRootPath)))
            {
                return ValidateOptionsResult.Fail("Storage:PrivateRootPath must be an absolute path.");
            }
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Storage:PrivateRootPath must be a valid absolute path.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsLocalEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");
}
