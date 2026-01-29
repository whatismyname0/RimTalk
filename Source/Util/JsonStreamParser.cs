using System;
using System.Collections.Generic;
using System.Text;

namespace RimTalk.Util;

public class JsonStreamParser<T> where T : class
{
    private readonly StringBuilder _buffer = new();

    public List<T> Parse(string textChunk)
    {
        _buffer.Append(textChunk);
        var newObjects = new List<T>();
        string text = _buffer.ToString();
        int searchStart = 0;
        int lastSuccessfulEnd = 0;

        while (searchStart < text.Length)
        {
            int objStart = text.IndexOf('{', searchStart);
            if (objStart == -1)
            {
                break;
            }

            int objEnd = FindMatchingBrace(text, objStart);
            if (objEnd == -1)
            {
                // Incomplete object
                break;
            }

            string jsonObj = text.Substring(objStart, objEnd - objStart + 1);
            
            // Process and potentially split into multiple objects
            string processedJson = JsonUtil.ProcessResponse(jsonObj);
            
            // If ProcessResponse returned multiple objects, put them back in buffer for re-parsing
            if (processedJson.Length > jsonObj.Length * 2 || CountBraces(processedJson) > 1)
            {
                // Insert processed JSON back into buffer at current position
                _buffer.Remove(0, objEnd + 1);
                _buffer.Insert(0, processedJson);
                text = _buffer.ToString();
                searchStart = 0;
                lastSuccessfulEnd = 0;
                continue;
            }

            try
            {
                var parsedObject = JsonUtil.DeserializeFromJson<T>(processedJson);
                if (parsedObject != null)
                {
                    newObjects.Add(parsedObject);
                }
            }
            catch (Exception ex)
            {
                // Log the error and continue searching for next valid object
                Logger.Warning($"Failed to parse JSON object in stream: {ex.Message}\nJSON: {processedJson}");
            }

            searchStart = objEnd + 1;
            lastSuccessfulEnd = searchStart;
        }

        if (lastSuccessfulEnd > 0)
        {
            _buffer.Remove(0, lastSuccessfulEnd);
        }

        return newObjects;
    }

    private int CountBraces(string text)
    {
        int count = 0;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{')
            {
                if (depth == 0)
                {
                    count++;
                }
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
        }

        return count;
    }

    private int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1; // No matching brace found
    }
}