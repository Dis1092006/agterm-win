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

- **Headless-хост (`HeadlessHost`/`HeadlessControlActions`).** Полная реализация
  `IControlActions` поверх ОДНОГО `AppStore` (single-window; upstream-резолвер целей
  свёрнут на один стор, wire-строки ошибок не тронуты). Расхождения, продиктованные
  отсутствием окон/терминального движка:
  - `window.new/close/resize/move/zoom/fullscreen` → `"window management is not
    available in the headless host"` (после резолюции цели — `no such window`/prefix-
    ошибки закреплены); `window.select` единственного окна → ok+id; `window.rename`
    работает; `window.delete` → upstream-точное `"cannot delete last window"`.
    `window.list`: `geometry`/`fullscreen`/`zoomed` опущены.
  - Терминальные армы идут через шов `IHeadlessSurface` (фейковые поверхности E2E);
    realize-поллинги (~360 мс) заменены одиночной проверкой; строки ошибок
    (`session not realized; use select`, `quick terminal not open`, `no selection`
    и т.д.) — из upstream.
  - `session.status --sound` принимается, но не проигрывается и не валидируется;
    `notify` всегда идёт banners-off путём (`result.text` = BannersOffNote);
    `session.search` считает совпадения по буферу фейка синхронно;
    `session.reveal` выбирает сессию (без файлового менеджера), gate на
    существование cwd сохранён; `config.reload` — инжектируемый счётчик (движка
    нет); батч-грейс `session.close` по умолчанию выключен (таймер финализации
    жил бы вне диспетчер-лока), включается опцией.
  - Бутстрап как у приложения: загрузка снапшота из state dir (armed
    launchRestore), пустой стейт → дефолтный воркспейс + сессия в home.

- **`agtermctl` (AgTerm.Cli).** Субкоманды/флаги/валидация/вывод — 1:1 с
  agtermctlKit; парсер — System.CommandLine вместо swift-argument-parser
  (шов `ParseAsRoot`/`MakeRequest`/`Validate` сохранён для портированных тестов).
  Разрешение socket-пути: `--socket` → `%AGTERM_STATE_DIR%\agterm.sock` →
  `%LOCALAPPDATA%\agterm\agterm.sock` → `<temp>\agterm\agterm.sock` (аналог
  upstream `$HOME/Library/Application Support` → `/tmp`); из пути выводится имя
  пайпа (см. запись о транспорте). `session type --stdin`: .NET-декодер stdin
  заменяет невалидный UTF-8 замещающими символами (Swift даёт nil/пусто) — на
  wire уходит то, что прочиталось. Help-текст `window fullscreen` не упоминает
  macOS (help — не часть wire-контракта).

## Фаза 3 (терминальный движок)

- **Дефолтный шелл вместо login-shell.** На Windows нет `$SHELL` и login-shell-семантики
  (upstream спавнит login-шелл пользователя). Политика порта (`ShellResolver`): явная
  настройка → вербатим; иначе PowerShell 7 (`pwsh.exe` с PATH); иначе `%COMSPEC%`;
  иначе литеральный `cmd.exe`. На wire не влияет.

- **Токенизация `session.new --command`.** Upstream отдаёт строку конфиг-парсеру libghostty.
  Здесь токенайзер свой (`CommandTokenizer`), контракт §6 сохранён (кавычко-aware split,
  argv[0] exec-ится напрямую, операторы шелла НЕ интерпретируются): whitespace разделяет,
  `'...'` группирует литерально, `"..."` группирует с эскейпами `\"` и `\\`, backslash вне
  кавычек — ОБЫЧНЫЙ символ (голые `C:\пути` обязаны выживать), незакрытая кавычка берёт
  хвост литерально. Argv →
  командная строка CreateProcessW — точная инверсия правил CommandLineToArgvW
  (`WindowsCommandLine`).

- **ConPTY-спавн и std-хэндлы.** `CreateProcessW` без `STARTF_USESTDHANDLES` дублирует
  (возможно перенаправленные) std-хэндлы хоста в ребёнка, и вывод ребёнка обходит
  псевдоконсоль (уходит в stdout хоста). Порт передаёт `STARTF_USESTDHANDLES` с нулевыми
  хэндлами (воркараунд Windows Terminal ConptyConnection) — conhost подменяет их хэндлами
  самой псевдоконсоли.

- **Реализация поверхностей (замена Observation/SwiftUI-рендера).** Upstream реализует
  поверхность, когда SwiftUI отрисовывает показанный пейн. В порте у стора есть шов
  `AppStore.SurfaceRealizer` (тот же прецедент, что auto-follow re-arm): оба пути «показа»
  (SelectSession и AddSession select:true) зовут его для главного пейна без поверхности;
  фоновое создание (--no-select) остаётся нереализованным — паритетная ошибка
  `session not realized; use select` сохраняется. Restore() выставляет выбор мимо
  SelectSession, поэтому хост после бутстрап-restore зовёт `RealizeActiveSession()`
  (аналог первой отрисовки окна). Событие `AppStore.SelectionChanged` — замена
  Observation над selectedSessionID для хрома окна.

- **LiveControlHost (приложение).** Control-plane headless-хоста + реальные поверхности
  (`XtermPaneSurface`): реализация блокирует диспетчер-поток до ~15 с (upstream-аналог
  realize-поллинга); UI-half (`OpenSurface`) создаёт WebView2-пейн на UI-потоке. Правило
  потоков: UI-поток НИКОГДА не берёт хост-гейт (иначе дедлок против блокирующей
  реализации) — колбэки поверхностей (OSC title/cwd, exit) прыгают в пул и мутируют
  сессию под `RunLocked`. Выход шелла главного пейна → `ClosePrimaryPane` (upstream-путь);
  сессия с `--command --wait` остаётся открытой. quick/scratch/split/overlay-поверхности
  в фазе 3 не реализуются — армы отвечают закреплёнными ошибками. Снапшот пишется на
  выходе (Dispose → Save + teardown всех панелей).

- **`session.text` и мягкие переносы.** Чтение буфера склеивает soft-wrapped строки xterm
  в логические (иначе обёрнутая длинная команда читается двумя «строками»), `--lines N` —
  последние N строк КОНТЕНТА (хвостовые пустые ряды вьюпорта отбрасываются). Плоский текст
  берётся из buffer API (`translateToString`), НЕ из serialize-аддона: тот эмитит
  VT/SGR-последовательности, а wire-контракт `session.text` — plain text (serialize-аддон
  забандлен и используется хостом для сериализации экрана, не для session.text).

## Фаза 4 (сайдбар и мультисессии)

- **Один WebView2 на окно, много xterm.js-пейнов (мост v2).** Фаза 3 держала WebView2 на
  каждую сессию (~100+ МБ рантайма и медленный `EnsureCoreWebView2Async` на каждую).
  Теперь окно грузит `terminal.html` один раз; `bridge.js` держит реестр пейнов по
  `paneId`, каждое сообщение обеих сторон и каждый `agterm.*`-вызов адресованы пейном
  (`XtermPage` — C#-владелец страницы, `XtermTerminal` — per-pane хэндл). Переключение
  сессий = show/hide div-ов (`agterm.showPane`), а не Visibility WebView2. `PaneToken`
  поверхности (`AGTERM_PANE_ID`) и `paneId` моста — один и тот же идентификатор.
  На wire не влияет.

- **Eager deck в live-хосте.** Upstream-приложение реализует вью каждой сессии с момента
  её появления в деке, поэтому `session.type` попадает в фоновую сессию без `select`.
  Порт: `AppStore.EagerSurfaceRealization` (включает live-хост) — `AddSession` реализует
  и `--no-select`-создания, бутстрап после рестора зовёт `RealizeAllSessions()`.
  Headless-хост флаг НЕ включает: там поверхности навешиваются тестами извне, и
  паритетная ошибка `"session not realized; use select"` сохраняется как есть.
