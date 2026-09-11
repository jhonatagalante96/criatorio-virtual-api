namespace CriatorioVirtual.Domain.Birds;

public enum BirdEligibilityIssueCode
{
    MissingRingNumber,
    InactiveStatus
}

public sealed record BirdEligibility(IReadOnlyCollection<BirdEligibilityIssueCode> Issues)
{
    public bool IsEligible => Issues.Count == 0;

    public static BirdEligibility Evaluate(string? ringNumber, BirdStatus status)
    {
        var issues = new List<BirdEligibilityIssueCode>();

        if (string.IsNullOrWhiteSpace(ringNumber))
        {
            issues.Add(BirdEligibilityIssueCode.MissingRingNumber);
        }

        if (status != BirdStatus.Active)
        {
            issues.Add(BirdEligibilityIssueCode.InactiveStatus);
        }

        return new BirdEligibility(issues);
    }
}
