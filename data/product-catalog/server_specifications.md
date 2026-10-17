# GPU Server Infrastructure Catalog (2026)

## 1. High-Density Compute Overview
Enterprise compute configurations optimized for large-scale multilingual embedding generation, RAG reranking, and LLM fine-tuning.

## 2. Technical Specifications of GPU Accelerator Nodes

| Server Model | Accelerators | GPU Memory | Interconnect | Target Workload |
| --- | --- | --- | --- | --- |
| DGX-H100 | 8x NVIDIA H100 SXM5 | 640 GB HBM3 | NVLink 900 GB/s | LLM Pre-training & BGE-M3 Dense Indexing |
| HGX-L40S | 8x NVIDIA L40S PCIe | 384 GB GDDR6 | PCIe Gen 5 | Multimodal Vision Captioning & Serving |
| RT-4090-Node | 4x RTX 4090 24GB | 96 GB GDDR6X | PCIe Gen 4 | Edge Inference & Embedding Extraction |

## 3. Storage and Ingestion Throughput
All nodes feature NVMe-oF low latency storage with up to 25 GB/s sequential read for rapid multi-gigabyte document parsing.