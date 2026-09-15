# Codex Tray Indicator 1.3.0

Невеликий Windows 11 tray-застосунок, який показує стан Codex CLI, запущеного в WSL2 Ubuntu. Runtime-стан передається лише через Windows Named Pipe у RAM — без `status.txt`, логів, SQLite, registry polling, screenshot/OCR чи періодичного опитування WSL.

Версія 1.1.0 також дублює статус на **Turing Smart Screen 3,5″ (Revision A), 320×480** через USB COM-порт. Екран показує великий кольоровий індикатор, той самий напис Ready / Busy / Error / Inactive та коротке пояснення.

Версія 1.2.0 додає **Pixel shift animation**: увесь блок статусу повільно переміщується з відскоком від країв, щоб світлі елементи не залишалися постійно на тих самих пікселях.

Версія 1.3.0 додає **анімованого піксельного робота Codex** замість кола: Ready — махає рукою й моргає; Busy — друкує на клавіатурі; Inactive — спить із «Z»; Error — піднімає руки й блимає червоним індикатором. Текст і колір статусу залишаються такими самими, як у tray.

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
- стан USB-підключення та **USB screen (Turing 3.5")** — Automatic, Off, вибір COM-порту, Orientation, Pixel shift animation, Animated character та Reconnect screen;
- **Reconnect / Test** — локально показує Ready без notification;
- **Exit** — коректно зупиняє pipe server і tray.

Дозволено лише один tray-процес поточного користувача через mutex `Local\CodexTray.Status.v1`.

## USB-екран Turing 3,5″

1. Під’єднайте екран і закрийте програму виробника або інші програми, що використовують його COM-порт.
2. Запустіть оновлений `dist/CodexTray.exe` або встановіть `dist/CodexTraySetup.exe`.
3. За замовчуванням **Automatic** вибирає єдиний підключений пристрій із USB serial `USB35INCHIPSV2` або `VID_1A86&PID_5722`. На перевіреній машині це **COM3**. Якщо пристроїв кілька, виберіть потрібний COM-порт у tray-меню.
4. **Orientation** дозволяє вибрати вертикальний 320×480, горизонтальний 480×320 або їх перевернуті варіанти. За замовчуванням екран вертикальний.
5. **Off** вимикає екран і звільняє порт. Вихід із tray також вимикає екран.

**Pixel shift animation** увімкнена за замовчуванням. Раз на 10 секунд блок CODEX, кольорове коло, статус та пояснення зміщується на 6 пікселів. Рух охоплює вільну площу екрана й змінює напрям біля країв; у вертикальному режимі більший діапазон руху по вертикалі, у горизонтальному — по горизонталі. Світла смуга та нерухомий footer прибрані, всі видимі елементи рухаються. Новий кадр повністю очищує попереднє положення, а 24-піксельні поля не дозволяють обрізати написи. Вимкнути рух можна в **USB screen → Pixel shift animation**.

**Animated character** також увімкнений за замовчуванням. Робот має незалежний таймер із кроком 500 мс і продовжує рух навіть при вимкненому Pixel shift. Персонаж намальований у програмі як 28×28 пікселів, збільшених рівно вчетверо. Між змінами статусу та повільними зміщеннями на USB передається лише його ділянка 112×112 — близько 25 КБ замість 300 КБ повного екрана. Кадри не накопичуються: після тривалого запису worker відразу переходить до актуальної пози. Вимкнення **USB screen → Animated character** повертає звичайне кольорове коло.

Таймер використовує монотонний час і продовжує рух при незмінному Ready, Busy, Error або Inactive. Зміни статусу надходять одразу, без очікування наступного кроку анімації. Після затримки або USB-перепідключення worker показує актуальний кадр, пропускаючи застарілі кроки. Анімація зменшує час відображення нерухомих елементів; це не апаратна гарантія від залишкового зображення чи вигорання.

USB-виведення використовує той самий стан, що й tray: включно з початковим Inactive, реальними hooks, помилками IPC та `--hook-test`. Повідомлення Busy → Ready залишаються звичайними Windows notifications. На USB не передаються prompts, responses або ідентифікатори сесій.

Усі serial-записи й формування зображень працюють в окремому worker. Черга зберігає тільки останній стан, а нові кадри надсилаються при зміні статусу, кроці анімації або перепідключенні. Кожні 3 секунди worker перевіряє лише наявність USB-порту та відправляє коротку команду screen-on для перевірки відкритого з’єднання, якщо новий кадр не потрібен; він не опитує Codex або WSL. Після від’єднання чи зайнятого порту підключення повторюється автоматично. Помилка екрана показується окремо в меню і не змінює статус Codex.

Транспорт: 115200, 8N1, DTR/RTS; Revision A, packed 6-byte commands, RGB565 little endian. Яскравість встановлена на 25%. Референси протоколу: [TuringSmartScreenLib](https://github.com/usausa/turing-smart-screen), [апаратні ревізії](https://github.com/mathoudebine/turing-smart-screen-python/wiki/Hardware-revisions). Інші ревізії 3,5″ (наприклад XuanFang / Revision B) потребують іншого драйвера.

Перевірка на фізичному пристрої (спочатку **USB screen → Off** у tray):

```powershell
.\scripts\test-usb-screen.ps1 -Port COM3
# Горизонтальний режим:
.\scripts\test-usb-screen.ps1 -Port COM3 -Orientation Landscape
```

Скрипт надсилає Inactive → Busy → Error → Ready, перевіряє кілька положень Pixel shift, а також часткові кадри персонажа в усіх чотирьох статусах, і відновлює попередній стан після кожного сценарію. Прев’ю зберігаються в `artifacts/usb-screen-previews`. Serial-запис доводить передачу байтів; правильну орієнтацію та видимі переходи перевірте на самому екранчику. Після тесту поверніть **Automatic** або потрібний COM-порт у tray. Hardware-тести пропускаються під час звичайної збірки.

## Архітектура та приватність

Codex hooks у `~/.codex/hooks.json` запускають той самий Windows `CodexTray.exe` у режимі `--hook`. Hook читає JSON зі stdin, залишає лише назву lifecycle-події, session ID, turn ID, source і timestamp та надсилає bounded frame до `CodexTray.Status.v1` з `PipeOptions.CurrentUserOnly`.

Prompt, response, transcript та інші текстові поля не передаються. Hook fail-open: неправильний JSON, відсутній tray або pipe timeout не блокують Codex і повертають exit code 0. Бюджет підключення — 250 мс; перевірений offline-виклик через WSL займає менше 750 мс.

Файли використовуються тільки для встановлення та конфігурації:

- `~/.codex/hooks.json` — офіційна Codex hook-конфігурація;
- `~/.codex/hooks.json.codextray.bak` — одноразовий backup початкового файлу;
- `HKCU\Software\CodexTray` — preferences і вибрана WSL-дистрибуція;
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexTray` — автозапуск.

На кожну runtime-подію нічого не записується на диск або в registry.

`UsbScreenPort` (`AUTO`, `OFF` або `COM3` тощо), `UsbScreenOrientation` (0–3), `UsbScreenAnimationEnabled` та `UsbScreenMascotEnabled` (0/1) зберігаються у preferences тільки при зміні налаштувань у меню. USB-виявлення читає Windows device registry; стан Codex через registry не передається.

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
