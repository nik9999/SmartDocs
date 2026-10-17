# SmartDocs.GigaChat.Tests

Набор модульных тестов для библиотеки **SmartDocs.GigaChat**.

## Описание тестов

* **Аутентификация и OAuth 2.0 (`GigaChatAuthTests`)**:
  * Корректность кодирования авторизационных заголовков Base64.
  * Формирование уникальных идентификаторов `RqUID` на каждый запрос.
  * Кэширование токена доступа и его автоматическое обновление при приближении к `expires_at`.
  * Валидация областей видимости (`Scope`: Personal, Corporate, B2B).
* **Генерация ответов (`GigaChatChatClientTests`)**:
  * Синхронная генерация через `GetResponseAsync`.
  * Потоковая передача чанков (Server-Sent Events) через `GetStreamingResponseAsync`.
  * Трансляция ролей сообщений (`System`, `User`, `Assistant`, `Tool`).
  * Function Calling: сериализация описаний функций и десериализация вызовов `function_call`.
* **Эмбеддинги (`GigaChatEmbeddingGeneratorTests`)**:
  * Векторизация строк через `IEmbeddingGenerator`.
  * Проверка размерности и нормализации векторов.
* **Конфигурация (`GigaChatOptionsTests`)**:
  * Привязка секций конфигурации из `appsettings.json`.

## Запуск тестов

```bash
dotnet test tests/SmartDocs.GigaChat.Tests/SmartDocs.GigaChat.Tests.csproj
```
