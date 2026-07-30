namespace Act.Core.Abstractions;

public interface IClock
{
    DateTimeOffset Now { get; }
}
