using Backoffice.Application.Abstractions;

namespace Backoffice.Workers.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
