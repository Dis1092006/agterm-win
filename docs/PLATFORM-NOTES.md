# PLATFORM-NOTES

Документированные адаптации и расхождения Windows-реализации с upstream agterm
(политика паритета — см. README.md). Каждая запись: что, почему, как ведёт себя на wire.

## Фаза 1 (AgTerm.Core)

- **`Session.DisplayName` basename и разделители путей.** Upstream использует
  `NSString.lastPathComponent` (только `/`). Windows-порт принимает оба разделителя
  (`/` и `\`): пины спецификации сохранены (`/` → `/`, `/a/b/` → `b`, `""` → `~`),
  дополнительно `C:\git\proj` → `proj`. На wire-протокол не влияет — это только
  вычисление метки сайдбара.

- **`PersistenceStore`.** Формат файла тот же JSON-снапшот (имена полей байт-в-байт),
  но upstream пишет prettyPrinted+sortedKeys, мы — компактный JSON без сортировки
  ключей. Контракт — имена/типы полей и правила декода, а не байтовый вид локального
  файла. Каталог по умолчанию: `%AGTERM_STATE_DIR%`, иначе `%LOCALAPPDATA%\agterm`
  (аналог `~/Library/Application Support/agterm`, спец. §3).
