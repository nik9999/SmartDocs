import os
import sys
import time
import urllib.request

def download_file(url, target_path, desc):
    if os.path.exists(target_path) and os.path.getsize(target_path) > 0:
        print(f"[SKIP] {desc} already exists ({os.path.getsize(target_path)/(1024*1024):.2f} MB)")
        return
    
    print(f"[DOWNLOADING] {desc} from {url}...")
    temp_path = target_path + ".tmp"
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"})
    
    start_time = time.time()
    last_print = 0
    with urllib.request.urlopen(req) as resp, open(temp_path, "wb") as f:
        total_size = int(resp.headers.get("Content-Length", 0))
        downloaded = 0
        chunk_size = 1024 * 1024 # 1 MB
        
        while True:
            chunk = resp.read(chunk_size)
            if not chunk:
                break
            f.write(chunk)
            downloaded += len(chunk)
            
            now = time.time()
            if now - last_print > 5 or downloaded == total_size:
                elapsed = max(now - start_time, 0.001)
                speed = (downloaded / (1024 * 1024)) / elapsed
                percent = (downloaded / total_size * 100) if total_size > 0 else 0
                print(f"  {downloaded/(1024*1024):.1f}/{total_size/(1024*1024):.1f} MB ({percent:.1f}%) at {speed:.2f} MB/s", flush=True)
                last_print = now
                
    os.replace(temp_path, target_path)
    print(f"[OK] {desc} downloaded successfully in {time.time()-start_time:.1f}s\n", flush=True)

def main():
    models = {
        "models/bge-m3": {
            "name": "bge-m3",
            "files": [
                ("https://huggingface.co/onnx-community/bge-m3-ONNX/resolve/main/config.json", "config.json"),
                ("https://huggingface.co/onnx-community/bge-m3-ONNX/resolve/main/tokenizer.json", "tokenizer.json"),
                ("https://huggingface.co/onnx-community/bge-m3-ONNX/resolve/main/tokenizer_config.json", "tokenizer_config.json"),
                ("https://huggingface.co/onnx-community/bge-m3-ONNX/resolve/main/special_tokens_map.json", "special_tokens_map.json"),
                ("https://huggingface.co/BAAI/bge-m3/resolve/main/sentencepiece.bpe.model", "sentencepiece.bpe.model"),
                ("https://huggingface.co/onnx-community/bge-m3-ONNX/resolve/main/onnx/model_int8.onnx", "model.onnx"),
            ]
        },
        "models/bge-reranker-v2-m3": {
            "name": "bge-reranker-v2-m3",
            "files": [
                ("https://huggingface.co/onnx-community/bge-reranker-v2-m3-ONNX/resolve/main/config.json", "config.json"),
                ("https://huggingface.co/onnx-community/bge-reranker-v2-m3-ONNX/resolve/main/tokenizer.json", "tokenizer.json"),
                ("https://huggingface.co/onnx-community/bge-reranker-v2-m3-ONNX/resolve/main/tokenizer_config.json", "tokenizer_config.json"),
                ("https://huggingface.co/onnx-community/bge-reranker-v2-m3-ONNX/resolve/main/special_tokens_map.json", "special_tokens_map.json"),
                ("https://huggingface.co/BAAI/bge-reranker-v2-m3/resolve/main/sentencepiece.bpe.model", "sentencepiece.bpe.model"),
                ("https://huggingface.co/onnx-community/bge-reranker-v2-m3-ONNX/resolve/main/onnx/model_int8.onnx", "model.onnx"),
            ]
        }
    }
    
    for dir_path, info in models.items():
        os.makedirs(dir_path, exist_ok=True)
        print(f"=== Downloading {info['name']} into {dir_path} ===")
        for url, filename in info["files"]:
            target = os.path.join(dir_path, filename)
            download_file(url, target, f"{info['name']} -> {filename}")

if __name__ == "__main__":
    main()
