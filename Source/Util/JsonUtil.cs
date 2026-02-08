using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace RimTalk.Util;

public static class JsonUtil
{
    public static string SerializeToJson<T>(T obj)
    {
        // Create a memory stream for serialization
        using var stream = new MemoryStream();
        // Create a DataContractJsonSerializer
        var serializer = new DataContractJsonSerializer(typeof(T));

        // Serialize the ApiRequest object
        serializer.WriteObject(stream, obj);

        // Convert the memory stream to a string
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static T DeserializeFromJson<T>(string json)
    {
        string sanitizedJson = Sanitize(json, typeof(T));
        
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedJson));
            // Create an instance of DataContractJsonSerializer
            var serializer = new DataContractJsonSerializer(typeof(T));

            // Deserialize the JSON data
            return (T)serializer.ReadObject(stream);
        }
        catch (Exception ex)
        {
            Logger.Error($"Json deserialization failed for {typeof(T).Name}\nException: {ex.GetType().Name} - {ex.Message}\nOriginal JSON: {json}\nSanitized JSON: {sanitizedJson}");
            throw;
        }
    }

    /// <summary>
    /// The definitive sanitizer that fixes structural, syntax, and formatting errors from LLM-generated JSON.
    /// </summary>
    /// <param name="text">The raw string from the LLM.</param>
    /// <param name="targetType">The C# type we are trying to deserialize into.</param>
    /// <returns>A cleaned and likely valid JSON string.</returns>
    public static string Sanitize(string text, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string sanitized = text.Replace("```json", "").Replace("```", "").Trim();

        int startIndex = sanitized.IndexOfAny(['{', '[']);
        int endIndex = sanitized.LastIndexOfAny(['}', ']']);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            sanitized = sanitized.Substring(startIndex, endIndex - startIndex + 1).Trim();
        }
        else
        {
            return string.Empty;
        }

        sanitized = Regex.Replace(
            sanitized, 
            @"""([^""]+)""\s*:\s*([,}])", 
            @"""$1"":null$2"
        );

        // Fix single quotes used instead of double quotes for strings
        // Match 'value' that appears after : and before , or }
        sanitized = Regex.Replace(
            sanitized,
            @":\s*'([^']*)'(\s*[,}\]])",
            @": ""$1""$2"
        );

        // Don't merge multiple objects - let JsonStreamParser handle them separately
        if (sanitized.Contains("]["))
        {
            sanitized = sanitized.Replace("][", ",");
        }
        
        // Check if target type is a collection before deciding whether to merge objects
        bool isEnumerable = typeof(IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string);
        
        // Handle }{ pattern with optional whitespace/newlines between objects
        sanitized = Regex.Replace(sanitized, @"\}(\s*)\{", "},$1{");
        
        // Check if we have multiple objects (indicated by },{ pattern with optional whitespace)
        bool hasMultipleObjects = Regex.IsMatch(sanitized, @"},\s*\{");
        
        // If target is not enumerable but we have multiple objects, extract only the first one
        if (!isEnumerable && hasMultipleObjects && sanitized.StartsWith("{"))
        {
            var multiMatch = Regex.Match(sanitized, @"},\s*\{");
            if (multiMatch.Success)
            {
                sanitized = sanitized.Substring(0, multiMatch.Index + 1); // +1 to include the closing }
            }
        }
    
        if (sanitized.StartsWith("{") && sanitized.EndsWith("}"))
        {
            string innerContent = sanitized.Substring(1, sanitized.Length - 2).Trim();
            if (innerContent.StartsWith("[") && innerContent.EndsWith("]"))
            {
                sanitized = innerContent;
            }
        }

        if (isEnumerable && sanitized.StartsWith("{"))
        {
            sanitized = $"[{sanitized}]";
        }

        return sanitized;
    }

    public static string ProcessResponse(string text)
    {
        string sanitized = text
            // Full-width punctuation to ASCII
            .Replace("：",":")
            .Replace("，",",")
            .Replace("（","(")
            .Replace("）",")")
            .Replace("【", "[")
            .Replace("】", "]")
            .Replace("｛", "{")
            .Replace("｝", "}")
            .Replace("“","\"")
            .Replace("”","\"")
            .Replace("＂","\"")
            .Replace("‘", "'")
            .Replace("’", "'")
            .Replace("…", "...")
            .Replace("—", "-")
            .Replace("－", "-")
            .Replace("～", "~")
            .Replace("　", " ")  // Full-width space
            .Trim();

        // Remove invalid escape sequences that LLMs sometimes generate
        sanitized = Regex.Replace(sanitized, @"\\(?![""\\bfnrtu/])", "");

        // Fix unquoted property names
        List<string> keys = ["name", "text", "act", "target"];
        foreach (var key in keys)
        {
            sanitized = Regex.Replace(
                sanitized,
                $"^[ \\t\"]*{key}[ \\t\"]*:",
                $"\"{key}\":",
                RegexOptions.Multiline);
        }

        // Fix unquoted string values (e.g., "text": (something) -> "text": "(something)")
        // Match property name followed by colon, optional whitespace, then a non-quote character that starts the value
        sanitized = Regex.Replace(
            sanitized,
            @"(""(?:name|text|act|target)""\s*:\s*)([^""\s\[\]{},][^\r\n]*?)(\s*[,}\]])",
            @"$1""$2""$3",
            RegexOptions.Multiline);

        // Fix trailing commas before closing brackets (e.g., ,] or ,})
        sanitized = Regex.Replace(sanitized, @",(\s*[}\]])", "$1");

        // Fix missing commas between array elements: }{ -> },{
        sanitized = Regex.Replace(sanitized, @"\}(\s*)\{", "},$1{");

        // Fix missing commas between properties: "value""key" -> "value","key"
        sanitized = Regex.Replace(sanitized, @"""(\s*)""([^:]+)"":", @""",$1""$2"":");

        // Remove control characters that break JSON parsing (except valid whitespace)
        sanitized = Regex.Replace(sanitized, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

        // Check if there are potential embedded quotes that need removal
        // Look for patterns like: "property":"value"with"embedded"quote"
        var propertyPattern = @"""(?:name|text|act|target)""\s*:\s*""[^""]*""[^,}\]]+""";
        bool hasEmbeddedQuotes = Regex.IsMatch(sanitized, propertyPattern);
        
        if (hasEmbeddedQuotes)
        {
            // Fix unescaped quotes inside string values for known properties
            sanitized = RemoveEmbeddedQuotesInStringValues(sanitized);
        }

        return sanitized;
    }

    /// <summary>
    /// Removes unescaped double quotes that are embedded inside JSON string values.
    /// Detects closing quotes by checking if they are followed by valid JSON structure (, } or ]).
    /// </summary>
    private static string RemoveEmbeddedQuotesInStringValues(string json)
    {
        var result = new StringBuilder();
        var propertyPattern = new Regex(@"""(name|text|act|target)""\s*:\s*""");
        int currentPos = 0;

        var match = propertyPattern.Match(json, currentPos);
        while (match.Success)
        {
            // Append everything up to and including the opening quote of the value
            result.Append(json, currentPos, match.Index + match.Length - currentPos);
            int valueStart = match.Index + match.Length;

            // Scan for the actual closing quote (one followed by valid JSON structure)
            int pos = valueStart;
            while (pos < json.Length)
            {
                if (json[pos] == '"')
                {
                    if (IsLikelyClosingQuote(json, pos))
                    {
                        // Append the value content (without embedded quotes) and the closing quote
                        result.Append(json, valueStart, pos - valueStart);
                        result.Append('"');
                        currentPos = pos + 1;
                        break;
                    }
                    else
                    {
                        // This is an embedded quote - append content before it, skip the quote itself
                        result.Append(json, valueStart, pos - valueStart);
                        valueStart = pos + 1; // Skip the embedded quote
                    }
                }
                pos++;
            }

            if (pos >= json.Length)
            {
                // No closing quote found, append the rest as-is
                result.Append(json, valueStart, json.Length - valueStart);
                currentPos = json.Length;
                break;
            }

            match = propertyPattern.Match(json, currentPos);
        }

        // Append any remaining content after the last match
        if (currentPos < json.Length)
        {
            result.Append(json, currentPos, json.Length - currentPos);
        }

        return result.ToString();
    }

    /// <summary>
    /// Determines if a quote at the given position is likely the closing quote of a JSON string value.
    /// A closing quote should be followed by , } or ] (with optional whitespace).
    /// If followed by comma, the next non-whitespace should be " (start of next property) or } ].
    /// </summary>
    private static bool IsLikelyClosingQuote(string json, int quoteIndex)
    {
        int j = quoteIndex + 1;

        // Skip whitespace and newlines
        while (j < json.Length && (char.IsWhiteSpace(json[j]) || json[j] == '\r' || json[j] == '\n')) 
            j++;

        if (j >= json.Length) return true; // End of string

        char next = json[j];

        // Definitely closing if followed by } or ]
        if (next == '}' || next == ']') return true;

        if (next == ',')
        {
            // Check what's after the comma - should be start of next property (") or end of object/array
            int k = j + 1;
            while (k < json.Length && (char.IsWhiteSpace(json[k]) || json[k] == '\r' || json[k] == '\n')) 
                k++;

            if (k >= json.Length) return true;

            // Next property should start with " or it could be trailing comma before } ]
            if (json[k] == '"' || json[k] == '}' || json[k] == ']') return true;

            // Check if we're looking at a property name pattern: "propertyname":
            // This handles cases where there's a comma followed by a property
            if (json[k] == '"')
            {
                int colonPos = json.IndexOf(':', k);
                if (colonPos > k && colonPos < k + 50) // property names shouldn't be too long
                {
                    return true;
                }
            }

            // Otherwise, this comma is likely part of the string content (e.g., Chinese text with internal commas)
            return false;
        }

        // Quote not followed by valid JSON structure - likely embedded
        return false;
    }
}