namespace Swgoh.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
