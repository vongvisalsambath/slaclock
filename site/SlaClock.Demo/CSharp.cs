using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SlaClock.Demo;

/// <summary>A tiny C# highlighter for the generated code (same token classes as js/site.js uses for the static examples).</summary>
internal static partial class CSharp
{
    [GeneratedRegex("""(?<c>//[^\n]*)|(?<s>"(?:\\.|[^"\\])*")|(?<k>\b(?:var|new|using|await|async|public|private|static|class|record|return|if|else|foreach|in|true|false|null|void|is)\b)|(?<t>\b(?:BusinessCalendar|SlaTimer|SlaStatus|SlaState|SlaEvent|SlaEventKind|TimeSpan|DateOnly|DateTimeOffset|DayOfWeek|TimeRange|Task)\b)|(?<n>\b\d+(?:\.\d+)?\b)|(?<m>(?<=\.)[A-Z][A-Za-z0-9]*(?=\())""")]
    private static partial Regex Tokens();

    public static string Highlight(string code)
    {
        var sb = new StringBuilder(code.Length * 2);
        var last = 0;
        foreach (Match m in Tokens().Matches(code))
        {
            sb.Append(WebUtility.HtmlEncode(code[last..m.Index]));
            var cls = m.Groups["c"].Success ? "c" : m.Groups["s"].Success ? "s" : m.Groups["k"].Success ? "k" : m.Groups["t"].Success ? "t" : m.Groups["n"].Success ? "n" : "m";
            sb.Append("<span class=\"tok-").Append(cls).Append("\">").Append(WebUtility.HtmlEncode(m.Value)).Append("</span>");
            last = m.Index + m.Length;
        }
        sb.Append(WebUtility.HtmlEncode(code[last..]));
        return sb.ToString();
    }
}
