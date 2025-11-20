"""
ChromaDB manager for RimTalk conversation storage and retrieval.
Handles per-save database management, storing conversations with metadata,
and querying relevant historical context for prompt enrichment.
"""
import fuzzywuzzy
import chromadb
import hashlib
import json
from pathlib import Path
from typing import Dict, List, Optional, Tuple
import threading
import shutil

# Global embedding model (loaded once)
_model = None
_model_lock = threading.Lock()


def get_embedding_model():
    """Get or initialize the lightweight Chinese embedding model."""
    global _model
    if _model is None:
        with _model_lock:
            if _model is None:
                # CHANGE 1: Use the standard FlagModel, not BGEM3FlagModel
                # CHANGE 2: Use the 'small' Chinese model
                from FlagEmbedding import FlagModel
                _model = FlagModel(
                    # 'BAAI/bge-base-zh-v1.5', 
                    'BAAI/bge-m3', 
                    use_fp16=True
                )
    return _model

class BGE_Base_ZH(chromadb.EmbeddingFunction):
    """Wrapper for BGE-Base-ZH."""
    def __call__(self, input: chromadb.Documents) -> chromadb.Embeddings:
        model = get_embedding_model()
        # FlagModel.encode returns a list of arrays, which fits Chroma's expectation
        return model.encode(input).tolist()


class ChromaDBManager:
    """
    Manages ChromaDB instances for each RimWorld save.
    - Creates/opens per-save databases
    - Stores conversations with rich metadata
    - Queries relevant historical context
    - Enforces entry limit (800k) with cleanup
    """

    def __init__(self, base_dir: str = "./chromadb"):
        """
        Initialize the ChromaDB manager.
        
        Args:
            base_dir: Base directory for all ChromaDB instances
        """
        self.base_dir = Path(base_dir)
        self.base_dir.mkdir(parents=True, exist_ok=True)
        
        # Active collections per save (save_id -> collection)
        self._collections: Dict[str, chromadb.Collection] = {}
        self._clients: Dict[str, chromadb.PersistentClient] = {}
        self._lock = threading.Lock()
        
        # Entry limit per collection
        self.ENTRY_LIMIT = 200000
        self.embedding_fn = BGE_Base_ZH()

    def check_database_health(self, save_id: str) -> bool:
        try:
            # 步骤1: 获取集合（测试连接是否正常）
            collection = self.get_or_create_collection(save_id)
            
            # 步骤2: 测试计数操作（基本读取测试）
            count = collection.count()
            
            # 步骤3: 测试查询操作（复杂操作测试）
            test_results = collection.peek(limit=1)  # 查看前1条记录
            
            # 步骤4: 所有测试通过，返回健康状态
            return True
            
        except Exception as e:
            # 步骤5: 任何一步出错都认为数据库不健康
            return False

    def get_or_create_collection(self, save_id: str) -> chromadb.Collection:
        """
        Get or create a ChromaDB collection for a specific save.
        
        Args:
            save_id: Unique identifier for the save (e.g., world name or hash)
            
        Returns:
            ChromaDB collection for this save
        """
        with self._lock:
            if save_id in self._collections:
                return self._collections[save_id]

            # Create save-specific directory
            save_dir = self.base_dir / save_id
            save_dir.mkdir(parents=True, exist_ok=True)

            # Create persistent client for this save
            client = chromadb.PersistentClient(path=str(save_dir))
            self._clients[save_id] = client

            # Get or create collection
            collection = client.get_or_create_collection(
                name="conversations",
                #embedding_function=self.embedding_fn,
                metadata={"save_id": save_id}
            )
            
            self._collections[save_id] = collection
            return collection

    def add_conversation(
        self,
        save_id: str,
        talk_responses: List[Dict],
        speakers: List[str],
        listeners: List[str],
        date_string: str,
        talk_type: str
    ) -> bool:
        """
        Store a conversation turn in the database.
        
        Args:
            save_id: Save identifier
            talk_responses: List of TalkResponse dicts (name, text, talk_type)
            speakers: List of speaker names
            listeners: List of listener names (allInvolvedPawns)
            date_string: In-game date string (e.g., "5th of Septober, year 5500")
            talk_type: Type of the entry (e.g., "dialogue", "info")
            
        Returns:
            True if successful, False otherwise
        """
        try:
            collection = self.get_or_create_collection(save_id)
            
            # Check entry limit and cleanup if needed
            self._enforce_entry_limit(save_id)
            
            documents = []
            ids = []
            metadatas = []
            
            for idx, response in enumerate(talk_responses):
                # Create unique ID
                doc_id = f"{save_id}_{len(collection.get()['ids'])}_{idx}"
                
                # Prepare metadata - include speaker, listeners, and date
                metadata = {
                    "save_id": save_id,
                    "speaker": response.get("name", "Unknown"),
                    "listeners": json.dumps(listeners),
                    "date": date_string,
                    "talk_type": response.get("talk_type", "Unknown")
                }
                
                documents.append(response.get("text", ""))
                ids.append(doc_id)
                metadatas.append(metadata)
                
                #print(f"[ChromaManager] Storing entry: speaker={metadata['speaker']}, date={metadata['date']}, listeners={metadata['listeners']}", flush=True)
            
            # Add to collection
            if documents:
                collection.add(
                    ids=ids,
                    documents=documents,
                    metadatas=metadatas
                )
            
            #print(f"[ChromaManager] Successfully stored {len(documents)} entries for save {save_id}", flush=True)
            return True
            
        except Exception as e:
            print(f"[RimTalk ChromaDB] Error adding conversation: {e}", flush=True)
            return False
        
    def query_relevant_context(
        self,
        save_id: str,
        query_texts: List[str], # Accepts a list of query strings
        n_results: int = 5,
        speakers: Optional[List[str]] = None,
        listeners: Optional[List[str]] = None
    ) -> List[Dict]:
        """
        Query historically relevant conversations for context enrichment using multiple query vectors.
        
        Args:
            save_id: Identifier for the current save database.
            query_texts: A list of search query strings generated by the AI.
            n_results: The maximum number of results to return.
            speakers: Optional list of speakers to filter conversational history by (for proximity/relation).
            listeners: Optional list of listeners to filter conversational history by (for proximity/relation).
            
        Returns:
            A list of dictionaries, each representing a unique, relevant context entry, 
            sorted by normalized relevance score (highest first).
        """
        try:
            collection = self.get_or_create_collection(save_id)
            
            # 1. Basic checks
            if collection.count() == 0 or not query_texts:
                return []
            
            # Ensure input is a list (though it should be by this point)
            if isinstance(query_texts, str):
                query_texts = [query_texts]
            
            all_results_map = {} # Use a dict (ID: result_dict) to deduplicate results
            all_results_map_text = {} 

            # 2. Helper to process batch results from Chroma
            def process_batch_results(results):
                """Processes the nested list structure returned by Chroma's batch query."""
                if results and results['documents']:
                    # Iterate over each query's result set
                    for i in range(len(results['documents'])): 
                        if not results['documents'][i]: continue
                        
                        # Iterate over the results for the i-th query
                        for doc, meta, dist, id in zip(
                            results['documents'][i],
                            results['metadatas'][i],
                            results['distances'][i],
                            results['ids'][i]
                        ):
                            # Listener filtering (only applies to non-'info' talk_type)
                            if meta.get("talk_type") != "info" and listeners:
                                doc_listeners = json.loads(meta.get("listeners", "[]"))
                                # Check if any of the target listeners are in the document's listeners
                                if not any(l in doc_listeners for l in listeners):
                                    continue

                            doc2=doc+(":"+meta.get("definition","([WARNING] Info entry does not include a definition.)") if (meta.get("talk_type", "")=="info" and meta.get("definition") != "N/A") else "")
                            id2 = id
                            id2=id2.replace("_short","")
                            if meta.get("talk_type") == "info":
                                lenS=len(query_texts[i])
                                substrings=[doc2[i:i+lenS]for i in range(len(doc2)-lenS+1)]
                                best, score = fuzzywuzzy.process.extractOne(query_texts[i], substrings, scorer=fuzzywuzzy.fuzz.ratio)
                                if score<70:
                                    continue
                                
                            # Deduplicate based on unique ID
                            if id2 not in all_results_map:
                                # Normalize distance (L2 norm) to relevance score (e.g., 1.0 - distance/2.0)
                                relevance = float(1.0-(dist/2.0))
                                
                                all_results_map[id2] = {
                                    "text": doc2,
                                    "speaker": meta.get("speaker", "Unknown"),
                                    "listeners": json.loads(meta.get("listeners", "[]")),
                                    "date": meta.get("date", ""),
                                    "talk_type": meta.get("talk_type", ""),
                                    "relevance": relevance,
                                }
                            else:
                                # If found again (via another keyword), keep the higher relevance score
                                current_rel = float(1.0-(dist/2.0))
                                if current_rel > all_results_map[id2]["relevance"]:
                                    all_results_map[id2]["relevance"] = current_rel

            # 3. Query 'info' (background) with ALL keywords (no speaker/listener filter)
            info_results = collection.query(
                query_texts=query_texts,
                n_results=min(50, collection.count()), # Query more results for better merging
                where={"talk_type": "info"}
            )
            process_batch_results(info_results)

            # 4. Prepare filter for conversation history (talk_type != "info")
            where_filter = None
            conditions = [{"talk_type": {"$ne": "info"}}]

            if speakers:
                # Speakers filter: Matches documents where the 'speaker' is one of the desired speakers
                speaker_conditions = [{"speaker": {"$eq": s}} for s in speakers]
                if len(speaker_conditions) > 1:
                    conditions.append({"$or": speaker_conditions})
                else:
                    conditions.extend(speaker_conditions)
            
            if len(conditions) > 1:
                where_filter = {"$and": conditions}
            elif conditions:
                where_filter = conditions[0]

            # 5. Query conversation history with ALL keywords
            # Note: Listener filtering is handled post-retrieval in process_batch_results 
            # because the 'listeners' metadata is stored as a JSON string, not a direct list field.
            filtered_results = collection.query(
                query_texts=query_texts,
                n_results=min(10, collection.count()), 
                where=where_filter
            )
            process_batch_results(filtered_results)
            
            # 6. Final sorting and truncation
            final_results = list(all_results_map.values())
            final_results.sort(key=lambda x: x["relevance"], reverse=True)
            
            # Return only the top N results
            return final_results[:n_results]
            
        except Exception as e:
            # Standard error logging/handling
            print(f"[RimTalk ChromaDB] Error querying context: {e}")
            return []

    def info(
        self,
        save_id: str):
        try:
            collection = self.get_or_create_collection(save_id)
            return {
                "count": collection.count()
            }
        except Exception as e:
            print(f"[RimTalk ChromaDB] Error getting info: {e}")
            return {}
        
    def update_background(
    self,
    save_id: str,
    talk_responses: List[str],
    speakers: List[str],
    listeners: List[str],
    date_string: str,
    talk_type: str
    ) -> str:
        """
        Update background in the database.
        
        Args:
            save_id: Save identifier
            talk_responses: List of TalkResponse dicts (name, text, talk_type)
            speakers: List of speaker names
            listeners: List of listener names (allInvolvedPawns)
            date_string: In-game date string (e.g., "5th of Septober, year 5500")
            talk_type: Type of the entry (e.g., "dialogue", "info")
            
        Returns:
            string indicating success or error message
        """
        try:
            collection = self.get_or_create_collection(save_id)
            
            # Check entry limit and cleanup if needed
            self.delete_background(save_id)
            
            documents = []
            ids = []
            metadatas = []
            collection_info = collection.get()
            existing_ids_count = len(collection_info['ids'])
            
            for i, entry in enumerate(talk_responses):
                # Create unique ID
                doc_id = f"{save_id}_{existing_ids_count + i}_{hashlib.md5(entry.encode()).hexdigest()}"

                # Prepare metadata - include speaker, listeners, and date
                metadata = {
                    "save_id": save_id,
                    "speaker": "system",
                    "listeners": json.dumps([]),
                    "date": date_string,
                    "talk_type": "info",
                    "definition": "N/A",
                }
                
                documents.append(entry)
                ids.append(doc_id)
                metadatas.append(metadata)

                doc_id2 = doc_id + "_short"
                
                entry2 = entry.split(':',1)

                metadata2 = metadata.copy()
                metadata2["definition"] = entry2[-1]
                
                documents.append(entry2[0])
                ids.append(doc_id2)
                metadatas.append(metadata2)
                
                #print(f"[ChromaManager] Storing entry: speaker={metadata['speaker']}, date={metadata['date']}, listeners={metadata['listeners']}", flush=True)
            
            # Add to collection
            if documents:
                collection.add(
                    ids=ids,
                    documents=documents,
                    metadatas=metadatas
                )
            
            #print(f"[ChromaManager] Successfully stored {len(documents)} entries for save {save_id}", flush=True)
            return "Success"
            
        except Exception as e:
            return f"[RimTalk ChromaDB] Error updating background: {e}"

    def query_all_entry(
        self,
        save_id: str):
        try:
            collection = self.get_or_create_collection(save_id)
            
            if collection.count() == 0:
                return []
            
            # Query using prompt for semantic search
            results = collection.get()
            
            # Format results
            relevant = []
            if results and results['documents'] and len(results['documents']) > 0:
                for id, doc, meta in zip(
                    results['ids'],
                    results['documents'],
                    results['metadatas']
                ):
                    
                    relevant.append({
                        "id": id,
                        "text": doc+(":"+meta.get("definition","([WARNING] Info entry does not include a definition.)") if (meta.get("talk_type", "")=="info" and meta.get("definition") != "N/A") else ""),
                        "speaker": meta.get("speaker", "Unknown"),
                        "listeners": json.loads(meta.get("listeners", "[]")),
                        "date": meta.get("date", ""),
                        "talk_type": meta.get("talk_type", ""),
                    })
            
            return relevant
            
        except Exception as e:
            print(f"[RimTalk ChromaDB] Error querying all entry: {e}")
            return []
        
    def ensure_healthy_database(self, save_id: str):
        if not self.check_database_health(save_id):
            return self.reset_corrupted_database(save_id)
        else:
            return self.get_or_create_collection(save_id)
        
    def reset_corrupted_database(self, save_id: str):
        # 1. 关闭现有连接
        del self._collections[save_id]
        del self._clients[save_id]
        
        # 2. 删除数据库目录（彻底清除）
        shutil.rmtree(self.base_dir / save_id, ignore_errors=True)
        
        # 3. 重新创建集合（全新的开始）
        return self.get_or_create_collection(save_id)

    def _enforce_entry_limit(self, save_id: str):
        """
        Enforce the 800k entry limit by removing oldest entries if exceeded.
        
        Args:
            save_id: Save identifier
        """
        try:
            collection = self.get_or_create_collection(save_id)
            current_count = collection.count()
            
            if current_count >= self.ENTRY_LIMIT:
                # Get all entries and sort by insertion order (remove oldest)
                all_data = collection.get(
                    where={"talk_type": {"$ne": "info"}}  # 排除 talk_type="info" 的条目
                )
                ids = all_data['ids']
                
                # Remove oldest 10% when limit exceeded
                remove_count = max(1, current_count // 10)
                ids_to_remove = ids[:remove_count]
                
                collection.delete(ids=ids_to_remove)
                #print(f"[RimTalk ChromaDB] Cleaned up {remove_count} old entries for save {save_id}")
            return "success"
                
        except Exception as e:
            return f"[RimTalk ChromaDB] Error enforcing entry limit: {e}"

    def delete_background(self, save_id: str):
        """
        Delete background entries.
        
        Args:
            save_id: Save identifier
        """
        try:
            collection = self.get_or_create_collection(save_id)
            all_data = collection.get(
                where={"talk_type": "info"}
            )
            if not all_data['ids']:
                return
            ids = all_data['ids']
            collection.delete(ids=ids)
                
        except Exception as e:
            return f"[RimTalk ChromaDB] Error deleting background: {e}"

    def close_save(self, save_id: str):
        """
        Close and unload a save's database connection.
        
        Args:
            save_id: Save identifier
        """
        with self._lock:
            if save_id in self._collections:
                del self._collections[save_id]
            if save_id in self._clients:
                del self._clients[save_id]


# Global manager instance
_manager = None
_manager_lock = threading.Lock()


def get_manager() -> ChromaDBManager:
    """Get or create the global ChromaDB manager."""
    global _manager
    if _manager is None:
        with _manager_lock:
            if _manager is None:
                _manager = ChromaDBManager()
    return _manager


def initialize_manager(base_dir: str = "./chromadb"):
    """Initialize the global manager with custom base directory."""
    global _manager
    with _manager_lock:
        _manager = ChromaDBManager(base_dir)
    return _manager
