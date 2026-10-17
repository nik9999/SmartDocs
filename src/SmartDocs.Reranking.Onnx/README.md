# SmartDocs.Reranking.Onnx

Локальный Cross-Encoder реранкер на базе **ONNX Runtime** для RAG-стека **SmartDocs**.

## Назначение

Пакет предоставляет реализацию `ICrossEncoderModel` с использованием модели **`BAAI/bge-reranker-v2-m3`** — мультиязычного кросс-энкодера с высокой точностью для русского и английского языков.

Тяжелые нативные библиотеки (`Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers`) изолированы в этом пакете, чтобы базовый проект `SmartDocs.Reranking` оставался легким.

## Основные компоненты

* **`BgeOnnxCrossEncoderModel`**:
  * Реализует `ICrossEncoderModel` и `IDisposable`.
  * Использует токенизатор SentencePiece для построения структуры пар XLM-RoBERTa: `[<s>] query [</s></s>] doc [</s>]`.
  * Выполняет пакетную обработку (`batchSize = 16`, `maxLength = 512`) с генерацией тензоров `input_ids` и `attention_mask`.
  * Преобразует выходные логиты в вероятности релевантности через функцию Sigmoid: $\text{score} \in [0, 1]$.

## Регистрация в DI

```csharp
services.AddSmartDocsOnnxCrossEncoder(
    onnxModelPath: "models/bge-reranker-v2-m3/model.onnx",
    sentencePieceModelPath: "models/bge-reranker-v2-m3/sentencepiece.bpe.model",
    maxLength: 512,
    batchSize: 16);
```
