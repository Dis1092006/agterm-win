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

## Фаза 2 (control plane)

- **AppStore и потоки.** Upstream `AppStore` изолирован на `@MainActor`; в порте класс
  так же НЕ потокобезопасен, но actor-а в .NET нет — хост обязан сериализовать доступ
  сам (headless-хост и pipe-сервер оборачивают диспетчеризацию в один замок).
  `Debouncer` и грейс-таймеры pending-close срабатывают на пуле потоков и вызывают
  колбэк напрямую (upstream — через main queue); отложенное действие само отвечает
  за маршалинг.

- **Auto-follow re-arm без Observation.** Upstream перевзводит idle-таймер через
  `withObservationTracking` над `attentionSessions` (отложенно, с коалесингом на
  runloop). В порте нет Observation/runloop: `SetAgentIndicator` (единственная точка
  мутации attention-набора) перевзводит дебаунсер напрямую, когда auto-follow включён.
  Семантика та же: изменение attention-набора при включённой функции даёт блоку тот же
  idle-грейс, что и нажатие клавиши. `NotificationCenter`-уведомления
  (`.agtermAutoFollowed`, `.agtermSidebarVisibilityChanged`) заменены C#-событиями
  `AppStore.AutoFollowed` / `AppStore.SidebarVisibilityChanged`.

- **Транспорт: named pipe вместо unix-сокета (спец. §5).** Файлового пути у канала
  нет — имя пайпа детерминированно выводится из ТОГО ЖЕ сокет-пути
  (`ControlResolve.SocketPath`, `<state dir>\agterm.sock`):
  `agterm-<sha256(path)[0..12] hex>`, т.е. изоляция по `%AGTERM_STATE_DIR%`
  сохраняется. `%AGTERM_CONTROL_SOCKET%` остаётся явным override-ом и содержит
  ЛИТЕРАЛЬНОЕ имя пайпа (без префикса `\\.\pipe\`). Семантика соединения — как в
  upstream: одно соединение = один запрос, newline-JSON, лимит строки 1 MiB, per-read
  таймаут ~5 с + общий дедлайн ~10 с на чтение и запись, серийный accept-loop
  (один инстанс пайпа). ACL: `PipeOptions.CurrentUserOnly` — аналог owner-only
  `chmod 0600` на сокет-файле. «Идемпотентность старта»: у пайпа нет устаревшего
  файла, который нужно unlink-ать — повторный `Start()` просто no-op.
  Строки ошибок транспорта закреплены: `"request too large or read failed"`,
  `"invalid request: <сообщение декодера>"` (префикс — контракт, суффикс
  платформозависим; upstream пишет `localizedDescription` Foundation),
  `"debug.appearance is a UI-test-only seam"` для единственной команды, которую
  диспетчер отклоняет (UI-тестовый шов вне паритета).

- **`RecentClosedStore`.** Локальный файл `recent-closed.json`: имена полей 1:1,
  `closedAt` кодируется как double секунд от reference date 2001-01-01 (формат
  Swift `JSONEncoder` по умолчанию) для кросс-читаемости; upstream пишет
  prettyPrinted+sortedKeys, мы — компактно и атомарно (как `PersistenceStore`).
