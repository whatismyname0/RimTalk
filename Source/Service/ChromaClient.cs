using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using RimTalk.Data;
using RimTalk.Util;

namespace RimTalk.Service;

/// <summary>
/// Client for communicating with Python ChromaManager via subprocess/IPC.
/// Handles serialization/deserialization of ChromaDB operations.
/// </summary>
public class ChromaClient
{
    private Process _pythonProcess;
    private StreamWriter _stdin;
    private StreamReader _stdout;
    private readonly object _lock = new object();
    private readonly string _chromaManagerPath="D:\\steam\\steamapps\\common\\RimWorld\\Mods\\RimTalk-main\\Source\\ChromaManager\\ChromaManager_CLI.py";
    private readonly string _modDirectory="D:\\steam\\steamapps\\common\\RimWorld\\Mods\\RimTalk-main\\Source\\ChromaManager";

    /// <summary>
    /// Initialize the ChromaDB client for a specific save.
    /// </summary>
    public void Initialize(string saveId)
    {
        lock (_lock)
        {
            try
            {
                // Verify the ChromaManager script exists
                if (!File.Exists(_chromaManagerPath))
                {
                    throw new FileNotFoundException($"ChromaManager_CLI.py not found at: {_chromaManagerPath}");
                }

                // Start Python process running ChromaManager
                var psi = new ProcessStartInfo
                {
                    FileName = "D:\\Program\\.venv\\Scripts\\python.exe",
                    Arguments = $"\"{_chromaManagerPath}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _modDirectory
                };

                Logger.Debug($"[ChromaClient] Starting Python process with working directory: {psi.WorkingDirectory}");
                Logger.Debug($"[ChromaClient] Python executable: {psi.FileName}");
                Logger.Debug($"[ChromaClient] Arguments: {psi.Arguments}");

                _pythonProcess = Process.Start(psi);
                _stdin = _pythonProcess.StandardInput;
                _stdout = _pythonProcess.StandardOutput;

                // Give process time to initialize and fail if it's going to crash
                Thread.Sleep(500);
                
                if (_pythonProcess.HasExited)
                {
                    Logger.Error($"[ChromaClient] Python process exited immediately with code: {_pythonProcess.ExitCode}");
                    try
                    {
                        var stderr = _pythonProcess.StandardError.ReadToEnd();
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            Logger.Error($"[ChromaClient] Python startup error: {stderr}");
                        }
                    }
                    catch { }
                    throw new Exception("Python process crashed on startup");
                }

                // Send initialization command
                var initResponse = SendCommand(new Dictionary<string, object>
                {
                    { "action", "init" },
                    { "save_id", saveId }
                });

                if (initResponse == null)
                {
                    throw new Exception("No response from Python process during initialization");
                }

                Logger.Debug($"[ChromaClient] Initialized for save: {saveId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[ChromaClient] Failed to initialize: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Add a conversation to ChromaDB.
    /// </summary>
    public void updateBackground(
        string saveId,
        string str)
    {
        List<string> backgrounds = str.Split(['\n']).ToList();
        
        var response = SendCommand(new Dictionary<string, object>
        {
            { "action", "update_background" },
            { "save_id", saveId },
            { "responses", backgrounds },
            { "speakers", new List<string>() },
            { "listeners", new List<string>() },
            { "date", "Not Applicable" },
            {"type", "info" }
        });

        if (response == null)
        {
            Logger.Warning("[ChromaClient] updateBackground received null response from Python");
        }
        else
        {
            Logger.Debug($"[ChromaClient] updateBackground response: {response}");
        }
    }

    public void AddConversation(
        string saveId,
        List<TalkResponse> responses,
        List<string> speakers,
        List<string> listeners,
        string dateString)
    {
        Logger.Debug($"[ChromaClient] AddConversation called: {responses.Count} responses, speakers={string.Join(",", speakers)}, date={dateString}");
        
        // Convert responses to a serializable format
        var responseList = new List<Dictionary<string, object>>();
        foreach (var r in responses)
        {
            responseList.Add(new Dictionary<string, object>
            {
                { "name", r.Name },
                { "text", r.Text },
                { "talk_type", r.TalkType.ToString() }
            });
        }
        
        // 修改：使用同步方法而不是异步方法
        var response = SendCommand(new Dictionary<string, object>
        {
            { "action", "add_conversation" },
            { "save_id", saveId },
            { "responses", responseList },
            { "speakers", speakers },
            { "listeners", listeners },
            { "date", dateString },
            {"type", responses[0].TalkType.ToString() }
        });

        if (response == null)
        {
            Logger.Warning("[ChromaClient] AddConversation received null response from Python");
        }
        else
        {
            Logger.Debug($"[ChromaClient] AddConversation response: {response}");
        }
    }

    /// <summary>
    /// Query relevant historical context from ChromaDB using a list of search queries.
    /// </summary>
    public List<ContextEntry> QueryContext(
        string saveId,
        List<string> queries, // CHANGED: string prompt -> List<string> queries
        List<string> listeners,
        int maxResults = 10)
    {
        Logger.Debug($"[ChromaClient] QueryContext called: queries_count={queries.Count}, listeners={string.Join(",", listeners)}, maxResults={maxResults}");
        
        var response = SendCommand(new Dictionary<string, object>
        {
            { "action", "query_context" },
            { "save_id", saveId },
            { "queries", queries }, // CHANGED: "prompt" -> "queries"
            { "listeners", listeners },
            { "n_results", maxResults }
        });

        if (response == null)
        {
            Logger.Warning("[ChromaClient] QueryContext received null response from Python");
            return new List<ContextEntry>();
        }

        try
        {
            Logger.Debug($"[ChromaClient] QueryContext parsing response: {response}");
            
            // Parse the wrapper response first
            var responseObj = JsonUtil.DeserializeToDictionary(response);
            if (responseObj == null || !responseObj.ContainsKey("data"))
            {
                Logger.Warning("[ChromaClient] Response missing 'data' field");
                return new List<ContextEntry>();
            }
            
            // Extract the data array - it will be a List<object> or similar
            var dataObj = responseObj["data"];
            if (dataObj == null)
            {
                Logger.Debug("[ChromaClient] Data field is null");
                return new List<ContextEntry>();
            }
            
            // If data is already a list, we need to convert it
            var resultList = new List<ContextEntry>();
            
            if (dataObj is System.Collections.IList dataList)
            {
                Logger.Debug($"[ChromaClient] Data is a list with {dataList.Count} items");
                
                foreach (var item in dataList)
                {
                    try
                    {
                        // Each item should be a Dictionary or object representing ContextEntry
                        var entry = new ContextEntry();
                        
                        if (item is Dictionary<string, object> dict)
                        {
                            entry.Text = GetDictString(dict, "text");
                            entry.Speaker = GetDictString(dict, "speaker");
                            entry.Date = GetDictString(dict, "date");
                            entry.TalkType = GetDictString(dict, "talk_type");
                            entry.Relevance = GetDictFloat(dict, "relevance");
                            
                            if (dict.ContainsKey("listeners"))
                            {
                                var listenersRet = dict["listeners"];
                                if (listenersRet is System.Collections.IList listenerList)
                                {
                                    entry.Listeners = listenerList.Cast<object>().Select(l => l?.ToString() ?? "").ToList();
                                }
                                else if (listenersRet is string listenerStr)
                                {
                                    entry.Listeners = new List<string> { listenerStr };
                                }
                            }
                            
                            resultList.Add(entry);
                            Logger.Debug($"[ChromaClient]   Entry: speaker={entry.Speaker}, relevance={entry.Relevance:F4}, date={entry.Date}");
                        }
                    }
                    catch (Exception itemEx)
                    {
                        Logger.Warning($"[ChromaClient] Error parsing individual entry: {itemEx.Message}");
                    }
                }
            }
            else
            {
                Logger.Warning($"[ChromaClient] Data is not a list, it's: {dataObj.GetType().Name}");
            }
            
            Logger.Debug($"[ChromaClient] QueryContext returned {resultList.Count} entries");
            return resultList;
        }
        catch (Exception ex)
        {
            Logger.Error($"[ChromaClient] Failed to deserialize context: {ex.GetType().Name}: {ex.Message}");
            Logger.Debug($"[ChromaClient] Response was: {response}");
            return new List<ContextEntry>();
        }
    }

    /// <summary>
    /// Helper to safely get string value from dictionary.
    /// </summary>
    private string GetDictString(Dictionary<string, object> dict, string key)
    {
        if (!dict.ContainsKey(key))
            return "";
        
        var value = dict[key];
        return value?.ToString() ?? "";
    }

    /// <summary>
    /// Helper to safely get float value from dictionary.
    /// </summary>
    private float GetDictFloat(Dictionary<string, object> dict, string key)
    {
        if (!dict.ContainsKey(key))
            return 0f;
        
        var value = dict[key];
        if (value == null)
            return 0f;
        
        if (value is float floatVal)
            return floatVal;
        
        if (value is double doubleVal)
            return (float)doubleVal;
        
        if (value is int intVal)
            return (float)intVal;
        
        if (float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
            return result;
        
        return 0f;
    }

    /// <summary>
    /// Close the save's connection.
    /// </summary>
    public void Close(string saveId)
    {
        try
        {
            SendCommand(new Dictionary<string, object>
            {
                { "action", "close_save" },
                { "save_id", saveId }
            });
        }
        catch { }
        finally
        {
            _stdin?.Close();
            _stdout?.Close();
            _pythonProcess?.Kill();
            _pythonProcess?.Dispose();
        }
    }

    /// <summary>
    /// Send a command and wait for response (synchronous).
    /// </summary>
    private string SendCommand(Dictionary<string, object> command)
    {
        lock (_lock)
        {
            try
            {
                var json = SerializeDictToJson(command);
                string action = command.ContainsKey("action") ? command["action"].ToString() : "unknown";
                Logger.Debug($"[ChromaClient] Sending command: action={action}, json_length={json.Length}");
                Logger.Debug($"[ChromaClient] Command JSON: {json}");
                
                _stdin.WriteLine(json);
                _stdin.Flush();
                Logger.Debug("[ChromaClient] Command sent to Python process");

                // Read response with timeout
                var responseTask = _stdout.ReadLineAsync();
                if (command["action"].ToString()!="update_background" &&!responseTask.Wait(TimeSpan.FromSeconds(45)))
                {
                    Logger.Error("[ChromaClient] Command timeout - no response from Python process");
                    return null;
                }

                string response = responseTask.Result;
                Logger.Debug($"[ChromaClient] Received response from Python: {response}");
                return response;
            }
            catch (Exception ex)
            {
                Logger.Error($"[ChromaClient] Error sending command: {ex.GetType().Name}: {ex.Message}");
                Logger.Debug($"[ChromaClient] Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }

    /// <summary>
    /// Simple JSON serializer for dictionaries (avoids DataContractSerializer issues).
    /// </summary>
    private string SerializeDictToJson(Dictionary<string, object> dict)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{");

        var kvps = dict.ToList();
        for (int i = 0; i < kvps.Count; i++)
        {
            var (key, value) = kvps[i];
            sb.Append($"\"{key}\":");
            sb.Append(SerializeValue(value));

            if (i < kvps.Count - 1)
                sb.Append(",");
        }

        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// Recursively serialize a value to JSON.
    /// </summary>
    private string SerializeValue(object value)
    {
        if (value == null)
            return "null";

        if (value is string str)
            return $"\"{EscapeJson(str)}\"";

        if (value is bool b)
            return b ? "true" : "false";

        if (value is int || value is long || value is double || value is float)
            return value.ToString();

        if (value is List<string> strList)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < strList.Count; i++)
            {
                sb.Append($"\"{EscapeJson(strList[i])}\"");
                if (i < strList.Count - 1)
                    sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // Handle List<Dictionary<string, object>>
        if (value is List<Dictionary<string, object>> dictList)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < dictList.Count; i++)
            {
                sb.Append(SerializeDictToJson(dictList[i]));
                if (i < dictList.Count - 1)
                    sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        if (value is List<object> objList)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < objList.Count; i++)
            {
                sb.Append(SerializeValue(objList[i]));
                if (i < objList.Count - 1)
                    sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        // Handle Dictionary<string, object>
        if (value is Dictionary<string, object> dict)
        {
            return SerializeDictToJson(dict);
        }

        // Handle anonymous objects / complex types
        if (value.GetType().IsClass && value.GetType().Name.Contains("Anonymous"))
        {
            var anonDict = new Dictionary<string, object>();
            foreach (var prop in value.GetType().GetProperties())
            {
                anonDict[prop.Name] = prop.GetValue(value);
            }
            return SerializeDictToJson(anonDict);
        }

        // Fallback: try to serialize as dict-like
        return "\"" + EscapeJson(value.ToString()) + "\"";
    }

    /// <summary>
    /// Escape JSON special characters.
    /// </summary>
    private string EscapeJson(string str)
    {
        if (string.IsNullOrEmpty(str))
            return "";

        return str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}