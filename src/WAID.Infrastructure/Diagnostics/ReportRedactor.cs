using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WAID.Infrastructure.Diagnostics;
public static class ReportRedactor
{
    private static readonly Regex SensitiveName = new("(?i)(password|token|secret|product.?key|cookie|serial|device.?id|authorization)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SensitiveValue = new("(?i)(password|token|secret|product.?key|authorization|cookie)\\s*[:=]\\s*[^\\s;,]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    public static bool IsSensitiveName(string name) => SensitiveName.IsMatch(name);
    public static void Redact(JsonNode node)
    {
        if (node is JsonObject obj) foreach (var property in obj.ToArray()) { if (IsSensitiveName(property.Key)) obj.Remove(property.Key); else if (property.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[property.Key] = RedactText(text); else if (property.Value is not null) Redact(property.Value); }
        else if (node is JsonArray array) for (var index = 0; index < array.Count; index++) { if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = RedactText(text); else if (array[index] is not null) Redact(array[index]!); }
    }
    public static string RedactText(string text) { var redacted = SensitiveValue.Replace(text, "$1=[REDACTED]"); var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); if(!string.IsNullOrWhiteSpace(profile))redacted=redacted.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);var user=Environment.UserName;return string.IsNullOrWhiteSpace(user)?redacted:redacted.Replace(user,"[USER]",StringComparison.OrdinalIgnoreCase); }
}
