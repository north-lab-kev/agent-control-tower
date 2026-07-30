namespace Act.Core.Abstractions;

public sealed record LaunchConfigAdjustment(
    string Field,
    string? Requested,
    string? Substituted,
    string Reason);
