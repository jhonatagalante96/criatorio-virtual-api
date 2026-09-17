namespace CriatorioVirtual.Infrastructure.Storage;

internal static class PrivateObjectStorageKeyValidation
{
    public static void ValidateTenant(Guid breedingFarmId)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("A breeding farm is required for private storage.", nameof(breedingFarmId));
        }
    }

    public static void ValidateObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            objectKey.Contains('\0') ||
            objectKey.Contains('\\') ||
            Path.IsPathRooted(objectKey) ||
            objectKey.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("The object key must be a safe relative path.", nameof(objectKey));
        }

        var segments = objectKey.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("The object key must not contain path traversal segments.", nameof(objectKey));
        }
    }

    public static string ToTenantKey(Guid breedingFarmId, string objectKey)
    {
        ValidateTenant(breedingFarmId);
        ValidateObjectKey(objectKey);
        return $"{breedingFarmId:N}/{objectKey}";
    }
}
