#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RimTalk ChromaManager CLI - handles stdin/stdout JSON-based communication.
Processes commands from C# ChromaClient via JSON lines protocol.
"""

import sys
import json
import io
import traceback

# Ensure UTF-8 encoding for stdin/stdout/stderr
if sys.version_info[0] >= 3:
    # Python 3: reconfigure standard streams for UTF-8
    sys.stdin = io.TextIOWrapper(sys.stdin.buffer, encoding='utf-8')
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', line_buffering=True)
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

try:
    from ChromaManager import get_manager
except Exception as e:
    # If import fails, print error and exit gracefully
    error_response = {
        "status": "error",
        "message": f"Failed to import ChromaManager: {type(e).__name__}: {str(e)}",
        "traceback": traceback.format_exc()
    }
    print(json.dumps(error_response, ensure_ascii=False), flush=True)
    sys.exit(1)

def main():
    """Main loop for processing commands from C# via stdin."""
    manager = get_manager()
    
    try:
        while True:
            # Read command line from stdin
            line = sys.stdin.readline()
            if not line:
                break
            
            # Strip BOM if present (defensive programming)
            # This can happen if C# sends UTF-8 with BOM
            if line.startswith('\ufeff'):
                line = line[1:]
            
            try:
                command = json.loads(line.strip())
                action = command.get("action")
                
                if action == "init":
                    save_id = command.get("save_id", "default")
                    manager.get_or_create_collection(save_id)
                    response = {"status": "ok", "message": f"Initialized for save: {save_id}"}

                elif action == "info":
                    save_id = command.get("save_id")
                    result = manager.info(save_id)
                    response = {"status": "ok", "data": result}

                elif action =="debug_get_all_entry":
                    save_id = command.get("save_id")
                    
                    results = manager.query_all_entry(
                        save_id
                    )
                    
                    # Convert ContextEntry objects to dicts
                    result_dicts = []
                    for r in results:
                        result_dicts.append({
                            "id": r["id"],
                            "text": r["text"],
                            "speaker": r["speaker"],
                            "listeners": r["listeners"],
                            "date": r["date"],
                            "talk_type": r["talk_type"]
                        })
                    
                    response = {"status": "ok", "data": result_dicts}
                
                elif action == "add_conversation":
                    save_id = command.get("save_id")
                    responses = command.get("responses", [])
                    speakers = command.get("speakers", [])
                    listeners = command.get("listeners", [])
                    date_string = command.get("date", "Not Specified")
                    
                    #print(f"[ChromaManager_CLI] add_conversation: save_id={save_id}, responses_count={len(responses)}, speakers={speakers}, listeners={listeners}, date={date_string}", flush=True)
                    
                    success = manager.add_conversation(
                        save_id,
                        responses,
                        speakers,
                        listeners,
                        date_string,
                        ""
                    )

                    if not success:
                        try:
                            manager.get_or_create_collection(save_id)
                            success = manager.add_conversation(
                                save_id,
                                responses,
                                speakers,
                                listeners,
                                date_string,
                                ""
                            )
                        except Exception as e:
                            response = {"status": "error", "message": f"Failed to create collection: {type(e).__name__}: {str(e)}"}
                    
                    #print(f"[ChromaManager_CLI] add_conversation result: success={success}", flush=True)
                    if success:
                        response = {"status": "ok", "message": "Conversation stored"}
                
                elif action == "query_context":
                    save_id = command.get("save_id")
                    
                    # CHANGED: Handle 'queries' list, fallback to 'prompt'
                    queries = command.get("queries", [])
                    if not queries:
                        single_prompt = command.get("prompt", "")
                        if single_prompt:
                            queries = [single_prompt]

                    listeners = command.get("listeners", [])
                    n_results = command.get("n_results", 5)
                    
                    results = manager.query_relevant_context(
                        save_id,
                        queries, # Pass list
                        n_results,
                        listeners
                    )
                    
                    # Convert ContextEntry objects to dicts
                    result_dicts = []
                    for r in results:
                        result_dicts.append({
                            "text": r["text"],
                            "speaker": r["speaker"],
                            "listeners": r["listeners"],
                            "date": r["date"],
                            "talk_type": r["talk_type"],
                            "relevance": r["relevance"]
                        })
                    
                    response = {"status": "ok", "data": result_dicts}

                elif action == "update_background":
                    def chunkize(arr, sz):
                        return [arr[i:min(i+sz,len(arr))] for i in range(0,len(arr),sz)]

                    save_id = command.get("save_id")
                    responses = chunkize(command.get("responses", []),200)
                    speakers = [[]]
                    listeners = [[]]
                    date_string = command.get("date", "Not applicable")
                    
                    for i in range(len(responses)):
                        success = manager.update_background(
                            save_id,
                            responses[i],
                            speakers[0],
                            listeners[0],
                            date_string,
                            "info"
                        )

                        if not "Error" in success:
                            response = {"status": "ok", "message": "Background updated"}
                        else:
                            response = {"status": "error", "message": success}
                            break
                
                elif action == "close_save":
                    save_id = command.get("save_id")
                    manager.close_save(save_id)
                    response = {"status": "ok", "message": f"Closed save: {save_id}"}
                
                else:
                    response = {"status": "error", "message": f"Unknown action: {action}"}
                
            except json.JSONDecodeError as e:
                response = {"status": "error", "message": f"Invalid JSON: {str(e)}"}
            except Exception as e:
                response = {"status": "error", "message": f"Error: {str(e)}"}
            
            # Send response as JSON line with UTF-8 encoding
            try:
                json_str = json.dumps(response, ensure_ascii=False, separators=(',', ': '))
                print(json_str, flush=True)
            except Exception as encode_err:
                # Fallback if there are encoding issues
                print(json.dumps({"status": "error", "message": f"Encoding error: {str(encode_err)}"}, ensure_ascii=True), flush=True)
    
    except KeyboardInterrupt:
        pass
    except Exception as e:
        error_msg = f"Fatal error: {type(e).__name__}: {str(e)}"
        try:
            print(json.dumps({"status": "error", "message": error_msg}, ensure_ascii=False), flush=True)
        except:
            print(json.dumps({"status": "error", "message": "Unknown fatal error"}, ensure_ascii=True), flush=True)
    finally:
        # Cleanup
        pass

if __name__ == "__main__":
    main()
