using System.Text;

namespace Backoffice.Application.Policy;

/// <summary>
/// Converts PascalCase C# enum names (e.g. <c>CaseState.DocumentsReceived</c>) to the
/// SCREAMING_SNAKE_CASE strings policies/authorization.rego actually compares against
/// (e.g. <c>"DOCUMENTS_RECEIVED"</c>, per contracts/schemas/canonical-models-base.yaml).
/// Plain <c>enum.ToString()</c> silently mismatches every state-gated rule, since Rego's
/// `in {...}` set membership is an exact, case-sensitive string comparison.
/// </summary>
public static class PolicyWireFormat
{
    public static string ToWireString(this Enum value)
    {
        var name = value.ToString();
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

    /// <summary>
    /// Reverse of <see cref="ToWireString"/> — needed wherever an enum arrives as a raw
    /// SCREAMING_SNAKE_CASE string outside System.Text.Json's own converter pipeline (which
    /// already handles this both ways for JSON bodies), e.g. multipart form fields, which
    /// ASP.NET Core's default model binder would otherwise try to match with a plain
    /// case-insensitive `Enum.Parse` — that only works for single-word values, not
    /// multi-word ones like `TRANSACTION_PROOF` vs `TransactionProof`.
    /// </summary>
    public static TEnum FromWireString<TEnum>(string wireValue) where TEnum : struct, Enum
    {
        var pascalCase = string.Concat(wireValue.Split('_').Select(part =>
            part.Length == 0 ? "" : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        return Enum.Parse<TEnum>(pascalCase);
    }
}
