namespace Act.Core.Abstractions;

public sealed record PermissionDecision(bool Allowed, string? Reason = null)
{
    public static PermissionDecision Allow() => new(true);

    public static PermissionDecision Deny(string? reason = null) => new(false, reason);
}
