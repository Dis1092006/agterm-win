# agterm — платформо-независимая спецификация (ТЗ для Windows-порта)

> Извлечено из `agtermCore` (host-free Swift, Foundation-only) и `.claude/rules/control-api.md`
> действующего macOS-приложения. Это **контракт данных + control-протокол + keymap-формат**, без
> единой привязки к SwiftUI/AppKit/libghostty/Metal. Цель — дать точное ТЗ, по которому Windows-реализация
> (C#/.NET + ConPTY + WinUI/Avalonia, либо любой другой стек) может воспроизвести поведение
> и остаться совместимой с существующими агент-скриптами.

---

## 0. Область и приоритеты

Из трёх «ценностей» agterm на Windows приоритетны и **полностью переносимы**:

1. **Двухуровневая модель `Workspace → Session`** с вертикальным сайдбаром (§2, §3).
2. **Control API для агентов** — 66 команд (65 публичных + 1 тестовая), JSON-протокол, адресация,
   подписка на события (§5, §6, §11).

Непереносимое (движок рендера, шрифт-шейпинг, kitty-графика) заменяется Windows-нативным
терминальным движком (§9) и в этом ТЗ описано только там, где влияет на модель/протокол.

Трёхслойная архитектура оригинала, которую стоит сохранить:

| Слой | В agterm | Windows-эквивалент |
|---|---|---|
| Чистая логика + протокол | `agtermCore` (Foundation, `Codable`, `Sendable`) | отдельная C#-библиотека (`AgTerm.Core`), без UI-зависимостей |
| Сервер + владение сокетом/каналом | `ControlServer` (`@MainActor`) | сервис поверх named pipe / TCP на UI-потоке |
| CLI | `agtermctl` (SwiftPM + argument-parser) | `agtermctl.exe` (System.CommandLine) |

---

## 1. Иерархия объектов

```
Window (окно)                     ← верх; несколько окон, у каждого свой сайдбар
 └─ Workspace (рабочее пространство, именованная группа)
     └─ Session (сессия = одна вкладка/строка сайдбара)
         ├─ main pane   (основной шелл)
         ├─ split pane  (опциональный второй шелл — «одна сессия, два шелла»)
         ├─ scratch     (эфемерный третий шелл, полноэкранный оверлей, не persist)
         └─ overlay     (эфемерный терминал поверх, запускает одну программу, не persist)
```

Ключевые инварианты:
- Workspace нельзя удалить, если он последний (keep-at-least-one).
- Session принадлежит ровно одному Workspace; перемещается между ними.
- Split — это НЕ отдельная сессия: тот же ряд сайдбара, два шелла. `hasSplit` (есть) отличается от
  `isSplit` (показан сейчас side-by-side vs скрыт-максимизирован).
- Scratch и overlay никогда не сохраняются между запусками.

---

## 2. Модель данных (живые объекты)

### Session
| Поле | Тип | Persist | Назначение |
|---|---|---|---|
| `id` | UUID | да | канонический идентификатор |
| `customName` | string? | да | ручное имя (побеждает в `displayName`) |
| `initialCwd` | string | да (как `cwd`) | каталог для спавна шелла |
| `currentCwd` | string? | эфемерно* | живой cwd из OSC 7 / PWD-репорта |
| `oscTitle` | string? | нет | заголовок из OSC 0/1/2 (напр. `user@host:dir` по SSH) |
| `splitCwd` / `splitTitle` | string? | splitCwd — да | cwd/заголовок правого пейна |
| `isSplit` | bool | да | показан ли сплит сейчас |
| `hasSplit` | bool | производное | есть ли сплит вообще (жив, но может быть скрыт) |
| `splitFocused` | bool | нет | какой пейн в фокусе |
| `splitRatio` | double? | да | доля левого пейна (0.05…0.95); nil = 50/50 |
| `fontSize` | double? | да | размер шрифта сессии (cmd +/-) |
| `flagged` | bool | да | в «рабочем наборе» (флаг) |
| `unseenCount` | int | нет | непросмотренные уведомления (бейдж) |
| `agentIndicator` | {status, blink, autoReset} | нет | агент-статус (см. §7-status) |
| `statusChangedAt` | date? | нет | ключ сортировки для attention-навигации |
| `initialCommand` | string? | да | команда вместо login-шелла (`session.new --command`) |
| `foregroundCommand` / `splitForegroundCommand` | string[]? | да | argv переднего процесса, для restore |
| `backgroundWatermark` | spec? | да | водяной знак (см. §7-background) |
| `overlay*` | | нет | состояние эфемерного оверлея |
| `scratch*` | | нет | состояние scratch-шелла |
| `search*` (`searchActive`/`searchNeedle`/`searchTotal`/`searchSelected`) | | нет | состояние поиска по скроллбэку |

\* `currentCwd` кладётся в снапшот при структурных мутациях, но голый `cd` не триггерит запись
(OSC 7 сыплется постоянно) — при крэше теряются только изменения cwd с последней структурной мутации.

**`displayName` (метка в сайдбаре), порядок источников:**
1. непустой (после trim) `customName`;
2. иначе непустой заголовок фокусного пейна (`focusedOscTitle`);
3. иначе basename фокусного cwd (`focusedCwd`, fallback `initialCwd`).
Пины basename: корень `/` → `/`; хвостовой слэш игнорируется (`/a/b/` → `b`); пустой путь → `~`.

**`focusedCwd`** = cwd правого пейна, если `splitFocused` и есть `splitCwd`, иначе `currentCwd ?? initialCwd`.
**`effectiveCwd`** = ВСЕГДА primary (`currentCwd ?? initialCwd`) — сеет новый сплит/оверлей/quick,
и токен `AGT_SESSION_PWD`; не зависит от фокуса.
Начиная с upstream 67d65657 команда, запущенная в сплит-пейне, получает каталог сплит-пейна: см. `docs/PLATFORM-NOTES.md`, раздел `Upstream behavior adopted beyond the pin`.

**`searchDisplayText`**: пусто, пока `searchTotal == nil`; `"no matches"` при 0; `"N matches"` без выбора;
`"S of N"` при выбранном (S зажат до N).

### Workspace
`id: UUID` (стабильный), `name: string`, `sessions: Session[]` (упорядочен).
`unseenCount` = сумма по сессиям (для бейджа свёрнутого ряда).

### Window
Живёт как `AppStore` на окно + запись в `WindowLibrary`. Для протокола проецируется в
`{ id, name, open: bool, active: bool }` (`open` = есть окно на экране; `active` = frontmost).
Per-window состояние сайдбара: `sidebarVisible`, `sidebarWidth`, `sidebarMode` (tree|flagged),
`focusedWorkspaceID`, `selectedSessionID`.

---

## 3. Персистенс (формат снапшота)

JSON. Верхний объект — `Snapshot`, **на каждое окно** (в agterm — по окну через `WindowLibrary`).

```jsonc
{
  "version": 1,                       // мисматч → грузить с нуля
  "selectedSessionID": "<uuid|null>",
  "sidebarWidth": 240.0,              // null = дефолт
  "sidebarVisible": true,            // null = дефолт (показан)
  "sidebarMode": "tree",             // "tree" | "flagged"; null = tree
  "focusedWorkspaceID": "<uuid|null>",
  "workspaces": [
    {
      "id": "<uuid>", "name": "work",
      "sessions": [
        {
          "id": "<uuid>",
          "customName": "api",         // optional
          "cwd": "/home/u/proj",       // required — live currentCwd или initialCwd
          "isSplit": false,            // optional (forward-compat)
          "fontSize": 14.0,            // optional
          "splitCwd": "...",           // optional
          "splitRatio": 0.5,           // optional (0.05…0.95)
          "flagged": false,            // optional
          "foregroundCommand": ["ssh","host"],       // optional
          "splitForegroundCommand": [...],           // optional
          "initialCommand": "ssh host -t ...",       // optional
          "backgroundWatermark": { /* §7-background */ } // optional, LOSSY-декод
        }
      ]
    }
  ]
}
```

**Правило forward-compat (важно воспроизвести):** у снапшота ЕСТЬ версия (мисматч → старт с нуля), но
у полей сессии/окна её нет — они все Optional, отсутствующий ключ декодится как «дефолт», чтобы старый
файл на диске не ронял загрузку и не стирал дерево. `backgroundWatermark` декодится **LOSSY**: битый/неизвестный
спек (напр. даунгрейд после нового релиза) → nil, а не исключение (иначе одна плохая метка уронила бы весь
`SessionSnapshot` и стёрла все воркспейсы).

`AppSettings` (§8) хранится ОТДЕЛЬНО (`settings.json`) и **без** поля версии — там Optional-поля и есть
механизм совместимости (версия только добавила бы путь «стереть настройки при мисматче»).

### Пути (macOS → Windows)
| Что | macOS | Windows-предложение |
|---|---|---|
| state dir (снапшоты, сокет) | `~/Library/Application Support/agterm/` или `$AGTERM_STATE_DIR` | `~\.agterm-win\` или `%AGTERM_STATE_DIR%` (см. PLATFORM-NOTES) |
| config dir (`keymap.conf`, `ghostty.conf`) | `~/.config/agterm/` или `$AGTERM_STATE_DIR/config` | `AppSettings.configDirectory`, иначе state dir (т.е. `~\.agterm-win\`) — см. PLATFORM-NOTES |
| control-канал | unix-сокет `<stateDir>/agterm.sock`, `chmod 0600` | **named pipe** `\\.\pipe\agterm` (per-user ACL) или TCP `127.0.0.1:<port>` |

Резолвер сокета (перенести 1:1 по смыслу): `socketPath = (AGTERM_STATE_DIR ?? appSupport) + "/agterm.sock"`;
плюс override `AGTERM_CONTROL_SOCKET` (в agterm — для тестов), у CLI это флаг `--socket`.

---

## 4. Резолвер адресов (перенести дословно)

`resolve(target, candidates[UUID], active: UUID?) -> resolved(UUID) | notFound | ambiguous(UUID[])`:
1. пустая строка → `notFound` (пустой префикс матчил бы всё);
2. `"active"` → `active`, либо `notFound` если nil;
3. точный `uuidString` (регистронезависимо) → `resolved`;
4. иначе префикс по `uuidString.lowercased()`: 1 хит → `resolved`, 0 → `notFound`, ≥2 → `ambiguous(hits)`.

`--target` по умолчанию = `active`. `active` для workspace = текущий workspace, при отсутствии выбранной
сессии — fallback на `workspaces.last`.

---

## 5. Control-протокол (транспорт + wire-формат)

- **Соединение:** одно на запрос. Newline-delimited JSON. Клиент шлёт один `ControlRequest`, сервер отвечает
  одним `ControlResponse`, соединение закрывается.
- **Лимит строки:** 1 MiB (граница буфера).
- **Таймаут:** серверный `read` с таймаутом (~5 c) — зависший клиент не должен вешать серийный accept-loop.
- **Идемпотентность старта:** при старте удалить устаревший путь/канал перед бинд-ом; bind best-effort
  (падение логируется, приложение всё равно стартует).

**Request:**
```jsonc
{ "cmd": "session.new", "target": "active", "args": { /* см. §6 */ } }
```
**Response:**
```jsonc
{ "ok": true,  "result": { "id": "<uuid>", /* ... */ } }
{ "ok": false, "error": "not found" }
```
Неизвестный `cmd` → структурированная ошибка декода (НЕ крэш).

**`ControlResult` (все поля optional):**
| Поле | Для команд |
|---|---|
| `id` | мутации возвращают затронутый/новый id |
| `tree` | `tree` |
| `text` | `session.copy` (выделение), `session.text`/`quick.text` (буфер), `session.search` («N of M») |
| `windows[]` | `window.list` (`{id,name,open,active,autoFollowMs?,sidebarVisible?,geometry?,fullscreen?,zoomed?}`) |
| `exitCode` | `session.overlay.result` |
| `count` | `keymap.reload`/`config.reload` (число диагностик), `session.search` (всего совпадений) |
| `affected` | батч-мутации (`session.close`/`session.move` с несколькими target): сколько реально изменено |
| `theme` / `themes[]` | `theme.set` (эхо) / `theme.list` |
| `sync` / `light` / `dark` | `theme.set`/`theme.list` при светло/тёмной паре слотов (см. §6 theme) |
| `ratio` | `session.resize` (применённая доля после clamp) |
| `events` | `events.read` — страница кольца событий `{run, next, items[]}` (см. §11) |

В человеко-читаемом выводе CLI `result.id` печатается ТОЛЬКО для create-команд
(`session/workspace/window new`), где id заранее неизвестен; остальные мутации печатают `ok`.
Под `--json` id есть всегда.

---

## 6. Каталог команд (66 = 65 публичных + 1 тестовая)

Общий словарь аргументов (`ControlArgs`, все optional): `name, cwd, targets[], workspace, workspaceName,
createWorkspace, collapsed, noSelect, text, select, mode, path, color, shape, opacity, fit, position,
repeats, pane, paneID, ratio, ratioDelta, all, lines, to, after, before, run, kinds[], limit, title,
body, command, wait, sizePercent, full, follow, window, width, height, x, y, display, status, blink,
autoReset, sound, light, dark, close, fontSize, autoSize, mru`.

Батч-адресация: `session.close` и `session.move` принимают `args.targets[]` (упорядоченный список)
вместо одиночного `target`; ответ несёт `result.affected` — сколько реально изменено.

### tree
`tree` — вернуть всё дерево (`result.tree`). Узел сессии проецируется как:
`{ id, name, cwd, title?, active, split, splitFocused?, splitRatio?, overlay, overlaySizePercent?,
scratch, flagged, unseen?, fontSize?, splitFontSize?, scratchFontSize?, foreground?[], splitForeground?[],
status?, statusBlink?, statusColor?, statusShape?, statusPane?, restoreCommand?, splitRestoreCommand?,
commandWait?, background?, surfaces?[] }`.
`foreground`/`splitForeground` — живой argv переднего процесса (nil у приглашения; setuid-процессы
опускаются). `status*` — читающая сторона `session.status` (вкл. per-call цвет/форму/пейн).
`restoreCommand`/`splitRestoreCommand` — пин `session.restore` (omitted = авто-захват, `""` = «ничего»).
`surfaces[]` — стабильные адреса поверхностей `{id, kind, active, visible}` для `surface.zoom`.
Узел воркспейса: `{ id, name, active, focused, collapsed?, sessions[] }`.
Верхний уровень дерева ещё несёт: `sidebarMode`, `quickVisible`, `zoomedSurface?`, и read-back
`dashboard` (`dashboardMembers[]` — pane-ref'ы `<id>:left|right` в порядке сетки, `dashboardHighlighted`,
`dashboardFontSize?`, `dashboardFontMode` ∈ `auto|fixed|untouched`).

### events (1)
- `events.read` — прочитать страницу кольца событий (см. §11: курсор `args.run`+`args.after`,
  фильтр `args.kinds[]`, `args.limit` 1..1000). Возвращает `result.events = {run, next, items[]}`.

### Workspace (8)
- `workspace.new` — `args.name`; `args.collapsed:true` создаёт СВЁРНУТЫМ в сайдбаре (чтобы наполнять
  через `session.new --no-select`, не раскрывая). Возвращает id. Read-back: `collapsed` на узле.
- `workspace.rename` — target + `args.name`.
- `workspace.delete` — target. Соблюдает keep-at-least-one → ошибка вместо модалки.
- `workspace.select` — target.
- `workspace.move` — реордер: `args.to` ∈ `up|down|top|bottom`. Учитывает `--window`. Возвращает id.
- `workspace.focus` — target + `args.mode` ∈ `on|off|toggle` (сфокусировать сайдбар на одном воркспейсе).
  `off` разфокусирует, только если target — текущий фокус. Учитывает `--window`. Персист, per-window.
  Read-back: `focused` на узле воркспейса.
- `workspace.collapse` / `workspace.expand` — свернуть/развернуть ОДИН воркспейс в дереве сайдбара
  (per-workspace пара, в отличие от общего `sidebar.expand`/`collapse`). Учитывают `--window`.
  Read-back: `collapsed` на узле.
- Windows: `workspace.theme` — target + `args.name` (пустое или отсутствующее имя сбрасывает): своя тема терминала у workspace. Расширения только для Windows, у upstream нет: команда, поле `theme` у узла workspace в `tree` и у workspace в снимке окна (`parity/windows-only.json`).

### Session (27)
- `session.new` — создать. Назначение (взаимоисключающе): `args.workspace` (id/prefix/`active`) ИЛИ
  `args.workspaceName` (по имени, first-exact-trimmed) с опц. `args.createWorkspace` (reuse-or-create);
  ИЛИ позиционирование `args.after`/`args.before` (якорная сессия — сама несёт свой воркспейс, поэтому
  взаимоисключимо с воркспейс-параметрами). `args.name` — имя; `args.cwd` — каталог; `args.command` —
  запустить как процесс сессии вместо login-шелла (**без** `sh -c`: движок токенизирует в argv с учётом
  кавычек и exec-ит argv[0] напрямую — операторы шелла НЕ интерпретируются; нужен шелл — оборачивать в
  `sh -c '...'` самому); `args.wait` (только с `command`) — ДЕРЖАТЬ сессию открытой после выхода команды
  («press any key», переживает рестарт; read-back `commandWait`); `args.noSelect:true` — создать в фоне,
  не трогая выбор/фокус. Возвращает id.
- `session.duplicate` — свежая сессия (чистый login-шелл) в воркспейсе target'а, сразу ПОСЛЕ него,
  с cwd фокусного пейна источника. Переносится ТОЛЬКО каталог (ни имени, ни команды, ни сплита, ни
  статуса, ни флага, ни шрифта, ни фона). Эквивалент `session.new --cwd <src cwd> --after <src>`.
  Селектит + фокусирует; возвращает id.
- `session.close` — target ИЛИ батч `args.targets[]` (закрытие с одним общим grace-period undo).
- `session.select` — target.
- `session.rename` — target + `args.name`.
- `session.reveal` — open the focused pane's working directory in File Explorer.
- `session.move` — MODE-BEARING, три формы: `args.to` ∈ `up|down|top|bottom` РЕОРДЕР внутри воркспейса;
  `args.workspace` ПЕРЕМЕЩЕНИЕ в другой воркспейс (в конец); `args.after`/`args.before` — поставить
  после/перед якорной сессией (якорь сам определяет воркспейс — relocate+position за один вызов, в т.ч.
  кросс-воркспейс). Для workspace/after/before-форм принимает батч `targets[]` (переносится упорядоченным
  блоком); с `to` батч запрещён. Ровно одна форма; иначе ошибка.
- `session.type` — впечатать `args.text` keystroke'ами (каждый `\n`/`\r`/`\r\n` = Enter; НЕ «вставка» —
  иначе bracketed-paste маркеры). `args.pane` ∈ `left|right|scratch` (дефолт left/main; scratch — даже
  скрытый). `args.select:true` разрешает выбрать ещё-не-показанную сессию для реализации поверхности.
- `session.paste` — paste the system clipboard into the session (also available with Ctrl+V in the terminal).
- `session.selectall` — select the entire terminal buffer (no default shortcut; read with `session.copy`).
- `session.text` — вернуть буфер терминала плоским текстом (`result.text`). Дефолт — видимый экран
  фокусного пейна; `args.all` добавляет скроллбэк; `args.lines` — последние N строк;
  `args.pane` ∈ `left|right|scratch`.
- `session.split` — `args.mode` ∈ `on|off|toggle`. `off` СКРЫВАЕТ (keep-alive).
- `session.scratch` — `args.mode` ∈ `on|off|toggle`; опц. `args.command` (run-once при показе).
  Keep-alive при скрытии, пересоздаётся после `exit`. Не persist.
- `session.focus` — `args.pane` ∈ `left|right|other` (`other` = переключить, дефолт). Ошибка без сплита.
- `session.resize` — разделитель сплита: `args.ratio` ИЛИ `args.ratioDelta` (ровно одно). Клампится
  (0.05..0.95), персистится, эхо в `result.ratio`. Control-native.
- `session.go` — `args.to` ∈ `next|prev|first|last|next-attention|prev-attention`. Относительная (нет
  target); next/prev не заворачивают, attention-варианты заворачивают по `blocked`/`completed`.
  Возвращает id новой выбранной.
- `session.copy` — выделение → `result.text`. Не трогает системный буфер. Пусто → `no selection`.
- `session.search` — `args.text` — искомое; `args.to` ∈ `next|prev|close`. Селектит сессию. Возвращает
  `result.count` + `result.text` («N of M»).
- `session.status` — агент-статус (см. §7-status; + `--color`, `--shape`, `--pane`, `--pane-id`).
- `session.flag` — `args.mode` ∈ `on|off|toggle|clear` (`clear` снимает со всех). Read-back `flagged`.
- `session.seen` — сбросить счётчик непросмотренных уведомлений БЕЗ смены выбора/фокуса (фокус-фри
  парный к `notify`, который его поднимает). Идемпотентна. Read-back: `unseen` на узле.
- `session.restore` — пин того, что пейн перезапустит на СЛЕДУЮЩЕМ старте, поверх авто-захвата:
  `args.mode` ∈ `set|none|clear` (`set` + `args.command` — пин shell-строки, `none` — пин «ничего»,
  `clear` — вернуть авто-захват). `args.pane` ∈ `left|right` (`scratch` отвергается), `args.paneID` —
  стабильный токен поверхности (нерезолв без явного `pane` — ошибка). Липкий (срабатывает на каждом
  рестарте до очистки); гейтится настройкой restore-running-command, но обходит denylist.
  Read-back: `restoreCommand`/`splitRestoreCommand`.
- `session.background` — водяной знак (см. §7-background; + режим `color` — сплошной цвет фона).
- `session.overlay.open` — эфемерный терминал поверх сессии, запускает `args.command` (через `sh -c` —
  шелл-семантика ЕСТЬ). По умолчанию полноэкранный; `args.sizePercent` (1..100) → плавающая панель.
  **По умолчанию НЕ переключает пользователя** — открывается фоном на target; `args.follow:true`
  селектит target после открытия. `args.color` — собственный цвет фона оверлея. `args.wait` — держать
  после выхода команды. Захват кода выхода: обёртка `sh -c '( eval "$CMD" ); echo $? > "$CODEFILE"'`,
  БЕЗ редиректа stdout/stderr.
- `session.overlay.resize` — изменить УЖЕ открытый оверлей: `args.sizePercent` (плавающий) ИЛИ
  `args.full:true` (обратно в полноэкранный); ровно одно. Программа продолжает жить (без ре-спавна).
- `session.overlay.close` — target.
- `session.overlay.result` — `result.exitCode` после закрытия (`overlay still running` пока идёт,
  `no overlay result` если не было). CLI `--block` = open → поллинг → выход с кодом.

### surface / dashboard (2)
- `surface.zoom` — `args.mode` ∈ `show|hide|toggle`: развернуть ОДНУ поверхность на всё окно (сайдбар
  скрыт, остаётся тонкая полоса тайтл-бара с выходом). Target — стабильный адрес поверхности
  `surface:<session-id>:left|right|scratch|overlay` или `quick` (брать из `tree.surfaces[]`); без
  target — активная поверхность. Read-back: `zoomedSurface` на верхнем уровне дерева. Зум и дашборд
  взаимоисключающи (открытие одного закрывает другой).
- `dashboard` — view-only сетка живых пейнов (до 9): `args.targets[]` — явные id; ИЛИ `args.mru:true` —
  недавно-использованные (MRU) сессии окна; `args.close:true` — закрыть открытый дашборд (без других аргументов).
  Шрифт ячеек: `args.fontSize` (абсолютный) ИЛИ `args.autoSize` (относительный, сжимается с ростом
  сетки) — взаимоисключимо. Read-back: `dashboardMembers`/`dashboardHighlighted`/`dashboardFontSize`/
  `dashboardFontMode` на верхнем уровне дерева.

### quick / sidebar / notify (7)
- `quick` — `args.mode` ∈ `show|hide|toggle` (дефолт toggle). Quick-terminal frontmost-окна.
  Read-back: `quickVisible`.
- `quick.type` / `quick.text` — близнецы `session.type`/`session.text` для quick-терминала
  frontmost-окна (без `target`/`window`/`pane`).
- `sidebar` — `args.mode` ∈ `show|hide|toggle`. Видимость сайдбара frontmost-окна.
- `sidebar.mode` — `args.mode` ∈ `tree|flagged|toggle`.
- `sidebar.expand` / `sidebar.collapse` — развернуть ВСЕ воркспейсы / свернуть все, кроме активного.
  Учитывают `--window`.
- `notify` — десктоп-уведомление, атрибутированное сессии. `args.body` (обязательно), `args.title`
  (дефолт = имя сессии). Поднимает `unseen`-бейдж (парная `session.seen` снимает).

### font (3)
- `font.inc` / `font.dec` / `font.reset` — размер шрифта пейна; `args.pane` ∈ `left|right|scratch`
  (дефолт left/main). Read-back: `fontSize`/`splitFontSize`/`scratchFontSize` на узле.

### window (10)
- `window.new` — новое окно. Возвращает id.
- `window.list` — `result.windows[]` (`{id, name, open, active, autoFollowMs?, sidebarVisible?,
  geometry?, fullscreen?, zoomed?}`).
- `window.select` — target.
- `window.close` — закрыть окно на экране (запись остаётся).
- `window.rename` — target + `args.name`.
- `window.delete` — удалить запись окна.
- `window.resize` — `args.width`/`args.height` (в точках).
- `window.move` — `args.x`/`args.y` (от левого-верхнего угла целевого дисплея, y вниз), опц.
  `args.display`.
- `window.zoom` — maximize-to-screen toggle (жест двойного клика по заголовку). Read-back: `zoomed`.
- `window.fullscreen` — нативный полноэкранный режим (toggle). Read-back: `fullscreen`.

### keymap / config / theme / restore (5)
- `keymap.reload` — перечитать `keymap.conf`; `result.count` = число диагностик. App-global.
- `config.reload` — перечитать движко-конфиг; `result.count` = число диагностик.
- `theme.set` — теперь ДВУХСЛОТНАЯ: `args.name`/`args.light` (алиас; оба сразу — ошибка) — светлая/
  единственная тема; `args.dark` — тёмный слот, его наличие включает СЛЕЖЕНИЕ за системной темой
  (хранится парой light/dark); `args.dark = "none"` выключает слежение. Пусто/nil = встроенная тема
  движка. Неизвестное имя → `unknown theme: X`. Ответ: `result.theme` (одиночная) ЛИБО
  `result.sync/light/dark` (пара).
- `theme.list` — `result.themes[]` + текущая (`theme` либо `sync/light/dark`).
  Windows: тема, файл которой не удалось разобрать, показывается в InfoBar ошибок окна и в `theme.list` не попадает.
- `restore.clear` — очистить захваченные foreground-команды всех открытых сессий (пины
  `session.restore` НЕ трогает — у них свой `clear`).

### debug (1, ВНЕ паритета)
- `debug.appearance` — тестовая команда (сервер отвергает её вне XCUITest-запуска; нет CLI-сабкоманды,
  отсутствует в каталоге/skill). В Windows-порте НЕ реализуется — исключение фиксируется в
  parity-манифесте как skip.

---

## 7. Специальные подсистемы

### session.status (агент-статус)
`args.status` ∈ `idle|active|completed|blocked` (иначе `invalid status`). Индикатор =
`{status, blink, autoReset}` (эфемерный, не persist).
- `args.blink` — пульсация глифа.
- `args.autoReset` — сбросить в idle, как только сессию посетили (selectSession сбрасывает autoReset-индикатор
  и на посещённой, и на покинутой сессии).
- `args.sound` — one-shot звук при установке (`default`/`beep` = системный звук; иное = именованный
  системный звук). Неизвестное имя → `unknown sound: X`, статус НЕ меняется.
- `args.color` (`#rrggbb`) — per-call тинт глифа; живёт до следующего `session.status` без цвета
  (nil = цвет из настроек). Read-back: `statusColor`.
- `args.shape` ∈ `circle|square|triangle|diamond|capsule|star` — per-call силуэт глифа, живёт как
  `color` (nil = форма из настроек для этого статуса, иначе круг). Read-back: `statusShape`.
- `args.pane` ∈ `left|right|scratch` — КАКОЙ пейн установил статус (дефолт left/main): ввод в другом
  пейне не сбрасывает статус, а user-initiated выбор сессии раскрывает блокирующий пейн.
  Read-back: `statusPane`.
- `args.paneID` — стабильный spawn-токен поверхности (баканный в шелл `$AGTERM_PANE_ID`, форвардится
  хуком): если резолвится по живым поверхностям сессии — ПЕРЕКРЫВАЕТ устаревший `pane` (промоут +
  ре-сплит меняет роли); пустой/неизвестный токен — fallback на `pane`. Скрипты задают `--pane`,
  `--pane-id` оставляют хуку.
- «Требуют внимания» (`needsAttention`) = `blocked` или `completed` — по ним шагает `session.go --to *-attention`.
- `statusChangedAt` — метка времени последней установки non-idle, ключ сортировки attention-списка.

### session.background (водяной знак / цвет фона)
`args.mode` ∈ `image|text|color|clear`:
- `color`: `args.color` (`#rrggbb`, обязателен) — СПЛОШНОЙ цвет фона терминала; без opacity (уважает
  оконную полупрозрачность из настроек, в отличие от image/text, которые рендерят пейн непрозрачным);
- `image`: `args.path` (PNG/JPEG; валидировать формат + существование + отсутствие control-символов);
- `text`: `args.text` (≤256 симв.) + опц. `args.color` (`#rrggbb`, дефолт = foreground терминала);
- оба: `args.opacity` (0..1), `args.fit` (`contain|cover|stretch|none`, дефолт contain),
  `args.position` (`center` + 8 якорей, дефолт center), `args.repeats` (bool).

Спек (`Codable`, persist в `SessionSnapshot`, LOSSY-декод):
```jsonc
{ "kind": "image|text", "imagePath": "...", "text": "...", "colorHex": "#rrggbb",
  "opacity": 0.15, "fit": "contain", "position": "center", "repeats": false }
```
`fit`/`position`/`kind` — типизированные enum (raw-значения совпадают с ключами движка). Только `imagePath`
остаётся свободным текстом и ре-валидируется на эмите. Читается назад в поле `background` узла дерева.

---

## 8. Настройки (`AppSettings`, отдельный `settings.json`, без версии)

Все поля Optional, nil = «дефолт движка». Часть — не ключи рендера, а app-level.

| Поле | Тип | Дефолт (nil) | Природа |
|---|---|---|---|
| `fontFamily` | string? | движок | рендер |
| `fontSize` | double? | движок | рендер |
| `theme` | string? | движок | рендер; app-дефолт при свежей установке = `"agterm"` (≠ nil) |
| `backgroundOpacity` | double? (0..1) | opaque | оконная композиция (не рендер) |
| `backgroundBlur` | int? (0..100) | нет | оконный blur; виден только при opacity<1 |
| `notificationsEnabled` | bool? | on | OS-баннер |
| `notificationBadgeEnabled` | bool? | on | бейдж непросмотренного |
| `compactToolbar` | bool? | компактный (`?? true`) | тулбар |
| `attentionButtonEnabled` | bool? | off | колокол в тайтлбаре |
| `activeStatusColorHex`/`blockedStatusColorHex`/`completedStatusColorHex` | string? | active `#DBD9E6`, blocked амбер, completed зелёный | цвет глифа статуса |
| `blockedStatusSoundName` | string? | None | звук при переходе в blocked |
| `configDirectory` | string? | `~/.config/agterm` | путь к `keymap.conf` |
| `mouseScrollMultiplier` | double? | **3** (эмитится всегда) | рендер-ключ |
| `rightClickPaste` | bool? | **on** (эмитится всегда) | рендер-ключ |
| `inactivePaneMuteStrength` | int? (0..10) | 5 (=0.4 opacity) | приглушение неактивного пейна |
| `sidebarBackgroundShift` | int? (0..10) | 5 (нейтраль) | тон фона сайдбара |
| `sidebarThemeStyle` | String? | off | Windows-only: строки сайдбара у workspace со своей темой в цветах этой темы: `row` (вся строка), `label` (блок вокруг имени); без ключа или другое значение = off |
| `restoreRunningCommand` | bool? | off | ре-ран команды при рестарте |
| `inheritGlobalGhosttyConfig` | bool? | off | подмешивать глобальный конфиг движка |

Формулы (host-free, перенести): `muteOpacity(s) = clamp(s,0,10)*0.08`;
`sidebarShiftAmount(s) = (clamp(s,0,10)-5)*0.06` (знаковый, ±0.30 на краях).

---

## 9. Keymap-формат (`keymap.conf`, kitty-flavored)

Построчный. Пустые строки и `#`-комментарии пропускаются (инлайн-`#` — комментарий, только если перед
ним пробел И он вне двойных кавычек). Нормализовать CRLF/CR → LF. Плохая строка → диагностика
`{line, message}` и пропуск (не рушит остальной файл). Токенизация по общему whitespace (пробел ИЛИ таб).

**Два глагола:**
- `map <chord> <action>` — переопределить шорткат встроенного действия. Только один chord (leader →
  диагностика). `<action>` — raw-имя `BuiltinAction` (ниже). Резолюция конфликтов ORDER-INDEPENDENT
  по финальному состоянию: chord, к которому резолвятся ДВА разных действия, — конфликт; override против
  чужого немувнутого дефолта проигрывает; два override → проигрывает поздний по файлу.
- `command "<name>" [chord] <shell...>` — кастомная команда. Имя в кавычках (можно пробелы). Токен после
  кавычки — chord, ЕСЛИ парсится И содержит модификатор (без модификатора → диагностика, строка становится
  palette-only, чтобы кастомный шорткат не затенял обычную клавишу терминала). Остаток — shell-строка
  (`/bin/sh -c`), с сохранением `{AGT_X}`-токенов дословно. Пустая shell-строка → диагностика.

**Chord:** модификаторы `cmd|ctrl|alt/opt|shift` + одиночный печатный символ или именованная клавиша
`tab|space|return|delete|left|right|up|down`, join через `+`, leader через `>` (напр. `ctrl+a>g`).
Стрелки ВЫРАЗИМЫ (добавлено в #291) с одним доп. правилом: built-in `map` не может биндить стрелку БЕЗ
модификатора (always-on key-equivalent на голой стрелке проглотил бы её в терминале/палитрах/дашборде).
НЕвыразимы по-прежнему: `+` (джойнер) и `>` (leader-сепаратор).
*Для Windows:* маппить `cmd`→Win/Ctrl по вкусу платформы — это UI-решение, формат не меняется.

**Зарезервированные chord'ы монитора** (не перебиндить): `ctrl+tab` с любыми модификаторами (MRU-свитчер);
`ctrl+1`/`ctrl+2` с Ctrl как единственным модификатором (фокус пейнов). Их отвергает и `map`, и кастом.

**Кросс-секционная валидация (один финальный проход):** активный набор встроенных chord'ов (override
иначе `defaultChord`); кастом-биндинг, чей ПЕРВЫЙ chord совпал со встроенным, ИЛИ любой chord = зарезервированный
монитор-chord → сбрасывается `shortcut=""` (остаётся в палитре) + диагностика; затем дублирующиеся
кастом-vs-кастом — оба сбрасываются.

**`BuiltinAction` (41 действие) и дефолтные chord'ы:**
| raw | default | raw | default |
|---|---|---|---|
| `new_window` | ⌘⌥N | `toggle_split` | ⌘D |
| `rename_window` | — | `toggle_scratch` | ⌘J |
| `delete_window` | — | `toggle_terminal_zoom` | ⌘⇧↩ |
| `new_workspace` | ⌘⇧N | `toggle_search` | ⌘F |
| `rename_workspace` | — | `toggle_sidebar` | ⌃⌘S |
| `delete_workspace` | — | `toggle_fullscreen` | ⌃⌘F |
| `new_session` | ⌘N | `select_theme` | — |
| `open_directory` | ⌘O | `toggle_flagged_view` | — |
| `rename_session` | — | `toggle_flag` | ⌘⇧F |
| `duplicate_session` | — | `focus_workspace` | — |
| `close_session` | ⌘W | `focus_left_pane` | ⌘⌥← |
| `reopen_recent` | ⌘⇧T | `focus_right_pane` | ⌘⌥→ |
| `undo_close` | ⌘Z | `previous_session` | ⌘⌥↑ |
| `clear_status` | — | `next_session` | ⌘⌥↓ |
| `increase_font_size` | ⌘+ * | `previous_attention_session` | ⌃⌥↑ |
| `decrease_font_size` | ⌘- | `next_attention_session` | ⌃⌥↓ |
| `reset_font_size` | ⌘0 | `first_session` | — |
| `quick_terminal` | ⌃` | `last_session` | — |
| `session_palette` | ⌃P | `dashboard` | ⌘⇧D |
| `command_palette` | ⌃⇧P | `custom_command_palette` | ⌃⇧O |
| `show_attention` | ⌃⇧I | | |

Шесть стрелочных действий теперь имеют НАСТОЯЩИЕ `defaultChord` (стрелки выразимы, см. выше) и
пере-`map`-ятся как любые другие. \* `increase_font_size` (⌘+) — единственный оставшийся невыразимый
в файле дефолт (`+` — джойнер): действует, но перезаписать его `map`-строкой на тот же ⌘+ нельзя.

**Токены `{AGT_X}`** (подставляются RAW в `/bin/sh -c`; те же значения ещё и как env `$AGT_X`, уже
шелл-квотированные — для недоверенного контента рекомендовать `$AGT_X`):
`AGT_SESSION_ID, AGT_SESSION_NAME, AGT_SESSION_PWD, AGT_WORKSPACE_ID, AGT_WORKSPACE_NAME,
AGT_WINDOW_ID, AGT_WINDOW_NAME, AGT_PANE, AGT_SELECTION, AGT_SOCKET`
(`AGT_PANE` — роль фокусного пейна: `left|right|scratch`, добавлен вместе с pane-адресацией).
`expand`: одиночный проход, `{...}` заменяется из таблицы; пустое значение → пустая строка; неизвестный
`{...}` остаётся дословно.

*Windows:* кастомные команды запускать через `cmd /c` или PowerShell — это выбор реализации; таблица
токенов и грамматика неизменны.

---

## 10. Терминальный движок на Windows (замена libghostty)

libghostty не переносим. Нужны эквиваленты только этих операций, которые дёргает модель/протокол:
- спавн шелла в cwd, опц. с командой-процессом (argv напрямую, без `sh -c`) → **ConPTY**
  (`CreatePseudoConsole`) + child-процесс;
- впечатать текст/keystrokes (`session.type`) → запись в вход PTY (Enter как CR);
- чтение выделения (`session.copy`) → уровень эмулятора/рендера;
- поиск по скроллбэку (`session.search`) → уровень эмулятора;
- foreground-процесс пейна (`tree.foreground`, restore) → на Windows нет прямого аналога
  `ghostty_surface_foreground_pid`; вариант — трекать дочерние процессы job-объекта / `NtQueryInformationProcess`;
- OSC 7 (cwd) и OSC 0/1/2 (title) → парсер эмулятора;
- водяной знак/тема/шрифт → уровень рендера (WebView2+xterm.js даёт большинство «из коробки»).

Рекомендация стека (под твой Rider/.NET): **ConPTY** + рендер **WebView2 + xterm.js** (быстрый надёжный
путь) ИЛИ нативный контрол; UI/сайдбар — **WinUI 3** или **Avalonia** (C#). Модель (§2–§4, §7) и
control-протокол (§5–§6) переписываются на C# почти дословно; `agtermctl.exe` — на System.CommandLine.

---

## 11. События (`events.read`) и что по-прежнему ВНЕ протокола

### Подписка на события — ЕСТЬ (добавлена в v0.16, #273)

Дизайн — **курсорный поллинг кольцевого буфера**, НЕ push-подписка. Транспортный инвариант §5
(«одно соединение = один запрос») СОХРАНЯЕТСЯ: `agtermctl events` (режим follow) просто поллит
свежими соединениями. Для Windows-порта это значит: named pipe не обязан держать долгоживущие
соединения — только кольцо + курсоры.

**Кольцо:** app-run-scoped, ёмкость **4096 событий** на один запуск процесса. У каждого запуска —
свой `run` (UUID); последовательность `seq` монотонна внутри run.

**Виды событий (`kind`):** `status` | `notify` | `session.created` | `session.closed` | `tree.changed`
(`tree.changed` — коалесцированный сигнал структурных изменений дерева).
**Стриминга вывода терминала НЕТ по-прежнему** — для чтения буфера есть `session.text`.

**Событие:**
```jsonc
{ "seq": 42, "ts": 1721900000.5, "kind": "status",
  "window": "<id?>", "workspace": "<id?>", "session": "<id?>",
  "payload": { "name": "?", "status": "?", "pane": "?", "blink": true,
               "color": "?", "shape": "?", "title": "?", "body": "?" } }   // все поля payload optional
```
Изменение ТОЛЬКО формы глифа (`--shape`) — это реальное изменение индикатора и ЭМИТИТ `status`-событие
(без поля `shape` потребитель не смог бы объяснить перемену).

**Запрос `events.read`:** без курсора = bootstrap-чтение от текущего хвоста; продолжение — парные
`args.run` + `args.after`; фильтр `args.kinds[]` (неизвестный kind — обычная control-ошибка, не отказ
декода); `args.limit` 1..1000 (дефолт у диспетчера).

**Ответ:** `result.events = { run, next, items[] }`, где `next` — глобальный seq, до которого кольцо
просканировано (ВКЛЮЧАЯ отфильтрованные записи) — его и передавать как следующий `after`.

**Ошибки курсора (фатальные, НИКОГДА не ребейзлайнятся молча):** `event run changed` (приложение
перезапустилось), `event cursor expired` (кольцо перезаписало позицию — потребитель отстал),
`event cursor is ahead of the current sequence`. Страница `events` присутствует и при курсорных
ошибках — чтобы вызывающий сам решил, ребейзлайниться ли.

**Для CLI-паритета:** `agtermctl events` печатает по человекочитаемой строке на событие (форматтер —
`EventFormatter.human`; поле payload без арма в нём молча выпадает из не-`--json` вывода — при
добавлении поля обновлять оба), `--json` — по голому объекту события на строку; флаги
`--kind` (повторяемый/через запятую), `--run`+`--after`, `--limit`.

### По-прежнему сознательно ВНЕ протокола

- Стриминг вывода/скроллбэка терминала — нет и не планируется (читать буфер: `session.text`).
- Установщики (CLI/hooks/skill) и чистый визуальный chrome — control-EXEMPT, драйвить нечего. Сам agent skill и его установщик (Help ▸ Install Agent Skill…, plugin marketplace) в scope порта, но вне wire-протокола: `docs/PLATFORM-NOTES.md`, раздел «Agent skill».
- `debug.appearance` — тестовая (XCUITest-only), в паритет не входит (см. §6).
- Отображение картинок — НЕ control-команда, а bundled shell-хелпер поверх kitty-графики (на Windows
  зависит от выбранного движка; xterm.js имеет свои механизмы).
```
