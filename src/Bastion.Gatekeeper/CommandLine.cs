namespace Bastion.Gatekeeper;

/// <summary>
/// Parses the command line the IFEO loader hands us. The layout is:
///   "&lt;gatekeeper.exe&gt;" "&lt;full path to real target&gt;" &lt;original args...&gt;
/// We extract the target path (token 1) and preserve the remainder verbatim as
/// the arguments so quoting/spacing survive.
/// </summary>
public static class CommandLine
{
    public static (string target, string arguments) Parse(string raw)
    {
        int i = 0;
        SkipToken(raw, ref i);                 // our own exe
        var target = ReadToken(raw, ref i);
        SkipWhitespace(raw, ref i);
        var arguments = i < raw.Length ? raw[i..].Trim() : "";
        return (target, arguments);
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static void SkipToken(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        _ = ReadToken(s, ref i);
    }

    private static string ReadToken(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length) return "";
        if (s[i] == '"')
        {
            i++; // opening quote
            int start = i;
            while (i < s.Length && s[i] != '"') i++;
            var tok = s[start..i];
            if (i < s.Length) i++; // closing quote
            return tok;
        }
        else
        {
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            return s[start..i];
        }
    }
}
