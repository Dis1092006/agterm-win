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
    (`session not realized`, `quick terminal not open`, `no selection`
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
  пайпа (см. запись о транспорте). Help-текст `window fullscreen` не упоминает
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
  `session not realized` сохраняется. Restore() выставляет выбор мимо
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
  паритетная ошибка `"session not realized"` сохраняется как есть.

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

- **D3 themes and notifications.** The Windows theme catalog has upstream's 603 names: the Ghostty theme pack that upstream bundles at its pinned Ghostty revision, plus upstream's own `agterm` theme. Upstream reads the names from a directory in the app bundle; Windows embeds the same files, unchanged, in `AgTerm.Xterm.dll` (`src/AgTerm.Xterm/Themes/SOURCE.md`), so the names and their order are the same. Each theme carries its 16 ANSI colors, cursor and cursor text colors, and selection background and text colors, and applies to existing and future panes. A theme file that fails to parse is shown in the failure InfoBar and left out of `theme.list`. `theme.set` keeps the upstream two-slot transitions and exact unknown-theme errors, persists the slots in `settings.json`, follows Windows light/dark appearance, and derives WinUI chrome brightness from the active background. Theme-only appearance changes preserve per-pane font sizes, dashboard temporary fonts, and overlay background overrides. The theme palette previews the selected row live and saves it on Enter or click, as upstream's does. Two differences: upstream applies a preview to every window because its Ghostty config is app-global, while Windows previews only the window that has the palette open and the other windows change once on accept; and upstream's preview clears per-session font zoom because libghostty has no colors-only reload, while Windows keeps it. A palette pick writes the slot on screen (the dark slot while following Windows appearance in dark mode), and the palette has no "Default terminal" row while following, as upstream.

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

- **Only a bare shell argv is rewritten.** `LiveControlHost.CaptureRequest` puts a user's own program in the same `argv` for `session.new --command`. Injecting there would replace that program with a shell and break restore, so `ShellIntegration.Inject` rewrites a single-element argv naming a known shell and returns everything else untouched.
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
- **Both extensions are off by default in the engine, and both are enabled here.** `buildTerminalOptions` sets them for every pane. `win32InputMode` (`DECSET 9001`) is the ConPTY-native way to report full key events and helps TUI input generally.
- **The kitty protocol outranks win32-input-mode here, and the engine does the opposite.** `KeyboardService.evaluateKeyDown` checks win32-input-mode first and returns before the kitty path, so an app that enables both gets win32 key records. Claude Code enables both: it writes `DECSET 9001` at startup and then `CSI > 5 u`. Measured from the real `claude.exe` through a pseudoconsole, the first bytes out are `ESC[?9001h`, then `ESC[<u ESC[>5u ESC[>4;2m`. Shift+Enter then left as `ESC[13;28;13;1;16;1_`, which Claude Code does not read as Shift+Enter, so it submitted the prompt instead of inserting a newline. Ctrl+Enter worked throughout, because it survives as a win32 record the app does understand - that asymmetry is what identifies this bug. `openPane` watches the kitty push and pop and clears the `win32InputMode` option while a request is in effect, so the pane encodes what the app asked for. The mode itself stays set: `DECSET 9001` is the app's, not ours, and the option is what the engine consults. `XtermPageLiveTests.Popping_the_kitty_stack_gives_win32_input_mode_back` pins the restore.
- **An app that never asks keeps the old encoding.** The protocol is opt-in over the wire: the engine answers `CSI ? u`, tracks the stack pushed by `CSI > flags u` and popped by `CSI < n u`, and reports CSI u only while a request is in effect. Enabling the option changes nothing for a program that does not use it. `XtermPageLiveTests.Shift_enter_reports_kitty_csi_u_while_the_keyboard_protocol_is_pushed` pins all three states: CR before the push, `ESC [13;2u` for Shift+Enter while pushed, CR again after the pop.
- **Host chord interception runs first and is unaffected.** `attachCustomKeyEventHandler` sees the DOM event before any encoding, so which chords reach the pty is still decided by the keymap, not by the protocol.

Claude Code is the case that motivated this: its newline-without-submit binding needs Shift+Enter to be distinguishable from Enter, and `claude /terminal-setup` cannot help, since it writes per-terminal config for a fixed list of known terminals and has no entry for this app. Supporting the protocol is not sufficient on its own, because the request has to come from the program: see the next section.

## Saving a file while another process holds it

Every stored file is written to a temp file in the same directory and renamed over the destination, so a crashed write leaves the old file rather than half of the new one. On Windows that rename fails with `UnauthorizedAccessException` ("Access to the path is denied") while any other process holds the destination open, even for reading: a reader with `FileShare.Read` is enough, measured directly.

Nothing in this app opens those files twice. The holder is outside it, a virus scanner or the search indexer opening a file for a moment right after it is written, so the lock lasts a moment and the next attempt succeeds. `LiveApplicationHost.SaveIndexLocked` threw out of a test run that way, and the same pattern sat in `PersistenceStore`, `RecentClosed` and `SettingsStore`.

`AtomicFile.Replace` retries the rename for 2 seconds, backing off from 10ms to 100ms, and rethrows if the lock outlasts that. A caller that could not store its state has to hear about it: `RecentClosed` already swallows these exceptions by design (upstream does too), while the other three propagate.

## Clipboard chords

Upstream has no clipboard entry among the 41 builtin actions, and `parity/surface.json` confirms it: copy and paste exist only as the control commands `session.copy` and `session.paste`. On macOS the chords belong to Ghostty, which agterm runs inside. Here there is no such host, so a pane received no clipboard chord at all: Ctrl+V, Ctrl+Shift+C/V, Ctrl+Insert and Shift+Insert were dropped, and only Ctrl+C did anything, by sending 0x03.

The chords are added in the pane, not in the keymap. Adding builtin actions would change the parity surface; the keymap grammar also has no `insert` key, so two of these chords cannot be written in it. They are handled in `openPane` instead, after the keymap, so a user binding on the same chord still wins.

- **Ctrl+C copies only when there is a selection.** With nothing selected it falls through to 0x03, so interrupting a running program keeps working. `XtermPageLiveTests.Ctrl_c_without_a_selection_still_interrupts` pins that half.
- **Ctrl+V, Ctrl+Shift+C, Ctrl+Shift+V, Ctrl+Insert and Shift+Insert always act on the clipboard.** This is what Windows Terminal and VS Code do.
- **The host writes the clipboard, the page does not.** `navigator.clipboard.writeText` fails with `NotAllowedError` without a user gesture, and a key event dispatched by the engine does not count as one. Measured in the live WebView2: the page is a secure context and the API is present, and the write is still refused. A copy posts `copyRequested` with the selected text and `MainWindow` writes it, which mirrors the clipboard read that right-click paste already used.

## Terminal identification for spawned shells

Upstream needs none of this. agterm runs on Ghostty, so a child process reads Ghostty's own `TERM=xterm-ghostty` and `TERM_PROGRAM=ghostty` and knows what it is talking to. Here the children are spawned by a WinUI app, and the app declared nothing: `AGTERM_PANE_ID` was the only variable added, so everything else was inherited from whatever started agterm-win. Launched from an IDE terminal, the shell reported that IDE. `TerminalIdentity` declares the identification, and `ConPtySession.RemoveEnvironment` can drop an inherited variable instead of only overwriting it.

- **`TERM` is declared as `xterm-256color`.** What xterm.js implements. Inheriting left it describing a different emulator, or missing entirely when the app was launched from Explorer.
- **`TERM_PROGRAM` is declared as `ghostty`.** See below: this is an allowlist key, not a description.
- **`TERM_PROGRAM_VERSION` and `WT_SESSION` are removed.** `TERM_PROGRAM_VERSION` has no declared value to beat it with, so an inherited version would be read as this terminal's. `WT_SESSION` is read later in the same detection chain and reinstates the Windows Terminal path on its own.

### The terminal name is an allowlist key

Programs gate keyboard features on the terminal name, and Claude Code is the case this port had to satisfy: without the kitty keyboard protocol, Enter and Shift+Enter both reach the pty as CR and newline-without-submit cannot work.

It settles `extendedKeys` from the environment only for a name on a fixed list - `iTerm.app`, `kitty`, `WezTerm`, `ghostty`, `tmux`, `windows-terminal`, `WarpTerminal` - and pushes the protocol (`CSI > 5 u`) on the same test. Two findings, both measured rather than reasoned:

**`windows-terminal` is the one entry that costs the feature.** For that name specifically, Claude Code additionally requires a CONFIRMED reply to the kitty query `CSI ? u`. A default does not count, only an answer that came back. That answer never arrives, and not because of the engine. ConPTY answers terminal queries itself instead of forwarding them:

| Query | Answer when fed straight into xterm.js | Answer through ConPTY |
|---|---|---|
| `CSI > 0 q` (XTVERSION) | `DCS >\|xterm.js(6.1.0-beta.304) ST` | none |
| `CSI ? u` (kitty) | `CSI ? 0 u` | none |
| `CSI c` (primary DA) | `CSI ? 1 ; 2 c` | `CSI ? 61 ; 6 ; 7 ; 21 ; 22 ; 23 ; 24 ; 28 ; 32 ; 42 c` |

The third row is the tell: a reply arrives, but it is conhost's signature, not xterm.js's. conhost answers what it knows and silently swallows what it does not. `PSEUDOCONSOLE_PASSTHROUGH_MODE` (flag `0x8`) was tried on build 26200 and changed none of this.

**Declaring no name is not an option.** The detection chain ends with `return process.env.TERM`, so with `TERM_PROGRAM` unset the terminal was named `xterm-256color`, which is not on the list, and `extendedKeys` went off entirely - worse than the Windows Terminal claim it replaced.

That leaves the list. `ghostty` is the entry this port can carry: the others are gated on a version string that would have to be published and kept plausible as those terminals release, and `windows-terminal` is the dead end above.

This is a claim about behaviour, not provenance, and it is narrower than it looks. The protocol stays opt-in over the wire: the engine reports `CSI u` only while an application has pushed the stack, so a program that never asks keeps the legacy encoding. Capabilities that are probe-confirmed rather than name-granted - kitty graphics, pixel mouse - stay off on their own, because the probe gets no reply through ConPTY.

The engine side is not in question: feeding either `CSI > 5 u` (what Claude Code sends) or `CSI > 1 u` into a live pane makes a following Shift+Enter arrive as `ESC [13;2u` instead of CR. Both are covered by live tests.

What the name gives up is one performance optimization that was tied to `WT_SESSION`: Claude Code skipped its `DECSTBM` scrolling-region path for a Windows terminal session and repainted more instead. Correct either way.

If a future program needs something else from the name, the question to ask first is whether it queries the terminal to confirm what the name claims. Any capability confirmed by a query hits the same wall, until ConPTY forwards queries or this port answers them itself.

## Assets must survive packaging, not only a build

The app publishes as a directory, not a single file. `PublishSingleFile` produced a 220 MB exe that unpacked itself into `%TEMP%\.net\AgTerm.App\<bundle id>` before any code ran, and the id is per build, so each new build paid the unpack again and left the previous copy behind (1.4 GB across 8 directories on the development machine). Measured time from launch to window, on the same machine: 34.9 s for the first run of a build and 4.4 s afterwards, against 5.0 s and 1.0 s for a directory. The bundle costs on every launch, not only the first. `agtermctl` stays single-file: one small managed exe with nothing to unpack.

The `ExcludeFromSingleFile` and `CopyToPublishDirectory` markings below still apply and are still required. A directory publish makes `AppContext.BaseDirectory` and `Environment.ProcessPath`'s directory the same folder, so both resolutions land in the right place; the markings are what keeps a single-file publish working if it is ever turned back on.

The terminal page is not embedded in the assembly: WebView2 serves it with `MapVirtualHostToFolder(XtermAssets.Directory)`, which resolves to `AppContext.BaseDirectory\Assets` and needs real files on disk. Two separate things have to hold for that, and the published app satisfied neither for a while.

`PublishSingleFile` extracts the bundle to a directory under `%TEMP%\.net\<app>` and points `AppContext.BaseDirectory` there, not at the folder holding the exe. So the assets must stay INSIDE the bundle: marking them `ExcludeFromSingleFile` leaves them beside the exe where nothing looks for them, and the app finds an `Assets` folder containing only the application icon. The page then fails to load and every pane reports `session not realized`, while the sidebar, the control plane and persistence all keep working — the failure looks like a terminal bug rather than a packaging one. `ERR_FILE_NOT_FOUND` in the WebView is the tell.

This is the opposite of `Integration\**`, which must be excluded: those paths are handed to a spawned shell, which cannot read out of a bundle. Same directory, same `None` items, contradictory requirements.

`CopyToPublishDirectory` covers the layouts that are not single-file, where the published project is `AgTerm.App` and a referenced project's `None` items reach a build output through copy-local but stop there.

`XtermAssetsTests.Assets_are_declared_to_follow_a_publish_not_only_a_build` asserts the declaration. It is a weak test — it reads the csproj rather than a published layout — because the existing asset test reads the build output, where the files are always present, so a packaging-only gap is invisible to the suite. Worth remembering for any future run-time file: the question is not whether the build works, but whether the file is reachable from `AppContext.BaseDirectory` in the packaging the user actually runs.

## The WebView2 profile lives with the user's state, not beside the exe

Upstream has no analogue: libghostty renders in the app's own process and writes no browser profile.

WebView2 keeps a profile per application: cache, cookies, local storage and crash metrics. Given no environment it picks the directory itself, and the name it picks is `<exe>.WebView2` next to the executable. For a published build that is the publish directory, which produced two problems.

`publish.proj` starts with `RemoveDir` on the output directory, so a clean publish has to delete a profile written by the last run. WebView2 keeps some of those files memory-mapped past process exit, `EBWebView\CrashpadMetrics-active.pma` among them, so the delete failed and the publish stopped before building anything. The error named a `.pma` file and read as a file-locking problem rather than a layout one.

The second problem is quieter: a successful clean publish wipes the profile, so the app starts cold every time the user rebuilds it.

`MainWindow.BootPageAsync` now creates the environment explicitly with `CreateWithOptionsAsync` and a `userDataFolder` under the app's state dir, `AgTerm.Core.Model.WebViewProfile`. The state dir is the same root as `workspaces.json` and the `windows\` directory, and it honors `%AGTERM_STATE_DIR%`, so two instances pointed at different state dirs do not share one profile. The publish directory then holds the two exes and `Integration\`, and the profile survives a republish.

The resolution lives in `AgTerm.Core` rather than in the window because `AgTerm.App` has no test project. `WebViewProfileDirectoryTests` covers it; the window keeps only the `CreateWithOptionsAsync` call.

A failure to create the directory surfaces through the existing path: the call sits inside `BootPageAsync`, whose task `ReportPageInitializationFailureAsync` already awaits, so it reports an InfoBar rather than crashing.

The WinUI app reaches WebView2 through the Windows App SDK projection, which exposes a narrower surface than the standalone `Microsoft.Web.WebView2` package the test fixture uses. `CoreWebView2Environment.CreateAsync` there takes no arguments, and the only way to name a user data folder is `CreateWithOptionsAsync(browserExecutableFolder, userDataFolder, options)`. Code copied from the fixture, or from Microsoft's WebView2 documentation, will not compile against the app's projection.


## Bringing a window to the front

`window.select` and `pick.open --follow` bring the target window to the front, as upstream does. `Window.Activate` does not do that for a window that is already open: measured with `window.select` from a separate terminal, the window stayed behind and its taskbar button did not flash, even after that terminal had called `AllowSetForegroundWindow(ASFW_ANY)`. `MainWindow.ActivateWindow` therefore also calls `SetForegroundWindow` (`WindowForeground.Raise`).

Windows refuses that call while another application is in front, for example when `agtermctl` runs in a separate terminal and agterm is behind it. Then the window's taskbar button flashes until the user switches to it. Upstream brings agterm in front of other applications, which macOS allows. The routing side does not depend on it: the window is frontmost for routing either way.

## Upstream behavior adopted beyond the pin

The pin stays `7df70fe`. The entries below are upstream behavior changes to commands the port already implements, taken from upstream `ed831ab` (v0.36.0). Each names the upstream commit it follows.

- `session.type` and `quick.type` refuse text with a NUL byte: `text must not contain a NUL byte` (upstream `6bc8a0b7`). ConPTY would pass the byte through, but the reply is part of the wire contract.
- `session.type`, `session.text`, `font.inc|dec|reset` report `invalid pane: X` from the dispatcher, before target resolution (upstream `b3ef1baf`). The aliases upstream added with horizontal splits (`eb29d556`) are not adopted.
- `session.overlay.open` refuses `--size-percent` outside 1..100 in the dispatcher and in the CLI, where it was clamped (upstream `94c509bf`, `c9c9b81e`).
- `session type --stdin` and `quick type --stdin` read raw bytes and refuse invalid UTF-8 with `stdin must be valid UTF-8` (upstream `7b8f63cf`). A leading BOM is kept as U+FEFF; what Swift does with it is not verified.
- `session.paste`, `session.selectall` and `session.copy` answer `session not realized` when the pane has no terminal behind it: a pending realize or stopped output (upstream `9f145925`).
- `session.type` on an unrealized main pane answers `session not realized` with or without `--select`, and `--select` selects only when the surface is not ready (upstream `22d7e1c1`). The port checks once more after the select instead of polling ~360 ms. A session realizing outside any dispatch scope (created from the UI) and typed into during its realize window gets the error at once.
- `session.type` and `quick.type` write a final Return 10 ms after the text (upstream `2cd47dca`). Not measured whether Claude Code under ConPTY needs the gap; it is kept for parity.
- Session, workspace and window names and a session `--cwd` lose C0 control characters and DEL before they are stored, on the control and GUI paths (upstream `97d4e6af`). The port also sanitizes an OSC 7 / OSC 9;9 cwd, where `%0A` decoded to a real LF.
- A foreground-created workspace is the current one until the selection changes; `workspace.select` on a workspace with sessions takes the target back, on an empty one makes it current (upstream `0106275a`). With the pin's single focused workspace, `hidden by the filter` means another workspace is focused.
- A custom command fired in the split pane gets the split pane's directory in `{AGT_SESSION_PWD}`, in the environment variable (`$env:AGT_SESSION_PWD` in pwsh, `%AGT_SESSION_PWD%` in cmd) and as its working directory (upstream `67d65657`). The `{AGT_SESSION_PWD}` token expands unquoted into the shell line, so a directory name with `&` or `;` reaches the shell. The environment variable is the safe form, and upstream has the same rule.
- A custom command whose session directory is gone does not start and shows `Command failed` / `<name> (session directory <path> no longer exists)` (upstream `312987af`). Upstream posts a macOS banner; the port uses the failure InfoBar.
- A restore does not type a captured command or a pinned override that carries a C0 control character or DEL (and, for a captured argv, U+FFFD); the pane starts a plain shell (upstream `131dfc61`). A captured command is typed into the shell, never executed in its place, as upstream does.
- Captured foreground commands are armed only by the launch restore, not by a window reopen (upstream `ef46edcd`). Upstream's replay-exactly-once rework (`a8b5252`) is not adopted: the port never captures a foreground command, so the persisted field is only ever loaded from a snapshot.
- Upstream quotes a captured command for its POSIX shell. The port types one only when each argument is made of letters, digits, spaces and `_ - . / \ : + ~` (`WindowsCommandLine.IsInertForShells`, following upstream `131dfc61`), because the line is typed into pwsh or cmd and CreateProcess quoting does not protect it. A command with `=` or `,` (for example `--opt=value`) is not replayed and the pane starts a plain shell.
- `window.select` makes the window frontmost for routing even when Windows refuses to activate it for a background caller (upstream `28cad2c1` takes frontmost the same way).
- Palette fuzzy ranking caps a substring match at `5 + min(offset, 34)`, under the subsequence floor of 40 (upstream `2c8ad9f1`).
- While a pick is pending in a window, `quick show`, `surface.zoom` turning a zoom on, `dashboard` opening and `session.search` open/next/prev answer `pick pending`; dismissals keep working (upstream `6b6323ed`).
- While a `gui` question is pending in a window, the same four commands answer `ask pending`, and a `pick.open` answers `ask already pending` (upstream `32ec288d`). A pick refuses an `ask.open` with `pick already pending`.
- Closing a session (hard or soft, alone or in a batch), removing its workspace, closing the pane a question covers, closing or deleting the window, and quitting cancel its terminal questions; undo brings a session back without its question (upstream `1b765e69`). A `gui` question anchored to a session is cancelled when that session is deselected or the anchored pane leaves the screen.
- Every palette moves the selection with Ctrl+J and Ctrl+K, with Control as the only modifier (upstream `2fb0ca72`). The letter keys are matched by virtual key, so on a non-Latin layout they follow the physical QWERTY positions.
- Palettes accept blank ids, whitespace-only labels and an empty placeholder, because pick sends them.
- While a HUD holds a session's overlay slot, `session.overlay.open` replaces it with the program, `session.overlay.close` closes it and answers ok, `session.overlay.resize --size-percent N` sets its width and keeps that width through re-measures until the next open or update, `session.overlay.resize --full` answers `a hud is always floating: pass --size-percent, not --full`, and `session.overlay.result` answers `no overlay result: the slot holds a hud` (upstream `d0bec154`, `3ac149e6`). Beside a HUD, `tree` reads `overlay: false` and lists no overlay surface.

## Upstream features adopted beyond the pin

New commands and fields taken from upstream `ed831ab` (v0.36.0) beyond the pin `7df70fe`. `parity/adopted.json` lists them for the parity tests.

- `pick` (`pick.open`, `pick.result`, `pick.cancel`, `tree.pickPending`): the native picker a script drives through `agtermctl pick` (upstream `6b6323ed`, `33cd0872`, `d9da472e`). Design: `docs/superpowers/specs/2026-10-10-pick-design.md`.
  - The headless host keeps a pick pending until it is cancelled or a test resolves it through `HeadlessControlActions.Pick`; it never answers `no pick surface`.
  - A pick item decoded with a null `id` or `label` is refused with `invalid request: pick item requires id and label`; upstream fails the Swift decode with its own message.
  - `agtermctl pick` drops a leading UTF-8 BOM from stdin before deciding between JSON and lines. Windows PowerShell 5.1 and Notepad write one.
  - Malformed JSON or invalid UTF-8 on stdin gives `stdin must be UTF-8 text or a JSON item array` and exit 1. Upstream prints a Swift decoder description for malformed JSON. Every stdin error exits 1 here where upstream exits 64.
  - `agtermctl` writes in the console's output code page, the .NET default. pwsh decodes captured native output with that same code page, so `$x = agtermctl pick ...` keeps non-ASCII labels; a console set to UTF-8 gets UTF-8. Upstream always writes UTF-8, because macOS terminals are UTF-8.
  - A pick that arrives while a built-in palette is open closes it at once in any window. Upstream does that at once only in the frontmost window, because each window here has one palette view.
  - The picker is centered in the window, as the other palettes are; centering over the terminal area (upstream #367) is not ported.
  - Item ids are compared after NFC normalization, which matches Swift `String` equality.
  - `pick.open` with no open window answers `no open window`. Other commands answer `no such window: active` in the same state.
- `ask` (`ask.open`, `ask.result`, `ask.cancel`, `tree.askPending`, session `ask`): a question with 1 to 6 buttons that a script drives through `agtermctl ask` (upstream `32ec288d`, `1b765e69`). Design: `docs/superpowers/specs/2026-10-11-ask-design.md`.
  - The terminal style is drawn as HTML inside the xterm page over the pane or the whole session. The host keeps the highlight; the page reports keys and clicks as intents. A hotkey uses the typed letter when it is ASCII, else the key's Latin position (`KeyboardEvent.code`), so it works on a Cyrillic layout; upstream uses the typed character only.
  - Over a terminal question, window chords from the built-in keymap keep working; custom command chords and the clipboard chords do not reach a covered pane.
  - A terminal question hides while the quick terminal is shown and comes back when it hides, as under zoom and the dashboard; it stays pending. Upstream's quick terminal is a separate floating window drawn over the question; here it is a layer in the same page, so a question drawn above it would take its clicks.
  - Over a single pane, a click on the program overlay outside a pane question gives the program the keys until the next question arrives or the user clicks the question. Upstream flips the session's split focus, which persists. Here that flag also picks the pane shown alone, so flipping it would hide the question. Over a split the port flips the split focus as upstream does.
  - The headless host keeps a question pending until it is cancelled or a test answers it through `HeadlessControlActions.Pick` or `AppStore.ResolveAsk`; it never answers `no ask surface`.
  - A button decoded with a null `id` or `label` is refused with `invalid request: ask button requires id and label`; upstream fails the Swift decode with its own message.
  - Remote presentation of questions is not ported: no `reason` in the result, no `remote` or `replica` in the tree.
  - A `gui` question closes a built-in palette at once in any window, as a pick does.
  - Button ids are compared after NFC normalization, which matches Swift `String` equality.
  - `AskRegistry` keeps the pending ids itself. Upstream asks the owner through a closure; here the owner's model is behind the window lock and polls arrive on the pipe thread.
  - `ask.cancel` without `--window` runs in the owner's window, wherever the frontmost window is.
  - A hotkey matches by virtual key, so on a Cyrillic layout the key at the Latin letter's position presses the button. Upstream matches the typed character, and there a hotkey does nothing on a non-Latin layout.
- `session.hud` (`session.hud.open`, `session.hud.update`, `session.hud.close`, session `hud`): a passive message panel that a script posts over a session through `agtermctl session hud` (upstream `d0bec154`, `1923906e`, `224321c5`, `094dc986`, `9247b96f`, `8e710a6e`, `3ac149e6`). Design: `docs/superpowers/specs/2026-10-13-hud-design.md`.
  - The panel is an HTML layer in the xterm page, drawn from rows the host wraps and sizes. Upstream runs a helper script in a terminal surface and passes it a body file. The port has neither, so `hud helper is not bundled in this build` and `could not write the hud message` never occur.
  - `sizePercent` and `heightPercent` in `tree` come from a cell the page measures in CSS pixels, in the terminal's font. For the same text they differ from the numbers macOS reports; the clamp rules are the same.
  - The panel hides while the quick terminal is shown and comes back when it hides, as under zoom and the dashboard. Upstream's quick terminal is a separate window above the session; here it is a layer in the same page.
  - The reason in `cannot read --file <path>: <reason>` is the .NET exception message.
  - `agtermctl session hud --file` drops a leading UTF-8 BOM before it sends the message. Windows PowerShell 5.1 and Notepad write one, and the server would count it as part of the text.
  - The `--markdown` help says a link opens on a Ctrl+click, where upstream says command-click.
  - Alt+W closes the session, and a HUD goes down only with that soft close: a HUD with `--hide-after` closes, one without stays and comes back on undo. Upstream's Command-W takes the session's overlay slot down first and leaves the session open.
  - A link in a markdown HUD opens on Ctrl+click; upstream uses command-click. As upstream, a Ctrl+click anywhere over the panel is consumed with its release, link or not. A terminal question drawn above the HUD and the search bar keep their own clicks.
  - A `file://` link selects the file in Explorer and is never opened; other links open with the system handler. A `file://` link counts only with an empty or `localhost` host. Upstream also takes the machine's own host name, and it ignores the macOS auto-mount roots `/net`, `/Network` and `/home`, which are ordinary folders on Windows.
  - A link URL must be printable ASCII as written. A raw space or non-ASCII character makes the link a plain label; percent-encode it.
  - Markdown is parsed with Markdig (CommonMark, pipe tables and strikethrough), not Foundation `AttributedString(markdown:)`. A raw HTML block inside a quote or a list item keeps the quote bar and the item indent; upstream draws it with neither, because Foundation gives an HTML block no block context.
  - Markdig refuses block nesting deeper than 128 containers (about 127 nested quotes or 63 list levels) and inline nesting past about 12000 unmatched brackets. Past those limits upstream lays the message out; the port shows it as plain rows.
  - A `file://` link with a colon after the drive (an NTFS stream such as `C:/x:stream`) or a drive-relative path (`C:x`) is drawn as a link and does nothing on Ctrl+click.

## Windows-only extensions

Commands and fields that upstream does not have. `parity/windows-only.json` lists them for the parity tests, and a test checks that none of them exists in `docs/upstream/surface-v0.36.0.json`. None of them changes an upstream command, field or error string.

- A theme per workspace: the command `workspace.theme`, the field `theme` of the workspace node in `tree`, and the field `theme` of the workspace in the window snapshot. Design: `docs/superpowers/specs/2026-10-15-workspace-theme-design.md`.
  - `workspace.theme` selects the workspace as `workspace.rename` does and answers the same `{"id":...}`. `args.name` sets the theme; a missing or blank name clears it. A name not in `theme.list` gives `unknown theme: X`, the string `theme.set` uses.
  - `agtermctl workspace theme [NAME] [--target W] [--window ...]`; without `NAME` it clears the theme.
  - The panes of the workspace's sessions (main, split, scratch, overlay) show its theme. Only the colors come from it; font, opacity and cursor style stay global. An overlay `--background-color` still wins. The quick terminal and the panes of workspaces without a theme show the global theme.
  - The terminal-area background follows the selected session's workspace. The dashboard, an empty layout and a zoomed quick terminal use the global theme. The title bar and the sidebar always follow the global theme.
  - `tree` reports the stored name even when the theme catalog does not have it; such a workspace shows the global theme.
  - The snapshot reads `theme` leniently: a value that is not a string, or a blank string, reads as no theme, and the rest of the snapshot loads.
  - The workspace row menu has `Theme…`. It opens the theme palette for that workspace: `Use global theme` first, then the catalog, with a live preview on that workspace's panes; Enter or click sends `workspace.theme` to the window, Esc and every other close restore the saved look. If the selected session belongs to another workspace, the window first selects the workspace's most recently selected session. There is no builtin action and no default chord for it.
