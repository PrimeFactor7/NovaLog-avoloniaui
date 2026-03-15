using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Avalonia.ViewModels;

public static class SyntaxHighlighter
{
    private static readonly Regex StackMethodPattern = new(@"(?<atkw>at)\s+(?<method>[\w.+<>\[\]`,]+)\((?<args>[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex StackFilePattern = new(@"(?:(?<inkw>in)\s+)?(?<path>\w:[\\\/][^\s:]+|[\\\/][^\s:]+):(?:line\s+)?(?<line>\d+)", RegexOptions.Compiled);
    private static readonly Regex StackExceptionPattern = new(@"(?<extype>[\w.]+Exception)\b", RegexOptions.Compiled);
    private static readonly Regex HexPattern = new(@"\b0x[0-9a-fA-F]+\b", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"\b\d+\.?\d*(?:[eE][-+]?\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex GuidPattern = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IpPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"\b(?:https?|ftp)://[^\s]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SqlKeywordPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|CROSS|ON|AND|OR|NOT|IN|INTO|VALUES|SET|CREATE|DROP|ALTER|TABLE|INDEX|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|AS|DISTINCT|COUNT|SUM|AVG|MIN|MAX|BETWEEN|LIKE|IS|NULL|EXISTS|UNION|CASE|WHEN|THEN|ELSE|END|EXEC|EXECUTE|TOP|ASC|DESC)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SqlStringPattern = new(@"'(?:[^'\\]|\\.)*'", RegexOptions.Compiled);
    private static readonly Regex SqlOperatorPattern = new(@"[=<>!]+|[(),;*]", RegexOptions.Compiled);
    private static readonly Regex SqlNumberPattern = new(@"\b\d+\.?\d*\b", RegexOptions.Compiled);

    public static List<HighlightToken> Tokenize(string message, SyntaxFlavor flavor, bool isContinuation)
    {
        if (string.IsNullOrWhiteSpace(message)) return new List<HighlightToken>();

        return flavor switch
        {
            SyntaxFlavor.Json => TokenizeJson(message),
            SyntaxFlavor.Sql => TokenizeSql(message),
            SyntaxFlavor.StackTrace => TokenizeStackTrace(message),
            _ => TokenizePlain(message, isContinuation)
        };
    }

    private static List<HighlightToken> TokenizePlain(string message, bool isContinuation)
    {
        var matches = new List<HighlightToken>();
        
        foreach (Match m in GuidPattern.Matches(message))
            matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.Guid));

        foreach (Match m in UrlPattern.Matches(message))
            if (!Overlaps(matches, m.Index, m.Length))
                matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.Url));

        foreach (Match m in IpPattern.Matches(message))
            if (!Overlaps(matches, m.Index, m.Length))
                matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.IpAddress));

        foreach (Match m in HexPattern.Matches(message))
            if (!Overlaps(matches, m.Index, m.Length))
                matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.Hex));

        foreach (Match m in NumberPattern.Matches(message))
            if (!Overlaps(matches, m.Index, m.Length))
                matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.Number));

        return BuildFinalTokens(message, matches, isContinuation ? HighlightType.DimText : HighlightType.TextDefault);
    }

    private static List<HighlightToken> TokenizeStackTrace(string message)
    {
        var matches = new List<HighlightToken>();

        foreach (Match m in StackExceptionPattern.Matches(message))
            matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.StackException));

        foreach (Match m in StackMethodPattern.Matches(message))
        {
            if (m.Groups["atkw"].Success)
                matches.Add(new HighlightToken(m.Groups["atkw"].Index, m.Groups["atkw"].Length, HighlightType.StackKeyword));
            if (m.Groups["method"].Success)
                matches.Add(new HighlightToken(m.Groups["method"].Index, m.Groups["method"].Length, HighlightType.StackMethod));
            if (m.Groups["args"].Success && m.Groups["args"].Length > 0)
                matches.Add(new HighlightToken(m.Groups["args"].Index, m.Groups["args"].Length, HighlightType.StackArgs));
        }

        foreach (Match m in StackFilePattern.Matches(message))
        {
            if (m.Groups["inkw"].Success)
                matches.Add(new HighlightToken(m.Groups["inkw"].Index, m.Groups["inkw"].Length, HighlightType.StackKeyword));
            if (m.Groups["path"].Success)
                matches.Add(new HighlightToken(m.Groups["path"].Index, m.Groups["path"].Length, HighlightType.StackPath));
            if (m.Groups["line"].Success)
                matches.Add(new HighlightToken(m.Groups["line"].Index, m.Groups["line"].Length, HighlightType.StackLineNumber));
        }

        return BuildFinalTokens(message, matches, HighlightType.TextDefault);
    }

    private static List<HighlightToken> TokenizeJson(string message)
    {
        var raw = JsonHighlightTokenizer.Tokenize(message);
        var tokens = new List<HighlightToken>(raw.Count);
        foreach (var (start, len, kind) in raw)
        {
            var type = kind switch
            {
                JsonHighlightKind.Key => HighlightType.JsonKey,
                JsonHighlightKind.String => HighlightType.JsonString,
                JsonHighlightKind.Number => HighlightType.JsonNumber,
                JsonHighlightKind.Bool => HighlightType.JsonBool,
                JsonHighlightKind.Punctuation => HighlightType.JsonPunctuation, // Brace/Bracket handled by resolver usually, but we can refine here
                JsonHighlightKind.Prefix => HighlightType.DimText,
                _ => HighlightType.TextDefault
            };

            // Refine punctuation to Brace/Bracket
            if (type == HighlightType.JsonPunctuation && len == 1)
            {
                char c = message[start];
                if (c == '{' || c == '}') type = HighlightType.JsonBrace;
                else if (c == '[' || c == ']') type = HighlightType.JsonBracket;
            }

            tokens.Add(new HighlightToken(start, len, type));
        }
        return tokens;
    }

    private static List<HighlightToken> TokenizeSql(string message)
    {
        var matches = new List<HighlightToken>();

        foreach (Match m in SqlKeywordPattern.Matches(message))
            matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.SqlKeyword));
        foreach (Match m in SqlStringPattern.Matches(message))
            matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.SqlString));
        foreach (Match m in SqlOperatorPattern.Matches(message))
            if (!Overlaps(matches, m.Index, m.Length))
                matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.SqlOperator));
        foreach (Match m in SqlNumberPattern.Matches(message))
            if (!Overlaps(matches, m.Index, m.Length))
                matches.Add(new HighlightToken(m.Index, m.Length, HighlightType.SqlNumber));

        return BuildFinalTokens(message, matches, HighlightType.TextDefault);
    }

    private static List<HighlightToken> BuildFinalTokens(string message, List<HighlightToken> matches, HighlightType defaultType)
    {
        matches.Sort((a, b) => a.Index.CompareTo(b.Index));
        var tokens = new List<HighlightToken>();
        int lastPos = 0;

        foreach (var m in matches)
        {
            if (m.Index > lastPos)
                tokens.Add(new HighlightToken(lastPos, m.Index - lastPos, defaultType));
            
            // Handle potential overlaps if regexes are not perfect
            if (m.Index >= lastPos)
            {
                tokens.Add(m);
                lastPos = m.Index + m.Length;
            }
        }

        if (lastPos < message.Length)
            tokens.Add(new HighlightToken(lastPos, message.Length - lastPos, defaultType));

        return tokens;
    }

    private static bool Overlaps(List<HighlightToken> tokens, int index, int length)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (index < t.Index + t.Length && index + length > t.Index)
                return true;
        }
        return false;
    }
}
