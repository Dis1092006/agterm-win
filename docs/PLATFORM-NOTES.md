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
  файла.

- **Каталог состояния: `~\.agterm-win`** (вместо спец. §3 `%LOCALAPPDATA%\agterm`).
  Порядок: `%AGTERM_STATE_DIR%` → `%USERPROFILE%\.agterm-win` → `<temp>\.agterm-win`
  (последний — аналог upstream-фолбэка `нет $HOME → /tmp`). Имя каталога отличается
  от upstream-ового `agterm`: из пути выводится имя пайпа, и при совпадающих именах два
  разных приложения на одной машине делили бы состояние и канал управления. Корень —
  домашний каталог, как у CLI-инструментов, которыми приложение драйвится (`agtermctl`).
  На wire не влияет: `--socket` и `AGTERM_STATE_DIR` работают как раньше.
  `PersistenceStore.RealUserDirectory` и `AgtermctlOptions.SocketPath` обязаны выводить один
  и тот же путь — иначе CLI не найдёт приложение.

- **Config dir лежит рядом с состоянием** (вместо спец. §3 `%APPDATA%\agterm`).
  `keymap.conf` ищется там, куда указывает `AppSettings.configDirectory`, иначе в state dir,
  то есть по умолчанию `~\.agterm-win\keymap.conf`. `%APPDATA%` не используется нигде:
  всё состояние и конфиг приложения живут в одном каталоге, как у CLI-инструментов.

- **Гард на дефолтный каталог под тестами.** `PersistenceStore.DefaultDirectory` бросает
  `InvalidOperationException`, если к нему обращаются из тестового хоста без
  `AGTERM_STATE_DIR`: тест, дошёдший до неявного дефолта, перезаписал бы реальное
  состояние разработчика. `RealUserDirectory` гард обходит — он нужен для вывода
  имени пайпа (чистая строка, без обращения к диску), и читать/писать состояние
  через него нельзя.

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
  `%USERPROFILE%\.agterm-win\agterm.sock` → `<temp>\.agterm-win\agterm.sock` (аналог
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

- **`AppStore.SidebarChanged` (Observation-замена для сайдбара).** Коарсовое C#-событие
  «что-то в строках сайдбара изменилось» по прецеденту `SelectionChanged` (фаза 3):
  эмитится из `Save()` (любая персистируемая мутация) плюс из несохраняющих путей
  (selection до дебаунса, индикаторы, unseen). Публичный `NotifySidebar()` — для
  OOB-инвалидаций хоста (живые OSC title/cwd, unseen-бамп notify-арма). Кольцо событий
  НЕ тронуто: flag/collapse/focus/mode по-прежнему не эмитят `tree.changed` (паритет wire).
  UI-слой: `SidebarController` снимает `SidebarProjection`-снапшот ПОД гейтом и
  маршалит в UI; жесты сайдбара зовут те же мутаторы стора, что socket-армы
  (третий вызывающий, как upstream toolbar/menu/control).

- **Маппинг модификаторов keymap на Windows (спец. §9: «cmd → Win/Ctrl по вкусу»).**
  Принято: `cmd`→**Ctrl**, `ctrl`→Ctrl, `opt`→Alt, `shift`→Shift. Так как два
  keymap-модификатора делят одну физическую клавишу, нажатие декодируется в КАНДИДАТОВ
  по приоритету: cmd-интерпретация → ctrl → свёртка ctrl+cmd; побеждает первый, знакомый
  матчеру (`WindowsChordDecoder` + `HotkeyRouter`).

  **Дефолты чордов на Windows свои (`WindowsDefaultChords`).** Свёртка `cmd`→Ctrl сажала 13
  действий на голый Ctrl, а в терминале Windows голый Ctrl принадлежит оболочке: Ctrl+D
  завершает ввод, Ctrl+Z приостанавливает, Ctrl+W удаляет слово, Ctrl+P листает историю.
  Приложение, которое их перехватывает, ломает не удобство, а программу в панели. Поэтому
  каждое действие, попадающее на голый Ctrl, шипается на Alt с той же клавишей:
  `new_session` → Alt+N, `toggle_split` → Alt+D, `session_palette` → Alt+P и так далее.

  Критерий — куда чорд попадает физически, а не какой модификатор стоял в таблице macOS:
  пользователь исходного модификатора не видит, а на клавиатуре без клавиши Command он
  ничего не описывает. Всё, что несёт Shift или Alt (`Ctrl+Shift+…`, `Ctrl+Alt+…`), остаётся
  на месте: оболочка такие сочетания не читает, а каждое совпадение с macOS — это на одно
  сочетание меньше переучивать.

  `BuiltinAction.DefaultChord` при этом не тронут и по-прежнему сверяется с
  `parity/surface.json`; `WindowsDefaultChords` — слой над ним, там же, где уже живёт свёртка
  `cmd`→Ctrl. Строка `map` в keymap.conf перекрывает и то, и другое. Это расхождение с
  оригиналом: в `Ported/KeymapTests.cs` изменённые ожидания помечены построчно, чтобы файл
  по-прежнему можно было сверять со Swift.

  Побочный эффект: `toggle_fullscreen` стал достижим. Раньше ⌘F (`toggle_search`) и ⌃⌘F
  (`toggle_fullscreen`) оба сворачивались в Ctrl+F, побеждал первый, и полноэкранный режим
  не вызывался с клавиатуры вообще. Теперь это Alt+F и Alt+Enter.

  Остались две коллизии дефолтов (затенённое действие достижимо через `map`):
  | Физически | Побеждает | Затенён |
  |---|---|---|
  | Ctrl+Alt+↑ | `previous_session` (⌘⌥↑) | `previous_attention_session` (⌃⌥↑) |
  | Ctrl+Alt+↓ | `next_session` (⌘⌥↓) | `next_attention_session` (⌃⌥↓) |
  Обе несут Alt, оболочке не мешают, поэтому оставлены как есть.

  Перехват в терминале: страница получает таблицу активных физ-чордов
  (`agterm.setChords`); совпавший keydown НЕ уходит в шелл, а возвращается хосту
  `{type:'chord',…}` (xterm `attachCustomKeyEventHandler`). Ни один голый Ctrl-чорд в эту
  таблицу не попадает. Полная таблица действий — ниже и в README.

- **Custom-command shell and chord adaptation.** Upstream executes each custom command as an
  opaque `/bin/sh -c` line. Windows resolves `pwsh.exe` from `PATH` first and runs
  `-NoLogo -NoProfile -NonInteractive -Command <line>`; when PowerShell 7 is unavailable it
  falls back to `%COMSPEC% /d /c "<line>"`. The cmd fallback uses its command-string quoting
  rule so quotes already written by the command author around Windows paths remain part of the
  shell line. The runtime does not tokenize the line or add quotes around `{AGT_X}` expansions.
  It exports the same ten `AGT_*` values, with `AGT_SOCKET` containing the literal named-pipe
  override used by `agtermctl`, and uses the active session cwd when one exists. A sessionless
  keybind may run only when its command body does not reference session, workspace, or selection
  values; the palette path still requires an active session. The shared WebView2 receives one
  generation-stamped manifest containing active built-ins and parsed custom single/leader chords.
  Replacement is atomic and cancels any armed leader; stale generation events no-op in the host.
  A 1.5-second leader timeout and Escape abandon the prefix, and an unmatched continuation is
  retried at the root before it is returned to the terminal.

- **Sidebar folder import.** Explorer directory drops use the WinUI storage-item format and a
  copy operation. Drops are accepted only in tree mode. A row drop targets that row's workspace;
  empty space falls back to the focused workspace and then the current workspace. Non-directory
  items are ignored, source order is preserved, and one session is created for each of at most 20
  accepted directories. Spring-hover expansion of a collapsed workspace remains a Windows
  adaptation: WinUI drag-and-drop does not expose a stable hover-expand lifecycle, and adding a
  separate timer would conflict with ListView reorder feedback.

- **Close and flagged confirmations.** GUI session closes honor `confirmCloseSession`; single and
  multi-session dialogs snapshot immutable targets, run outside the store and application gates,
  and revalidate the complete target set before applying the close. A stale multi-session target
  aborts the whole operation. Clear Flagged always confirms a non-empty bulk change and removes
  flags only from the sessions shown by that prompt. The named-pipe control API does not show GUI
  dialogs and keeps its existing response semantics.

- **Close undo and recent items.** `closeGraceUndoEnabled` defaults to enabled, matching upstream.
  A per-window InfoBar presents the current three-second pending close and undoes its exact ID.
  Grace expiry runs through the owning host gate, and window shutdown finalizes hidden pending
  records while still awaiting their exact terminal surfaces. Persistent recent items are shared
  by all application windows; Ctrl+Shift+T restores the latest item into the invoking window, and
  concurrent windows cannot consume the same item twice.

- **Live failure presentation.** Terminal spawn and WebView2 initialization failures, settings
  reload failures, and live theme apply failures use one closable in-window error InfoBar. The
  existing failed-terminal row state remains visible. Failure presentation is UI-only: surface,
  config, and theme wire responses and error strings are unchanged.

## Phase 4.5: live orchestration

- **Bounded terminal output batching.** Each live pane routes ConPTY output through one
  bounded channel before it reaches WebView2. `OutputQueueMaxBytes` is 1,048,576 bytes,
  the item limit is 256 chunks, `OutputBatchMaxBytes` is 65,536 bytes, and
  `OutputFlushInterval` is 8 ms. The ConPTY reader waits when either queue limit is
  reached, so pipe backpressure reaches the child instead of growing the managed UI
  queue. The managed-memory bound also includes one reusable batch buffer and at most one
  carried chunk suffix, so the default peak is about 1,179,648 bytes
  (`OutputQueueMaxBytes + 2 * OutputBatchMaxBytes`). Normal process exit waits for reader
  EOF and the final renderer batch; teardown cancels blocked producers and a renderer
  callback that can no longer complete.
- **Output-pump measurement (2026-08-26, Debug, .NET SDK 10.0.201).** A deterministic
  10 MiB stream completed with the expected SHA-256 at 268.2 MiB/s. It used 160 renderer
  flushes, blocked the producer 55 times, and reached 1,048,576 maximum queued bytes.
  These values record the development-machine stress run; the limits above are the
  implementation contract, not the measured throughput.
- **Ordered live shutdown.** The first WinUI close request is canceled while the live host
  stops pipe acceptance, cancels and awaits active or queued surface realization, stops
  every output pump, saves the snapshot, terminates ConPTY children, and awaits renderer
  removal while WebView2 is still alive. The final close re-enters once after shutdown.
  Surface and renderer waits use a 5-second budget. Shutdown-triggered process exits do not
  remove sessions from the store or snapshot; a five-session live test verifies that all
  children exit, all pane IDs disappear from the page, and all five sessions remain persisted.
- **Windows list/details shell.** The live window uses the Windows App SDK 1.7 `TitleBar`
  above a `SplitView`: the custom multi-select sidebar remains the pane and the shared
  WebView2 remains the content. The pane is inline from 720 effective pixels and overlays
  below that breakpoint. Its persisted width remains clamped to 160..560 pixels; the 4-pixel
  drag target overlays a 1-pixel system divider and saves only on pointer release. The title
  bar shows the accepted session name and owning workspace from one immutable layout
  generation. The shell uses `MicaBackdrop` plus WinUI system theme resources, which provide
  the solid fallback and high-contrast resource variants without a separate color palette.

- **S2 sidebar gestures, keyboard parity, and live-only status projection.** Workspace row tap, Enter, and Space now toggle collapse immediately with no 350 ms delay. Workspace double-click has no rename action. Rename is exposed through F2 and the workspace or session context menu. Session single-click and Enter activate the selected session. Ctrl and Shift modifiers keep standard Extended multi-selection behavior, and Space keeps ListView selection semantics. Keyboard ContextRequested opens the same workspace and session menus as pointer context actions. SidebarSnapshot now carries immutable live-only surface status values (pending, ready, failed) so the UI reports terminal lifecycle without changing Core `SidebarRow` shape, tree JSON, or wire protocol fields. Session rows show pending and failed visual indicators from that status projection. UI Automation names now cover workspace and session selection state, lifecycle status text, agent status text, unseen count text, focused-workspace state, flagged-toggle state, and the new-workspace action. Manual Narrator verification is not claimed yet.

- **S3 capability-aware command routing.** The WinUI window classifies all 41 upstream builtin actions in one router, and every action has an explicit handler. Actions without a default chord remain available through the title bar, sidebar, context menus, or palettes as applicable. These entry points and hotkeys use the same router or the same selection-aware `SidebarController` store operation. xterm.js receives only physical chords whose effective Windows winner has an explicit handler. The cmd-to-Ctrl adaptation keeps upstream keymap grammar and reports effective physical conflicts as release-visible keymap warnings. Windows ships its own default chord for every action the fold would park on bare Ctrl (`WindowsDefaultChords`), so two default conflicts remain: Ctrl+Alt+Up (`previous_session` over `previous_attention_session`) and Ctrl+Alt+Down (`next_session` over `next_attention_session`). This changes only Windows input routing and diagnostics; builtin names, configuration parsing, control commands, and wire responses are unchanged.

- **S4 renderer settings and Windows composition.** One immutable snapshot maps the existing app settings into page-global xterm options and applies them to every current and future pane without recreating ConPTY sessions. It covers font family and size, the current Windows base palette, cursor defaults, scroll sensitivity, scrollback, right-click paste, and screen-reader mode. Named theme resolution remains in the D3 live theme catalog; S4 removes bridge-local color literals without implementing `theme.set` early. `backgroundOpacity` stays a window-composition setting: opaque windows use Mica, translucent windows use a transparent WebView2 background plus Desktop Acrylic when blur is nonzero, and no system backdrop when blur is zero. Windows App SDK controls the acrylic blur strength, so the persisted 0..100 value gates blur but does not map to an exact radius. `config.reload` reads settings off the host gate, drops stale loads, serializes UI application, updates existing/future panes and sidebar typography/status styling, and clears per-session font zoom as upstream explicit reload does. The optional WebGL addon is capability-detected but is not vendored in this pin; xterm's built-in renderer remains the tested fallback. Live tests verify Unicode, IME composition, selection, and screen-reader DOM behavior. No settings, control-command, tree, or wire field names changed.

- **Live command overlay shell adaptation.** Upstream runs `session.overlay.open` command text through `/bin/sh -c`. Windows runs the same opaque shell string through `%COMSPEC% /c`; the host does not split or reinterpret it before `cmd.exe` receives it. This preserves shell operators and the wrapper process reports the command's `%ERRORLEVEL%` through ConPTY, so `session.overlay.result` and CLI `--block` return the real exit code without a temporary code file. This does not change `session.new --command`, which keeps its direct-argv contract and does not interpret shell operators.

- **D3 themes and notifications.** The live Windows theme catalog contains `agterm`, `Builtin Light`, and `Dracula`. Upstream discovers additional Ghostty themes from libghostty's bundled resource directory; xterm.js has no equivalent resource catalog, so those extra names are not advertised by `theme.list` on Windows. The three Windows entries carry full 16-color ANSI palettes and apply to existing and future panes. `theme.set` keeps the upstream two-slot transitions and exact unknown-theme errors, persists the slots in `settings.json`, follows Windows light/dark appearance, and derives WinUI chrome brightness from the active background. Theme-only appearance changes preserve per-pane font sizes, dashboard temporary fonts, and overlay background overrides.

- **D3 notification presentation and sounds.** `notify` uses Windows App SDK app notifications when registration and display are available. An in-window `InfoBar` is the fallback for elevated execution, policy-disabled system notifications, or registration failure. Like upstream's explicit control-command path, it does not suppress a banner for the focused session; the separate terminal OSC path has that suppression and is not part of this control adapter. The unseen count and `notificationBadgeEnabled` remain independent of banner delivery: the count always changes, while the setting only gates sidebar rendering. The spec names `default` and `beep` as aliases for the system sound; Windows app notifications use their default audio, and the fallback/status path uses `MessageBeep`. Status playback suppresses the same sound name within 200 ms without delaying a different name. Other macOS named sounds remain accepted on the wire, are not played, and produce a diagnostic instead of claiming successful playback. `session.status --auto-reset` remains a selection-time Core transition, not a timer.

- **W2 live window control.** The live application implements all ten `window.*` commands through one application pipe while the single-window headless adaptations above remain unchanged. `window.list` includes every persisted record in index order and obtains geometry and presenter flags only from open WinUI windows. Move/list coordinates are logical points relative to the selected display's full top-left bounds, with y increasing downward; monitor-specific effective DPI converts them to and from AppWindow pixels. Resize is clamped to the target display work area, and move keeps an 80-point strip visible. Windows App SDK normally terminates a WinUI 3 application after its last `Window` closes. The final live `window.close` therefore returns its id, persists the record as closed, and completes terminal teardown before the process exits; the next launch reopens the same persisted frontmost-or-first fallback record. This replaces upstream's same-process zero-window state with the native Windows lifecycle while preserving the durable window id, name, store, and close response.

## Phase 5: live feature completion

On Windows, `cmd` maps to Ctrl when an upstream default chord is decoded. `keyless` means that the action has no default chord, not that the action is unimplemented; keyless actions remain available through their UI, menu, or palette paths. Physical chord conflicts remain diagnosed according to the S3 capability-aware command routing note above.

<!-- phase5-action-matrix:start -->

| Builtin action             | Wire name                    | Disposition   | Upstream default        | Windows default        | Effective Windows      |
| -------------------------- | ---------------------------- | ------------- | ----------------------- | ---------------------- | ---------------------- |
| `NewWindow`                | `new_window`                 | `Implemented` | `cmd+opt+n`             | `cmd+opt+n`            | `ctrl+alt+n`           |
| `RenameWindow`             | `rename_window`              | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `DeleteWindow`             | `delete_window`              | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `NewWorkspace`             | `new_workspace`              | `Implemented` | `cmd+shift+n`           | `cmd+shift+n`          | `ctrl+shift+n`         |
| `RenameWorkspace`          | `rename_workspace`           | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `DeleteWorkspace`          | `delete_workspace`           | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `NewSession`               | `new_session`                | `Implemented` | `cmd+n`                 | `opt+n`                | `alt+n`                |
| `OpenDirectory`            | `open_directory`             | `Implemented` | `cmd+o`                 | `opt+o`                | `alt+o`                |
| `RenameSession`            | `rename_session`             | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `DuplicateSession`         | `duplicate_session`          | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `CloseSession`             | `close_session`              | `Implemented` | `cmd+w`                 | `opt+w`                | `alt+w`                |
| `ReopenRecent`             | `reopen_recent`              | `Implemented` | `cmd+shift+t`           | `cmd+shift+t`          | `ctrl+shift+t`         |
| `UndoClose`                | `undo_close`                 | `Implemented` | `cmd+z`                 | `opt+z`                | `alt+z`                |
| `ClearStatus`              | `clear_status`               | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `IncreaseFontSize`         | `increase_font_size`         | `Implemented` | `cmd++`                 | `opt++`                | `alt++`                |
| `DecreaseFontSize`         | `decrease_font_size`         | `Implemented` | `cmd+-`                 | `opt+-`                | `alt+-`                |
| `ResetFontSize`            | `reset_font_size`            | `Implemented` | `cmd+0`                 | `opt+0`                | `alt+0`                |
| `ToggleSplit`              | `toggle_split`               | `Implemented` | `cmd+d`                 | `opt+d`                | `alt+d`                |
| `ToggleScratch`            | `toggle_scratch`             | `Implemented` | `cmd+j`                 | `opt+j`                | `alt+j`                |
| `ToggleTerminalZoom`       | `toggle_terminal_zoom`       | `Implemented` | `cmd+shift+return`      | `cmd+shift+return`     | `ctrl+shift+return`    |
| `ToggleSearch`             | `toggle_search`              | `Implemented` | `cmd+f`                 | `opt+f`                | `alt+f`                |
| `ToggleSidebar`            | `toggle_sidebar`             | `Implemented` | `ctrl+cmd+s`            | `opt+s`                | `alt+s`                |
| `SelectTheme`              | `select_theme`               | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `ToggleFullscreen`         | `toggle_fullscreen`          | `Implemented` | `ctrl+cmd+f`            | `opt+return`           | `alt+return`           |
| `ToggleFlaggedView`        | `toggle_flagged_view`        | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `ToggleFlag`               | `toggle_flag`                | `Implemented` | `cmd+shift+f`           | `cmd+shift+f`          | `ctrl+shift+f`         |
| `FocusWorkspace`           | `focus_workspace`            | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `FocusLeftPane`            | `focus_left_pane`            | `Implemented` | `cmd+opt+left`          | `cmd+opt+left`         | `ctrl+alt+left`        |
| `FocusRightPane`           | `focus_right_pane`           | `Implemented` | `cmd+opt+right`         | `cmd+opt+right`        | `ctrl+alt+right`       |
| `PreviousSession`          | `previous_session`           | `Implemented` | `cmd+opt+up`            | `cmd+opt+up`           | `ctrl+alt+up`          |
| `NextSession`              | `next_session`               | `Implemented` | `cmd+opt+down`          | `cmd+opt+down`         | `ctrl+alt+down`        |
| `PreviousAttentionSession` | `previous_attention_session` | `Implemented` | `ctrl+opt+up`           | `ctrl+opt+up`          | `ctrl+alt+up`          |
| `NextAttentionSession`     | `next_attention_session`     | `Implemented` | `ctrl+opt+down`         | `ctrl+opt+down`        | `ctrl+alt+down`        |
| `FirstSession`             | `first_session`              | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `LastSession`              | `last_session`               | `Implemented` | `keyless`               | `keyless`              | `keyless`              |
| `QuickTerminal`            | `quick_terminal`             | `Implemented` | <code>ctrl+&#96;</code> | <code>opt+&#96;</code> | <code>alt+&#96;</code> |
| `SessionPalette`           | `session_palette`            | `Implemented` | `ctrl+p`                | `opt+p`                | `alt+p`                |
| `CommandPalette`           | `command_palette`            | `Implemented` | `ctrl+shift+p`          | `ctrl+shift+p`         | `ctrl+shift+p`         |
| `CustomCommandPalette`     | `custom_command_palette`     | `Implemented` | `ctrl+shift+o`          | `ctrl+shift+o`         | `ctrl+shift+o`         |
| `ShowAttention`            | `show_attention`             | `Implemented` | `ctrl+shift+i`          | `ctrl+shift+i`         | `ctrl+shift+i`         |
| `Dashboard`                | `dashboard`                  | `Implemented` | `cmd+shift+d`           | `cmd+shift+d`          | `ctrl+shift+d`         |

<!-- phase5-action-matrix:end -->

### Phase 5 release-gate evidence

The U2 live scenario runs two windows through one application pipe, with one WebView2 page per window. It covers two workspaces per window, background sessions, split, scratch, overlay, Quick Terminal, search, palette selection, a palette-only custom command, zoom, dashboard, theme propagation, notification routing, restart, and event-run rollover. The scenario waits for every ConPTY pane and the exact custom-command process before it disposes both pages.

The 2026-09-03 automated gate passed 849 Xterm tests and the full solution passed 2,471 tests with 26 documented skips. WinUI builds passed for x64 and ARM64. The live x64 check used two native windows and three real PowerShell ConPTY children. It verified exact second-window geometry `(100, 50, 900, 700)`, native fullscreen on/off with geometry restoration, Dracula theme application, notification routing to a background session, named and keyboard-focusable UI Automation peers, one window remaining responsive after the other closed, final process exit, and cleanup of all seven captured direct child processes. The two live windows ran at 100% and 150% DPI.

The 2026-09-04 manual gate passed every required x64 and ARM64 row with no failed or blocked result. The checks covered physical keyboard, mouse, and touchpad workflows; Narrator; real IME composition; 100%, 150%, and 200% DPI; wide and narrow layouts; high contrast; fullscreen; Explorer folder drop; confirmation dialogs and native InfoBars; two-window behavior; restart persistence; crash-free final close; and orphan-process cleanup. Together with the automated gate and architecture builds, this completes the Phase 5 release gate.

## Shell integration (cwd reporting)

Upstream never implements this. libghostty injects its own shell integration into bash, zsh, fish and elvish, so OSC 7 arrives for free, and agterm only turns parts of it off (`shell-integration-features = no-cursor,no-title` in `ghostty-defaults.conf`). A WebView2/xterm.js surface has no injector, and no Windows shell reports its working directory on its own.

Without a report `Session.CurrentCwd` stays null forever, `EffectiveCwd` falls back to `InitialCwd`, and every restored session reopens at the directory it was first created in. The port therefore ships its own per-shell scripts in `src/AgTerm.Xterm/Integration/`:

| Shell | Script | Attached as | Reports |
| --- | --- | --- | --- |
| `pwsh.exe`, `powershell.exe` | `integration.ps1` | `-NoExit -Command ". '<script>'"` | OSC 9;9 |
| `cmd.exe` | `integration.cmd` | `/k call "<script>"` | OSC 9;9 |
| `bash.exe` (Git Bash) | `integration.bash` | `--rcfile <script> -i` | OSC 7 |

`wsl.exe` is excluded deliberately: it runs another OS, where a Windows script path does not exist and a reported path would be a `/mnt/c` location this side cannot spawn in. Any other shell is `ShellKind.Unsupported` — spawned exactly as before, with no cwd tracking rather than an error.

Constraints the implementation is pinned to, each of which was a real failure first:

- **Only a bare shell argv is rewritten.** `LiveControlHost.CaptureRequest` puts a user's own program in the same `argv` for a captured foreground command, `session.new --command`, and a `session.restore` override. Injecting there would replace that program with a shell and break restore, so `ShellIntegration.Inject` rewrites a single-element argv naming a known shell and returns everything else untouched.
- **The existing prompt is wrapped, not replaced.** `$function:prompt` is captured as a value (resolving it by name inside the wrapper would recurse), and the user's prompt still renders. Verified against a live Oh My Posh profile. `integration.bash` appends to `PROMPT_COMMAND` for the same reason, and re-sources `~/.bashrc` because `--rcfile` replaces it.
- **No script emits OSC 0/1/2.** `Session.DisplayName` prefers `oscTitle` over the cwd basename, so an auto-title would overwrite every sidebar row with a path — the same reason upstream sets `no-title`.
- **The OSC 7 URI needs three slashes and a literal colon.** `file:///C:/git/x` parses; `file://C%3A%5Cx` puts the drive letter in the host position and is rejected, and `file:///C%3A/x` yields a leading-slash path. Measured against `CwdReport.Parse`.
- **The scripts must stay real files, and are found via the exe path.** They are marked `ExcludeFromSingleFile`, because a `PublishSingleFile` bundle would hide them from the shell that is handed their path. `ShellIntegration.ResolveScriptDirectory` therefore tries `Environment.ProcessPath`'s directory BEFORE `AppContext.BaseDirectory`: in this app's single-file publish the two differ — `BaseDirectory` is the bundle extraction directory under `%TEMP%\.net\AgTerm.App\<hash>\`, while the excluded scripts stay beside the exe. A plain console single-file app reports the two as equal, so this only reproduces in the real app. Existence is checked, so a layout missing the scripts degrades to a plain shell.

A `cd` still does not trigger a disk write (spec line 81: reports arrive on every prompt, so only structural mutations save). The directory survives an ordinary quit because window close and `LiveApplicationHost.Dispose` both save; a hard kill still loses cwd changes made since the last structural mutation.

## WebView2 process death

Upstream has no analogue. libghostty renders in the app's own process, so there is no renderer that can die while the app keeps running. This port draws every pane in a WebView2, which runs its terminals in separate browser processes, and those processes can exit on their own.

What made this worth handling: the `CoreWebView2` object stays alive after its process dies. `WinRtScriptHost` captures it once at boot (`MainWindow.BootPageAsync`) and `_pageTask` is a `Task` that completes once, so nothing re-reads either. Every later `ExecuteScriptAsync` throws. The observed result was an app that ran for two days, lost its renderer, and then reported "Terminal failed to start" on all five restored sessions, with no statement of the real cause and no recovery short of a restart.

`CoreWebView2.ProcessFailed` is now subscribed right after `EnsureCoreWebView2Async`, so it covers failures after a successful init. Init failure itself is already reported by `ReportPageInitializationFailureAsync`.

- **Only some kinds take the page down.** `WebViewProcessFailure.IsFatal` treats `BrowserProcessExited`, `RenderProcessExited` and `RenderProcessUnresponsive` as fatal. `GpuProcessExited`, `UtilityProcessExited`, `SandboxHelperProcessExited`, the two PPAPI kinds and `UnknownProcessExited` leave the page running, and WebView2 recovers those processes itself. `FrameRenderProcessExited` covers an out-of-process iframe, which this page does not host.
- **An unrecognized kind reads as survivable.** A newer runtime can name a kind this build does not know. Claiming the terminals are gone while they still work is the worse error.
- **The kind arrives as its enum name.** `AgTerm.Xterm` has no WinUI or WebView2 reference, and the mapping is the part worth testing without a browser, so `MainWindow` passes `ProcessFailedKind.ToString()`.
- **The fatal notice holds the banner.** `FailureBanner` is one `InfoBar` and the newest notice replaces the last, so the per-session spawn failures that follow a dead renderer would bury the one notice that explains them. `FailureNoticeGate` drops every notice after a fatal one, including a repeat of the fatal notice itself, because WebView2 can raise `ProcessFailed` more than once.

The panes stay dead until the app is restarted, which is what the notice says. Recovery would need a reload, a fresh `XtermPage`, and a way to reattach the running ConPTY sessions to new panes; none of that exists yet. The spawned shells do keep running until the app exits, so a restart reopens them at their saved directories.

## Keyboard protocol (kitty CSI u, win32-input-mode)

Upstream does not carry this. agterm draws on Ghostty, which implements the kitty keyboard protocol itself, so an app running inside upstream agterm can ask for CSI u encoding and get it. Here the engine is xterm.js, and 6.0.0 has no support for the protocol at all: every key arrives at the pty in its legacy form. Enter and Shift+Enter are both CR, and nothing downstream can tell them apart.

That is a feature gap in the engine, not a parity divergence: the control API, the keymap grammar and the builtin actions are untouched. It is recorded here because the fix pins a prerelease.

- **The core runs on a 6.1.0 beta.** `vtExtensions` (`kittyKeyboard`, `win32InputMode`) is the option that carries it, and 6.1.0-beta is the first release to have it. `src/AgTerm.Xterm/Assets/vendor/MANIFEST.md` holds the pins and explains why the addons sit at a lower beta number than the core.
- **Both extensions are off by default in the engine, and both are enabled here.** `buildTerminalOptions` sets them for every pane. `win32InputMode` (`DECSET 9001`) is the ConPTY-native way to report full key events and helps TUI input generally; it is independent of the kitty protocol, and the engine prefers it when an app enables both.
- **An app that never asks keeps the old encoding.** The protocol is opt-in over the wire: the engine answers `CSI ? u`, tracks the stack pushed by `CSI > flags u` and popped by `CSI < n u`, and reports CSI u only while a request is in effect. Enabling the option changes nothing for a program that does not use it. `XtermPageLiveTests.Shift_enter_reports_kitty_csi_u_while_the_keyboard_protocol_is_pushed` pins all three states: CR before the push, `ESC [13;2u` for Shift+Enter while pushed, CR again after the pop.
- **Host chord interception runs first and is unaffected.** `attachCustomKeyEventHandler` sees the DOM event before any encoding, so which chords reach the pty is still decided by the keymap, not by the protocol.

Claude Code is the case that motivated this: its newline-without-submit binding needs Shift+Enter to be distinguishable from Enter, and `claude /terminal-setup` cannot help, since it writes per-terminal config for a fixed list of known terminals and has no entry for this app. Supporting the protocol is not sufficient on its own, because the request has to come from the program: see the next section.

## Terminal identification for spawned shells

Upstream needs none of this. agterm runs on Ghostty, so a child process reads Ghostty's own `TERM=xterm-ghostty` and `TERM_PROGRAM=ghostty` and knows what it is talking to. Here the children are spawned by a WinUI app, and the app declared nothing: `AGTERM_PANE_ID` was the only variable added, so everything else was inherited from whatever started agterm-win. Launched from an IDE terminal, the shell reported that IDE. `TerminalIdentity` now declares the identification, and `ConPtySession.RemoveEnvironment` can drop an inherited variable instead of only overwriting it.

This matters beyond cosmetics because programs gate features on it. Claude Code requests the kitty keyboard protocol only for a terminal it recognizes, from a fixed list; for anything else it sends no request at all, and Shift+Enter stays CR no matter what the engine supports. So the engine change in the previous section only takes effect once the terminal identifies itself.

- **`WT_SESSION` is set to a fresh GUID per pane.** It announces a Windows terminal session, which is the honest claim for a ConPTY-backed terminal on Windows, and a real one issues a session id per tab. It is also the only identification that does not require a version: a program claiming to be Ghostty or iTerm gets compared against `TERM_PROGRAM_VERSION` for feature support, which would mean publishing a fake version number and keeping it plausible as those terminals release.
- **`TERM` is declared as `xterm-256color`.** What xterm.js implements. Inheriting left it describing a different emulator, or missing entirely when the app was launched from Explorer.
- **`TERM_PROGRAM` and `TERM_PROGRAM_VERSION` are removed, not overwritten.** Detection generally tests whether `TERM_PROGRAM` is set before falling through to Windows signals, and returns its value verbatim, so an inherited name wins over `WT_SESSION` no matter what else is declared. Removal is why `RemoveEnvironment` exists; an explicit extra of the same name still wins, so the two can be combined.
- **The cost is one performance optimization.** Claude Code skips its `DECSTBM` scrolling-region path when `WT_SESSION` is set and repaints more instead, presumably from conhost history. Correct either way, and it does not disable the progress reporting (`OSC 9;4`) that xterm.js has no support for — that is suppressed for Windows terminal sessions, which suits this port.

This is identification chosen to satisfy other programs' allowlists, so it is a claim about behaviour rather than provenance: any program reading `WT_SESSION` will conclude it is inside Windows Terminal. No divergence was found for the features involved, but the mechanism deserves the same scrutiny if a future program gates something else on the same variable.

## Assets must survive packaging, not only a build

The terminal page is not embedded in the assembly: WebView2 serves it with `MapVirtualHostToFolder(XtermAssets.Directory)`, which resolves to `AppContext.BaseDirectory\Assets` and needs real files on disk. Two separate things have to hold for that, and the published app satisfied neither for a while.

`PublishSingleFile` extracts the bundle to a directory under `%TEMP%\.net\<app>` and points `AppContext.BaseDirectory` there, not at the folder holding the exe. So the assets must stay INSIDE the bundle: marking them `ExcludeFromSingleFile` leaves them beside the exe where nothing looks for them, and the app finds an `Assets` folder containing only the application icon. The page then fails to load and every pane reports `session not realized`, while the sidebar, the control plane and persistence all keep working — the failure looks like a terminal bug rather than a packaging one. `ERR_FILE_NOT_FOUND` in the WebView is the tell.

This is the opposite of `Integration\**`, which must be excluded: those paths are handed to a spawned shell, which cannot read out of a bundle. Same directory, same `None` items, contradictory requirements.

`CopyToPublishDirectory` covers the layouts that are not single-file, where the published project is `AgTerm.App` and a referenced project's `None` items reach a build output through copy-local but stop there.

`XtermAssetsTests.Assets_are_declared_to_follow_a_publish_not_only_a_build` asserts the declaration. It is a weak test — it reads the csproj rather than a published layout — because the existing asset test reads the build output, where the files are always present, so a packaging-only gap is invisible to the suite. Worth remembering for any future run-time file: the question is not whether the build works, but whether the file is reachable from `AppContext.BaseDirectory` in the packaging the user actually runs.
