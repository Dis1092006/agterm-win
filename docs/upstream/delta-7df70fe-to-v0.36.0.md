# Изменения upstream agterm после пина `7df70fe`

Снимок на 2026-10-10. Сравниваются пин паритета `7df70fe` (2026-07-24, v0.16.1 + 18 коммитов) и `origin/master` `ed831ab` (v0.36.0, 2026-10-09). Между ними 578 коммитов.

Дальше agterm-win не идёт за историей upstream по порядку. Возможности берутся по одной, и этот файл служит каталогом для выбора: что есть в upstream, какие команды и поля к этому относятся, что ломает уже сделанное.

Источники:

- `CHANGELOG.md` upstream, секции v0.17.0–v0.36.0. Номера `#NNN` ниже – это PR upstream.
- `docs/upstream/surface-v0.36.0.json` – вывод `tools/extract_surface.py` по `ed831ab`, формат тот же, что у `parity/surface.json`.

## Объём

| | пин `7df70fe` | `ed831ab` | разница |
|---|---|---|---|
| Команды протокола | 66 | 110 | +44 |
| ControlArgs | 53 | 86 | +33 |
| ControlResult | 14 | 32 | +18 |
| Узел сессии в `tree` | 28 | 49 | +21 |
| Узел окна в `tree` | 9 | 10 | +1 |
| BuiltinAction | 41 | 49 | +8 |
| `{AGT_X}` токены | 10 | 12 | +2 |
| AppSettings | 37 | 50 | +13 |
| SessionSnapshot | 15 | 23 | +8 |

Ничего не удалено, кроме `Snapshot.focusedWorkspaceID` (см. раздел 11).

Из v0.17.0 в пин уже вошли: стрелки в аккордах (#291), формы глифов статуса (#292), тултипы статуса (#283), сайдбар только в активном окне (#285), вопросы Codex в любом месте сообщения (#282), сообщение о подавленном баннере. Dock-меню (#284) пришло после пина.

## 1. Live sessions (zmx): процессы переживают перезапуск

- Режим восстановления `live` рядом с `fresh` и `rerun`: `restore.mode`, настройка `restoreMode`. Каждая основная и split-панель работает через мультиплексор zmx. Выход из приложения рвёт только соединение, процесс продолжает работать, следующий запуск переподключается к нему. Режим фиксируется на время жизни процесса #515.
- `zmx.list`, `zmx.prune`, `zmx.kill`, `zmx.reset` (Reset Live Sessions #587, #650), `zmx.screen` (экран демона по имени #694).
- `restore.capture` – заполнить captured-команды по запросу, а не только при выходе #452.
- Session host – вспомогательный процесс, который держит атрибуцию TCC за приложением #574. В `tree`: `backedByZmx`, `liveAttribution`, `splitLiveAttribution`, `liveReset`.
- `session.restart --command LINE (--pane-id | --pane)` – новый login shell в той же панели, `--pane-id` и `AGTERM_*` сохраняются #699. Без `--command` повторяет argv foreground-программы, в ответе `restart.replayedArgv` #701.
- Закрытие панели шлёт hangup foreground-задаче #699.
- Команда `session new --command` длиннее 1024 байт идёт в zmx как payload, а не через pty #533.

**Windows:** zmx нет. Нужен свой демон, который держит ConPTY, или Adapt с `{"ok":false,...}`. Самый крупный блок; разделы 2 и часть 9 на нём стоят.

## 2. Remote sessions по ssh (зависят от раздела 1)

- `zmx.tree HOST`, `zmx.attach HOST SESSION [--window]` – сессия с другой машины появляется в сайдбаре как обычная #524, #566.
- Presentation stream `zmx.present`: статус, уведомления, HUD, ask и overlay зеркалятся на подключившуюся машину; роли `presenter` и `mirror`. В `tree`: `presentation`, `presenters`, `remoteOverlays` #629, #630.
- Лидер панели: `session.lead`, `surfaces[].lead`. Только лидер задаёт размер #635.
- Раскладка сплитов и `session.context` зеркалятся с origin #634, #636.
- Reconnect после выхода ssh с кодом 255; причина и счётчик в `tree` как `reconnect`. Опции `ServerAliveInterval=5`, `ServerAliveCountMax=2`. Повтор сразу после wake #655, #683, #687, #694.
- `remotes.conf` и File ▸ Attach Remote, действие `attach_remote` на ⌘⇧R #710.
- События `remote.opened` и `remote.closed` #610. Токен `AGT_SESSION_HOST` #576.
- `agtermctl terminfo install DESTINATION` #609.
- В `tree`: `remoteHost`. В заголовке окна иконка облака и ssh-цель #572.

## 3. HTML-оверлеи (WebView)

- `session overlay open --html FILE | --url URL` в слотах session, floating и pane #659.
- Флаги: `--js`, `--navigation`, `--cwd DIR` (доступ к файлам), `--chromeless` #678, `--persistent` (общее хранилище cookies, localStorage и IndexedDB) #689, `--browse` (переходы на другие сайты) #708, `--block` #672.
- `session.overlay.reload`, `session.overlay.navigate back|forward|browser|finder` #663, `session.overlay.submit`, `session overlay result --page ID`.
- Мост к control API: `data-agterm="<cmd>"` без JS, `agterm.request(cmd, {target, args})` при `--js`. Команды без target действуют на свою сессию, окно или панель. Стриминговые команды запрещены #672.
- `window.close()` на `--js`-странице закрывает оверлей, результат `dismissed` #704.
- Зум страницы через ⌘+/⌘-/⌘0 и `font inc|dec|reset`, одно значение на приложение: настройка `htmlOverlayZoom` #670.
- `browser.clear`, `browser.links [browser|overlay]`, настройка `linkOpenMode`: ссылка из терминала открывается в браузере или в оверлее #708.
- Страница получает тему терминала через CSS-переменные.
- В `tree`: `htmlOverlays[]` с `id`, `page`, `zoom`, `persistent`, `browse`. В ответе: `pageID`, `pageOutcome`.

**Windows:** WebView2 уже есть в `AgTerm.Xterm`.

## 4. UI-примитивы для скриптов (pick реализован 2026-10-10, ask реализован 2026-10-12, session.hud реализован 2026-10-14)

`pick`, `ask` и `session.hud` реализованы, см. `docs/PLATFORM-NOTES.md`.

- **pick** – `pick.open`, `pick.result`, `pick.cancel`. Варианты из stdin строками или JSON, блокирующий и неблокирующий режимы, `allowCustom`, `--query`, `--select ID`. Сопоставляется только label; при пустом запросе сохраняется порядок от вызывающего; пустой список разрешён. В `tree`: `pickPending` #316, #339, #597.
- **ask** – `ask.open`, `ask.result`, `ask.cancel`. 1–6 кнопок, результат: кнопка, `escaped` или `cancelled`. Стиль `terminal` привязан к сессии или панели и закрывает только её; стиль `gui` модален для окна и делит слот с `pick`. В `tree`: `ask` у сессии, `askPending` наверху #556, #561.
- **HUD** – `session.hud.open`, `.update`, `.close` #361. Позиция по сетке 3x3, `top`/`bottom` остаются алиасами #386. `--pane`, `--pane-id` #536. `--markdown` до 4096 символов, `--font-size`, `--file` #641. `--hide-after` #613. `--sticky`, `--no-frame` #709. Ссылки в markdown открываются по command-click #707. В `tree`: `hud`.

## 5. Сплиты и адресация панелей

- Горизонтальный сплит: `axis` у `session.split`, `top`/`bottom` как алиасы, `splitAxis` в `tree` и в снимке. Новое действие `toggle_horizontal_split` на ⌘⇧D. **Dashboard переехал с ⌘⇧D на ⌘⇧G** – меняется дефолтный аккорд уже реализованного действия #427.
- `session.split.close` – закрыть split-панель целиком #421. `hasSplit` в `tree` отличает скрытый сплит от отсутствующего.
- `session.swap` – обмен терминалов между панелями; ось и ratio остаются #513.
- `--pane-id` (стабильный `$AGTERM_PANE_ID`) у `session type`, `session text`, `surface cursor`; `surfaces[].paneID` в `tree`; `result.pane` #698. Токен `AGT_PANE_ID` #603.
- Overlay на одну панель: `--pane left|right` у `session overlay open|close|result`, в `tree` `paneOverlays` #343.
- Фон на панель: `session background ... --pane`, `paneBackgrounds` в `tree` и в снимке #647.
- `session.paste --pane` #529. `surface.cursor` #451. `session.overlay.copy`, `session.overlay.text` #437.
- Dashboard принимает `id:left` и `id:right` #334.
- Двойной клик по разделителю выравнивает сплит 50/50 #357.

## 6. Навигация, окна, сайдбар

- `window.go --to next|prev`, действия `previous_window` и `next_window` (без аккорда) #591.
- `window.minimize on|off|toggle`, `window new --minimized`, `minimized` в `window list` #294. `window resize` возвращает применённый размер #566.
- `workspace.go --to next|prev`, действия `previous_workspace`, `next_workspace`, `toggle_workspace_collapse` (без аккорда) #436.
- Фокус как набор workspace: `workspace.filter`, `workspace focus add`, действие `toggle_workspace_filter` #297.
- Раскладка flagged view: `sidebar.flagged-layout flat|tree|plain|toggle`, настройка `flaggedViewLayout` #621, #718.
- `sidebar.width` #512.
- Attention-список (⌃⇧I) по всем окнам #596.
- Куда добавлять новую сессию: в конец или после текущей, настройка `newSessionPlacement` #665.
- Клик по строке workspace разворачивает её: `workspaceRowClickExpands` #342.
- Dock-меню #284, New Window в нём #319.
- Quick terminal – плавающая панель с глобальным hotkey #441, размер в процентах экрана `quickTerminalSizePercent` #459.

## 7. Keymap (keymap.list и keymap.run реализованы 2026-10-26)

`keymap.list`, `keymap.run` и пропуск повторного имени `command` реализованы, см. `docs/PLATFORM-NOTES.md`. Не взяты: `menu[]`, альтернативы через `|`, `--repeat`, запуск из сессии HTML-страницы. `--error-*` взяты.

- F1–F20 с модификаторами и без, как leader, как `global-hotkey` #619.
- Альтернативы через `|` в одной строке `map` или `command`; leader-последовательности для builtin #420.
- `--repeat` для leader (как tmux `bind -r`), `repeats` в `keymap list` #682.
- `global-hotkey` #441.
- `keymap.list` – во что реально разрешились биндинги, меню, диагностика #301. `keymap.run NAME` #694. Реализованы: `chord` в написании Windows, без `menu[]`.
- `--error-hud`, `--error-position`, `--error-pane` у custom-команд #613, #622. Реализовано.
- Аккорды разрешаются по раскладке: на не-латинской раскладке биндинг по физической клавише #310. ctrl+j и ctrl+k в палитрах работают на кириллице #705.
- Две строки на один аккорд больше не решаются порядком в файле #444.
- Повтор имени `command` пропускается с диагностикой #582. Реализовано.
- Builtin забирает аккорд у стандартного пункта меню и возвращает при освобождении #680.

## 8. События и hooks

- `hooks.conf` (`on <kind> <command>`), JSON события в stdin, `AGT_*` в окружении; `hooks.list`, `hooks.reload` #607.
- Новые события: `pane.split`, `pane.scratch` #607, `session.selected` с `previous` #694, `remote.opened`, `remote.closed` #610. У `status` появилось `previous`.
- `agtermctl events` после отказа соединения повторяет попытки (250 мс – 4 с) с того же курсора, выход после ~30 с #716. Backlog сокета 128 вместо 8 #717.

## 9. Agent status

- `statusReset`: on first key, on Enter, disabled. `session type` тоже его учитывает #585.
- `statusChangedAt` в `tree`, ставится на каждый принятый set #465, #617.
- Blocked-статус панели в сплите не перезаписывается соседом #523.
- Новые интеграции: OpenCode #289, OpenCode 2 #696, Copilot (рецепт) #464. Claude hooks ставят `blocked` при открытии permission prompt #721.
- `foregroundShell`, `splitForegroundShell` в `tree` #525.

## 10. Прочее в control API

- `version` #476.
- `session.context` – строка о назначении сессии в заголовке и в `tree`, переживает рестарт, `session.duplicate` её не копирует #522.
- `realized` в `tree` #417, `splitCwd` #566, `splitCommandWait`.
- `agtermctl --json` печатает строку сервера без перекодирования #628.
- `agtermctl clipboard set [TEXT]` – подкоманда CLI, в списке команд протокола её нет #722.
- При занятом сокете `agtermctl` различает "приложение не запущено" и "владелец есть, но не принимает" #633.
- Workspace name в заголовке окна #601; кнопка custom-команд в заголовке #582. Список видимых элементов – `shownInterfaceElements`.
- Шрифт палитр и picker – `interfaceFontSize` #367. Курсор – `cursorStyle`, `cursorBlink` #487.
- Окно приветствия при первом запуске – `welcomeShown` #353.

## 11. Персистентность и настройки (ломающее)

- `Snapshot`: `focusedWorkspaceID` заменён на `focusedWorkspaceIDs` и `focusEnabled`. Старые `workspaces.json` надо мигрировать при чтении.
- `SessionSnapshot`, новые поля: `paneIdentity`, `splitPaneIdentity`, `hasSplit`, `splitAxis`, `context`, `splitInitialCommand`, `splitCommandWait`, `paneBackgrounds`.
- Нераспознанное значение опционального поля в `workspaces.json` превращается в nil, а не роняет весь снимок #363.
- `AppSettings`, новые поля: `cursorBlink`, `cursorStyle`, `flaggedViewLayout`, `htmlOverlayZoom`, `interfaceFontSize`, `linkOpenMode`, `newSessionPlacement`, `quickTerminalSizePercent`, `restoreMode`, `shownInterfaceElements`, `statusReset`, `welcomeShown`, `workspaceRowClickExpands`.

## 12. Изменения поведения уже реализованных команд (реализовано 2026-10-10)

Реализовано 2026-10-10; см. раздел PLATFORM-NOTES `Upstream behavior adopted beyond the pin`.

Раздел реализован; каждое принятое изменение записано в `docs/PLATFORM-NOTES.md` с sha upstream.

Эти пункты меняют то, что в agterm-win уже сделано по пину. Они не зависят от новых возможностей и могут понадобиться при любом обновлении.

- `session text --pane-id` с неизвестным id отвечает `unknown pane id: <id>`, раньше читал видимую панель и отвечал ok #698.
- `session.paste`, `session.selectall`, `session.copy` без реальной поверхности отвечают `session not realized` #425.
- `session type`, `quick type`: NUL отклоняется `6bc8a0b7` (issue #455); невалидный UTF-8 в `--stdin` отклоняется #521; финальный Return идёт через 10 мс после текста #681.
- `session.overlay.open --size-percent` вне 1..100 отклоняется #498.
- `invalid pane: X` сообщает диспетчер для `session.type`, `session.text` и `font` (#530). Алиасы `primary`, `split`, `top`, `bottom` появились вместе с горизонтальными сплитами (#427) и в это изменение не входят.
- `session.type` на основной панели без `--select` отвечает `session not realized` вместо `session not realized; use select`, а `--select` выбирает сессию только когда поверхность не готова (#351).
- Новый workspace становится целью до смены выбранной сессии; `workspace select` перенацеливает всегда #329.
- `window new` отвечает после того, как окно присоединилось; `window list` не отдаёт устаревший кеш; минимизированное окно перестаёт быть frontmost; `window select` работает при неактивном приложении #294.
- Ошибка custom-команды в удалённом каталоге: `session directory <path> no longer exists`.
- Значения с переводом строки в именах и `--cwd` санитизируются до попадания в `{AGT_*}` #354.
- Custom-команда из правой панели получает cwd правой панели в `AGT_SESSION_PWD` #482.
- Записанный запуск команды с управляющими байтами отклоняется, а не повторяется #457.

## 13. Вне паритета

- macOS: TCC и entitlements, Accessibility и Dictation, локаль на macOS 27, освобождение GPU-поверхностей скрытых панелей, обновления libghostty и Zig, Homebrew cask, Reduce Motion.
- Без кода: рецепты в `cookbook/`, документация.
- Bundled agent skill и plugin marketplace для Claude Code и Codex больше не вне паритета: порт держит свою адаптированную копию skill и свой marketplace (`docs/PLATFORM-NOTES.md`, раздел «Agent skill»).

## Открытые вопросы

- `session.overlay.job.run` есть в списке команд. Похоже на внутреннюю команду для оверлеев, которые запускает presenter (раздел 2). Перед включением в паритет её надо прочитать в `ControlProtocol.swift`.

## Как пересчитать

```
git -C <agterm-clone> fetch origin
git -C <agterm-clone> log --oneline 7df70fe..origin/master
```

Снимок поверхности любой ревизии, без checkout:

```
python tools/extract_surface.py --agterm "<agterm-clone>" --ref "origin/master" --out "surface-new.json"
```

Скрипт ищет типы во всех файлах `agtermCore`, поэтому перенос типа между файлами его не ломает. Вычисляемые свойства (`effectiveX: T { ... }`) в снимок не попадают. Результат сравнивается с `parity/surface.json`.
