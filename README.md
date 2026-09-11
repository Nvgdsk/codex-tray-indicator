# Codex Tray Indicator 1.0.0

Невеликий Windows 11 tray-застосунок, який показує стан Codex CLI, запущеного в WSL2 Ubuntu. Runtime-стан передається лише через Windows Named Pipe у RAM — без `status.txt`, логів, SQLite, registry polling, screenshot/OCR чи періодичного опитування WSL.

## Встановлення

1. Запустіть `dist/CodexTraySetup.exe` подвійним кліком.
2. Після встановлення відкрийте звичайний Codex CLI в Ubuntu.
3. Введіть `/hooks`, перегляньте **Codex Tray Indicator** і виберіть **Trust**.
4. Tray-застосунок запускається після інтерактивного setup і автоматично стартуватиме разом із Windows.

Codex вимагає одноразового людського підтвердження non-managed hooks. Застосунок навмисно не встановлює небезпечний persisted bypass. Офіційна довідка: [Codex hooks](https://learn.chatgpt.com/docs/hooks).

Збірки наразі не мають Authenticode-підпису, бо сертифікат не наданий. Windows SmartScreen може показати стандартне попередження для нового unsigned застосунку.

## Стани

| Колір | Tooltip | Значення |
|---|---|---|
| Зелений `#22C55E` | `Codex: Ready` | Сесія активна, Codex чекає наступний prompt |
| Жовтий `#EAB308` | `Codex: Busy` | Codex обробляє поточний prompt |
| Червоний `#EF4444` | `Codex: Error` | Некоректний IPC-запит або помилка server loop |
| Сірий `#6B7280` | `Codex: Inactive` | Активних Codex-сесій немає |

Події відображаються так:

- `SessionStart` → Ready; `SessionStart` із source `compact` не скидає активний Busy;
- `UserPromptSubmit` → Busy;
- `Stop` або `Interrupt` → Ready;
- `SessionEnd` → Inactive, якщо інших активних сесій немає.

Момент `SessionEnd` визначається lifecycle-семантикою самого Codex. Якщо Codex ще вважає сесію активною після закриття terminal UI, сірий стан з’явиться після фактичної події `SessionEnd`.

Balloon notification **Codex finished** показується лише для справжнього переходу Busy → Ready після `Stop` або `Interrupt`. Синтетичні test-переходи не створюють notification.

## Tray-меню

- поточний read-only статус;
- **Start with Windows** — вмикає або вимикає HKCU startup value;
- **Notifications** — вмикає або вимикає повідомлення;
- **Reconnect / Test** — локально показує Ready без notification;
- **Exit** — коректно зупиняє pipe server і tray.

Дозволено лише один tray-процес поточного користувача через mutex `Local\CodexTray.Status.v1`.

## Архітектура та приватність

Codex hooks у `~/.codex/hooks.json` запускають той самий Windows `CodexTray.exe` у режимі `--hook`. Hook читає JSON зі stdin, залишає лише назву lifecycle-події, session ID, turn ID, source і timestamp та надсилає bounded frame до `CodexTray.Status.v1` з `PipeOptions.CurrentUserOnly`.

Prompt, response, transcript та інші текстові поля не передаються. Hook fail-open: неправильний JSON, відсутній tray або pipe timeout не блокують Codex і повертають exit code 0. Бюджет підключення — 250 мс; перевірений offline-виклик через WSL займає менше 750 мс.

Файли використовуються тільки для встановлення та конфігурації:

- `~/.codex/hooks.json` — офіційна Codex hook-конфігурація;
- `~/.codex/hooks.json.codextray.bak` — одноразовий backup початкового файлу;
- `HKCU\Software\CodexTray` — preferences і вибрана WSL-дистрибуція;
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexTray` — автозапуск.

На кожну runtime-подію нічого не записується на диск або в registry.

## WSL-виявлення

Installer виконує `wsl.exe --list --quiet`, ігнорує `docker-desktop` та перевіряє `command -v codex` і `codex --version` у кожній дистрибуції. На перевіреній машині знайдено Ubuntu з `/home/vasia/.local/bin/codex`, `codex-cli 0.154.0`.

Якщо Codex знайдено в кількох дистрибуціях і вибір ще не збережено, install завершується з поясненням замість довільного вибору. Значення можна попередньо задати як `WslDistribution` у `HKCU\Software\CodexTray`, після чого повторити setup.

## Діагностика

У PowerShell, використовуючи встановлений шлях:

```powershell
$codexTray = "$env:LOCALAPPDATA\Programs\CodexTray\CodexTray.exe"
& $codexTray --query-state
& $codexTray --hook-test busy
& $codexTray --hook-test ready
```

`--query-state` друкує `Inactive`, `Ready`, `Busy` або `Error`. `--hook-test` потребує активного tray та повертає 0 після IPC acknowledgement. Ці test-команди перевіряють транспорт, але не замінюють реальні Codex lifecycle hooks.

Якщо стан не змінюється:

1. переконайтеся, що tray запущений;
2. виконайте `--query-state`;
3. у Codex відкрийте `/hooks` і перевірте Trust;
4. перевстановіть setup — merge видалить лише handlers із integration ID `codex-tray-indicator-v1` і не дублюватиме їх.

## Видалення

Відкрийте **Settings → Apps → Installed apps → Codex Tray Indicator → Uninstall**. Uninstaller спочатку зупиняє безвіконний tray, потім атомарно видаляє лише власні п’ять handlers, startup value і application settings. Сторонні hooks та `hooks.json.codextray.bak` зберігаються.

## Збірка й тести

Підготовка локальних build tools:

```powershell
.\scripts\bootstrap-build-tools.ps1
```

Повна release-збірка:

```powershell
.\scripts\build-release.ps1
```

Повторювані інтеграційні перевірки:

```powershell
.\scripts\test-wsl-ipc.ps1 -Cycles 100
.\scripts\test-hook-install.ps1
```

Перший скрипт доводить реальний Ubuntu WSL → Windows Named Pipe round-trip, 100 Busy/Ready пар, restart до Inactive, fail-open без server та відсутність runtime-файлів. Другий працює лише в regex-валідованому `/tmp/codex-tray-tests-<guid>`, перевіряє atomic install/reinstall/uninstall, backup і збереження foreign hooks, після чого видаляє свій test-directory.

Ці автоматизовані перевірки не доводять видимі переходи реального Codex prompt. Після `/hooks` Trust вручну перевірте: старт/restore → зелений, prompt → жовтий, нормальне завершення → зелений + одне notification, другий prompt → жовтий, Ctrl+C → зелений, завершення сесії → сірий.
