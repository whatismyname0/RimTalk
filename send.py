import requests
import json
import time
from typing import List, Optional

class DeepSeekProcessor:
    def __init__(self, api_key: str, base_url: str = "https://api.deepseek.com/v1/chat/completions"):
        self.api_key = api_key
        self.base_url = base_url
        self.headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {api_key}"
        }
    
    def read_data_in_chunks(self, filename: str, chunk_size: int = 50) -> List[str]:
        """从文件中按指定大小读取数据块"""
        chunks = []
        current_chunk = []
        
        try:
            with open(filename, 'r', encoding='utf-8') as file:
                for i, line in enumerate(file, 1):
                    current_chunk.append(line.strip())
                    if i % chunk_size == 0:
                        chunks.append(current_chunk)
                        current_chunk = []
                
                # 添加最后不足chunk_size的行
                if current_chunk:
                    chunks.append(current_chunk)
            
            print(f"成功读取文件，共分成 {len(chunks)} 个数据块")
            return chunks
        
        except FileNotFoundError:
            print(f"错误：文件 {filename} 未找到")
            return []
        except Exception as e:
            print(f"读取文件时发生错误：{e}")
            return []
    
    def create_prompt(self, custom_prompt: str, data_chunk: List[str]) -> str:
        """创建自定义提示词"""
        data_text = "\n".join(data_chunk)
        return f"{custom_prompt}\n\n数据：\n{data_text}"
    
    def send_to_deepseek(self, prompt: str, max_retries: int = 3) -> Optional[str]:
        """发送请求到DeepSeek API"""
        payload = {
            "model": "deepseek-chat",
            "messages": [
                {
                    "role": "user",
                    "content": prompt
                }
            ],
            "stream": False,
            "temperature": 0.7,
            "max_tokens": 4000
        }
        
        for attempt in range(max_retries):
            try:
                response = requests.post(
                    self.base_url,
                    headers=self.headers,
                    data=json.dumps(payload),
                    timeout=120
                )
                
                if response.status_code == 200:
                    result = response.json()
                    return result['choices'][0]['message']['content']
                else:
                    print(f"API请求失败 (尝试 {attempt + 1}/{max_retries}): {response.status_code} - {response.text}")
                    if attempt < max_retries - 1:
                        time.sleep(2 ** attempt)  # 指数退避
                    
            except requests.exceptions.Timeout:
                print(f"请求超时 (尝试 {attempt + 1}/{max_retries})")
                if attempt < max_retries - 1:
                    time.sleep(2 ** attempt)
            except Exception as e:
                print(f"请求发生错误 (尝试 {attempt + 1}/{max_retries}): {e}")
                if attempt < max_retries - 1:
                    time.sleep(2 ** attempt)
        
        return None
    
    def save_result(self, result: str, filename: str = "result.txt"):
        """保存结果到文件"""
        try:
            with open(filename, 'a', encoding='utf-8') as file:
                file.write(f"{result}\n")
            return True
        except Exception as e:
            print(f"保存结果时发生错误：{e}")
            return False
    
    def process_data(self, 
                    data_file: str = "data.txt", 
                    result_file: str = "result.txt",
                    custom_prompt: str = "请分析以下数据：",
                    chunk_size: int = 50,
                    delay_between_requests: float = 1.0):
        """主处理函数"""
        
        # 读取数据块
        chunks = self.read_data_in_chunks(data_file, chunk_size)
        if not chunks:
            print("没有数据可处理")
            return
        
        # 清空或创建结果文件
        open(result_file, 'w', encoding='utf-8').close()
        
        total_chunks = len(chunks)
        successful_requests = 0
        
        print(f"开始处理 {total_chunks} 个数据块...")
        
        for i, chunk in enumerate(chunks, 1):
            print(f"处理第 {i}/{total_chunks} 个数据块...")
            
            # 创建提示词
            prompt = self.create_prompt(custom_prompt, chunk)
            
            # 发送到DeepSeek
            result = self.send_to_deepseek(prompt)
            
            if result:
                # 保存结果
                if self.save_result(result, result_file):
                    successful_requests += 1
                    print(f"✓ 第 {i} 个数据块处理完成并保存")
                else:
                    print(f"✗ 第 {i} 个数据块结果保存失败")
            else:
                print(f"✗ 第 {i} 个数据块处理失败")
            
            # 添加延迟，避免请求过于频繁
            if i < total_chunks:
                print(f"等待 {delay_between_requests} 秒后继续...")
                time.sleep(delay_between_requests)
        
        print(f"\n处理完成！成功处理 {successful_requests}/{total_chunks} 个数据块")
        print(f"结果已保存到 {result_file}")

def main():
    # 配置参数
    API_KEY = "sk-f2d7b4dd3cc14a6da574ea824b64978d"  # 替换为您的DeepSeek API密钥
    DATA_FILE = "data.txt"
    RESULT_FILE = "result.txt"
    CUSTOM_PROMPT = "请分析以下文本数据，将每一行输入根据语义拆解成一个或多个条目，拆解后条目名称与原条目完全一致。每个拆解后的条目分别占一行。条目输出格式（按照格式化字符串处理）：\n{条目名称}{使用文字连贯地衔接,不要用冒号}{描述}。\n不要输出其他内容"  # 可自定义的提示词
    CHUNK_SIZE = 50  # 每次处理的行数
    DELAY = .5  # 请求间隔时间（秒）
    
    # 创建处理器实例
    processor = DeepSeekProcessor(API_KEY)
    
    # 开始处理数据
    processor.process_data(
        data_file=DATA_FILE,
        result_file=RESULT_FILE,
        custom_prompt=CUSTOM_PROMPT,
        chunk_size=CHUNK_SIZE,
        delay_between_requests=DELAY
    )

if __name__ == "__main__":
    main()