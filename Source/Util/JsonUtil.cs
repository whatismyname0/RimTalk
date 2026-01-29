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
        catch (Exception)
        {
            Logger.Error($"Json deserialization failed for {typeof(T).Name}\n{json}");
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

        if (sanitized.Contains("]["))
        {
            sanitized = sanitized.Replace("][", ",");
        }
        if (sanitized.Contains("}{"))
        {
            sanitized = sanitized.Replace("}{", "},{");
        }
    
        if (sanitized.StartsWith("{") && sanitized.EndsWith("}"))
        {
            string innerContent = sanitized.Substring(1, sanitized.Length - 2).Trim();
            if (innerContent.StartsWith("[") && innerContent.EndsWith("]"))
            {
                sanitized = innerContent;
            }
        }

        bool isEnumerable = typeof(IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string);
        if (isEnumerable && sanitized.StartsWith("{"))
        {
            sanitized = $"[{sanitized}]";
        }

        return sanitized;
    }

    public static string ProcessResponse(string text)
    {
        string sanitized = text
            .Replace("：",":")
            .Replace("，",",")
            .Replace("（","(")
            .Replace("）",")")
            .Replace(")(","")
            .Replace("“","\"")
            .Replace("”","\"")
            .Replace("＂","\"")
            .Trim();

        List<string> keys = ["name", "text", "act", "target"];
        foreach (var key in keys)
        {
            sanitized = Regex.Replace(
                sanitized,
                $"^[ \\t\"]*{key}[ \\t\"]*:",
                $"\"{key}\":",
                RegexOptions.Multiline);
        }
    
        while (sanitized.StartsWith("{") && sanitized.EndsWith("}"))
        {
            string innerContent = sanitized.Substring(1, sanitized.Length - 2).Trim();
            if (innerContent.StartsWith("{") && innerContent.EndsWith("}"))
            {
                sanitized = innerContent;
                continue;
            }
            break;
        }

        foreach (var key in keys)
        {
            var keyPattern = "\"" + key + "\"";
            int keyPos = sanitized.IndexOf(keyPattern, StringComparison.Ordinal);
            if (keyPos == -1) continue;

            int colonPos = sanitized.IndexOf(':', keyPos);
            if (colonPos == -1) continue;

            int valueStart = colonPos + 1;
            
            while (valueStart < sanitized.Length && char.IsWhiteSpace(sanitized[valueStart])) valueStart++;
            if (valueStart >= sanitized.Length) continue;

            int nextKeyPos = -1;
            foreach (var k2 in keys)
            {
                var p = sanitized.IndexOf("\"" + k2 + "\"", valueStart, StringComparison.Ordinal);
                if (p != -1)
                    if (nextKeyPos == -1 || p < nextKeyPos)
                        nextKeyPos = p;
            }

            int closingBracePos = sanitized.IndexOf('}', valueStart);
            int boundary = -1;
            if (nextKeyPos != -1) boundary = nextKeyPos;
            else if (closingBracePos != -1) boundary = closingBracePos;
            else continue;

            int valueEnd = boundary-1;
            while (char.IsWhiteSpace(sanitized[valueEnd]))
                valueEnd--;
            if (sanitized[valueEnd] == ',')
                valueEnd--;
            else
                sanitized = sanitized.Insert(valueEnd+1,",");

            if (valueEnd < valueStart) valueStart = valueEnd;
            if (sanitized[valueStart] == '"')
            {
                sanitized = sanitized.Remove(valueStart, 1);
                valueEnd--;
            }
            
            string rawValue = sanitized.Substring(valueStart, valueEnd - valueStart + 1).Trim();
            if (rawValue.EndsWith("\""))
                rawValue = rawValue.Replace("\"", "");

            string escaped = rawValue.Replace("\"", "'");
            sanitized = sanitized.Substring(0, valueStart) + "\"" + escaped + "\"" + sanitized.Substring(valueEnd+1);
        }

        return sanitized;
    }
}