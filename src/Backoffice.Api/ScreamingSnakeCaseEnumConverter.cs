using System.Text;
using System.Text.Json;

namespace Backoffice.Api;

/// <summary>
/// Maps PascalCase enum members (e.g. CardPurchase) to the SCREAMING_SNAKE_CASE
/// values used throughout contracts/schemas/canonical-models-base.yaml (e.g. CARD_PURCHASE).
/// </summary>
public sealed class ScreamingSnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static readonly ScreamingSnakeCaseNamingPolicy Instance = new();

    public override string ConvertName(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('_');
            }
            builder.Append(char.ToUpperInvariant(c));
        }
        return builder.ToString();
    }
}
