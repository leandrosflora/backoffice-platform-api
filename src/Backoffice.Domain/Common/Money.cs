namespace Backoffice.Domain.Common;

public sealed record Money(string Currency, decimal Amount)
{
    public static Money Zero(string currency) => new(currency, 0m);
}
