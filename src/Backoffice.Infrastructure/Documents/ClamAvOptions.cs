namespace Backoffice.Infrastructure.Documents;

public sealed class ClamAvOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 3310;
    public int TimeoutSeconds { get; init; } = 30;
}
