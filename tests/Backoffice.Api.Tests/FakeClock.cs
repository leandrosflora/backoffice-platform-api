using Backoffice.Application.Abstractions;

namespace Backoffice.Api.Tests;

/// <summary>Controllable clock for tests that need to simulate elapsed time (e.g. approval expiry).</summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
