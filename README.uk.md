[English](README.md) | [Українська](README.uk.md)

# Codex Tray Indicator

Невеликий Windows tray-застосунок, який показує lifecycle-стан Codex CLI у WSL.
За бажанням дублює індикатор на USB-екрані Turing 3,5 дюйма Revision A.
Індикатор не показує й не зберігає текст prompts або responses.

Код ліцензовано за [MIT](LICENSE). Дивіться [історію змін](CHANGELOG.md).
v1.3.0 готується до першого публічного unsigned release; посилання на завантаження
запрацюють після публікації репозиторію та релізу.

<a id="supported-scope"></a>

## Підтримувані конфігурації

- Windows 11 x64, звичайний Windows-акаунт і WSL2 з увімкненим запуском Windows EXE.
- Перевірена WSL-конфігурація: Ubuntu з Codex CLI `0.154.0`. Codex має підтримувати
  hooks `SessionStart`, `UserPromptSubmit`, `Stop`, `Interrupt` та `SessionEnd`.
  Сумісність з іншими версіями CLI й дистрибутивами не гарантована.
- Один вибраний WSL-дистрибутив зі стандартним `~/.codex/hooks.json` Linux-користувача.
  Власні каталоги конфігурації Codex автоматично не виявляються.
- Необов’язковий екран: Turing Smart Screen 3,5 дюйма, Revision A, 320×480.
  Revision B/XuanFang та інші протоколи екранів не підтримуються.
- Нативний Windows Codex CLI і Codex desktop app **не відстежуються**.

Перевірте, що installer зможе знайти Codex; замініть `Ubuntu` на ваш дистрибутив:

```powershell
wsl.exe --list --verbose
wsl.exe -d Ubuntu --exec sh -lc 'command -v codex && codex --version'
```

Якщо друга команда не працює, виправте WSL/PATH до встановлення застосунку.

<a id="installation"></a>

## Встановлення

1. Відкрийте [Latest release](../../releases/latest).
2. Завантажте `CodexTraySetup.exe` та `SHA256SUMS.txt` з одного релізу.
   Для portable-варіанта також завантажте `CodexTray.exe`.
3. [Перевірте хеші](#checksums) і прочитайте [попередження про unsigned release](#unsigned-release).
4. Запустіть `CodexTraySetup.exe`. Setup встановлює застосунок для поточного користувача
   в `%LOCALAPPDATA%/Programs/CodexTray`, інтегрує один WSL-дистрибутив
   і вмикає автозапуск. Права адміністратора не потрібні.
5. Залиште запуск застосунку вибраним наприкінці інтерактивного setup.
   Знайдіть індикатор в області сповіщень Windows, включно з прихованими іконками.
6. Відкрийте Codex у вибраному WSL-дистрибутиві й підтвердьте [Trust для hooks](#hook-trust).

Обидва release EXE є self-contained: користувачу **не потрібно окремо встановлювати .NET runtime**.
Setup не встановлює WSL або Codex, не авторизує Codex-акаунт і не налаштовує інші дистрибутиви.

Для portable-варіанта залиште `CodexTray.exe` у постійному каталозі. У Windows PowerShell:

```powershell
$codexTray = 'C:/Apps/CodexTray/CodexTray.exe'
& $codexTray --install
Start-Process -FilePath $codexTray -WindowStyle Hidden
```

Замініть шлях на реальне розташування файла, потім підтвердьте Trust.
Переміщення portable EXE ламає шляхи hooks і startup: [видаліть стару інтеграцію](#uninstall)
до переміщення та встановіть знову з нового шляху.

<a id="hook-trust"></a>

## Перегляд hooks і підтвердження Trust

У Codex CLI введіть `/hooks`. Перегляньте handlers, команда яких закінчується
`--hook --integration-id codex-tray-indicator-v1`: вони повинні посилатися на
встановлений вами Windows EXE. Виберіть **Trust** для handlers індикатора.

Codex пропускає нові або змінені non-managed hooks до перегляду їхнього точного визначення.
Після перевстановлення або зміни шляху повторно перегляньте hooks, які очікують підтвердження.
Дивіться [офіційну документацію OpenAI](https://learn.chatgpt.com/docs/hooks).

Installer не обходить hook trust. Після підтвердження відкрийте нову Codex-сесію,
щоб її `SessionStart` надійшов до запущеного tray. Синтетичні тести не надають Trust.

<a id="tray-states"></a>

## Стани tray

| Колір | Tooltip | Значення |
|---|---|---|
| Зелений `#22C55E` | `Codex: Ready` | Є активна сесія, немає активного turn |
| Жовтий `#EAB308` | `Codex: Busy` | Принаймні одна сесія обробляє turn |
| Червоний `#EF4444` | `Codex: Error` | Помилка IPC/протоколу або застосунку; це не оцінка відповіді Codex |
| Сірий `#6B7280` | `Codex: Inactive` | Від запуску tray немає відомих активних сесій |

`SessionStart` → Ready; `UserPromptSubmit` → Busy; відповідний `Stop` або
`Interrupt` → Ready; `SessionEnd` видаляє сесію. Якщо інша сесія ще Busy,
спільний стан залишається Busy. `SessionStart` під час compaction не скидає Busy.

Стан існує лише в RAM. Після перезапуску tray починає з Inactive і не може відновити
старі сесії до надходження нових hooks. Закриття термінала саме по собі не доводить
подію `SessionEnd`: сірий стан з’являється після фактичної lifecycle-події Codex.

Налаштування відкриваються правим кліком по іконці. **Reconnect / Test** встановлює
синтетичний Ready і перепідключає USB; не відновлює відсутні hooks і не перевіряє реальний Codex turn.

<a id="notifications"></a>

## Сповіщення

**Notifications** вмикає або вимикає Windows balloon notifications.
**Codex finished** з’являється лише для справжнього спільного переходу Busy → Ready
через `Stop` або `Interrupt`. Синтетичні тести не сповіщають; завершення одного
turn, коли інший залишається Busy, теж не сповіщає.
Налаштування сповіщень Windows або Do Not Disturb можуть приховати повідомлення.

<a id="startup"></a>

## Автозапуск із Windows

**Start with Windows** перемикає startup entry поточного користувача в
`HKCU/Software/Microsoft/Windows/CurrentVersion/Run/CodexTray`.
Setup вмикає його лише після успішного запису hook-конфігурації.
**Exit** зупиняє tray/pipe і звільняє USB, але не видаляє інтеграцію або автозапуск.

<a id="usb-screen"></a>

## Налаштування USB-екрана

Екран необов’язковий. Без підтримуваного пристрою tray продовжує працювати.

1. Під’єднайте Revision A і закрийте програму виробника та інші програми, які використовують COM-порт.
2. Правий клік по tray → **USB screen (Turing 3.5")**.
3. **Automatic** вибирає єдиний відповідний пристрій: USB serial `USB35INCHIPSV2`
   або hardware ID `VID_1A86&PID_5722`. Якщо відповідних пристроїв кілька,
   виберіть COM-порт вручну. `COM3` у прикладах не є універсальним портом: використовуйте свій.
4. Виберіть **Orientation**: Portrait 320×480, Landscape 480×320 або перевернутий варіант.
5. **Reconnect screen** негайно повторює підключення. **Off** вимикає виведення та звільняє порт.

**Pixel shift animation** увімкнена за замовчуванням: блок статусу переміщується на 6 пікселів
раз на 10 секунд і відбивається від безпечних меж.
Це не апаратна гарантія від залишкового зображення або вигорання.

**Animated character** теж увімкнений за замовчуванням: піксельний робот у Ready махає
і моргає, у Busy друкує, в Inactive спить, в Error піднімає руки.
Персонаж оновлюється кожні 500 мс частковими кадрами; вимкнення повертає кольорове коло.

Під станом рядок **Weekly remaining: 72%** показує залишок тижневого ліміту
Codex зі смугою залишку. Індикатор читає дані акаунта, у який виконано вхід у Codex
вибраного WSL-дистрибутива, при запуску та щохвилини, поки USB-вивід увімкнений.
Читання працює незалежно від анімації й не запускає prompts.
Залишок обчислюється як `100 - usedPercent` для семиденного ліміту Codex.
Якщо тижневий ліміт відсутній, дані прострочені, виникла помилка входу/API або
дистрибутив не вибраний, екран показує **—** та порожню смугу.
Помилка читання ліміту не змінює lifecycle-стан. При залишку 25% смуга стає жовтою,
а при 10% — червоною. Дані ліміту зберігаються лише в пам’яті.

Рендеринг і serial-записи працюють в окремому worker, який зберігає лише останній стан у черзі.
USB-перевірки й повторні підключення не опитують Codex або WSL.
USB-помилки показуються окремо в меню та не змінюють стан Codex.
Preferences зберігаються в `HKCU/Software/CodexTray`.
Екран отримує графіку статусу й ліміту, а не prompts, responses чи session IDs.

<a id="wsl-selection"></a>

## Кілька WSL-дистрибутивів

Виявлення перелічує WSL-дистрибутиви, ігнорує `docker-desktop` і перевіряє
`codex` через `sh -lc`. Якщо Codex є в кількох дистрибутивах і коректний вибір
ще не збережено, встановлення зупиняється замість довільного вибору.

Перед першим встановленням збережіть точну назву в Windows PowerShell:

```powershell
$settingsPath = 'HKCU:/Software/CodexTray'
New-Item -Path $settingsPath -Force | Out-Null
New-ItemProperty -Path $settingsPath -Name WslDistribution -Value 'Ubuntu' -PropertyType String -Force | Out-Null
```

Після цього повторіть setup. Замініть `Ubuntu` назвою з `wsl.exe --list --quiet`.
Щоб змінити наявну інтеграцію, спочатку виконайте `--shutdown` і `--uninstall`
старим EXE, поки в налаштуваннях записаний старий дистрибутив.
Лише потім змініть `WslDistribution`, встановіть знову, запустіть tray і підтвердьте Trust.
Не перезаписуйте вибір без очищення: це може залишити hooks у старому дистрибутиві.

<a id="checksums"></a>

## Перевірка SHA-256

У каталозі із завантаженими файлами виконайте Windows PowerShell:

```powershell
Get-Content ./SHA256SUMS.txt
Get-FileHash -Algorithm SHA256 -LiteralPath ./CodexTraySetup.exe
# Also verify this file if downloaded:
Get-FileHash -Algorithm SHA256 -LiteralPath ./CodexTray.exe
```

Порівняйте кожний повний 64-символьний хеш із рядком для точної назви файла в
`SHA256SUMS.txt`. Якщо є розбіжність, **не запускайте файл**:
завантажте відповідні файли одного релізу повторно й повідомте про повторні розбіжності.
Хеші виявляють пошкодження або змішані завантаження, але не підтверджують особу видавця
незалежно від джерела, якщо release-акаунт або manifest скомпрометовані.

<a id="unsigned-release"></a>

## Unsigned release / Unknown Publisher

v1.3.0 не має Authenticode-підпису. Windows може показати **Unknown Publisher**
або `Unknown publisher`, а SmartScreen — попередження про невідомий застосунок.
Правильний хеш не прибирає цих попереджень.

Завантажуйте лише зі сторінки релізів цього проєкту, перевіряйте файли й переглядайте код.
Не вимикайте antivirus, SmartScreen, організаційні політики або Codex hook trust.
Якщо ваша політика безпеки забороняє unsigned apps, не запускайте цей реліз:
зверніться до адміністратора, перегляньте/зберіть код там, де це дозволено,
або дочекайтеся підписаного релізу.

<a id="source-build"></a>

## Збірка з коду

Потрібні Windows x64, Git, .NET SDK **10.0.401** та Inno Setup **7.1.0**.
Windows PowerShell 5.1 підтримує bootstrap/release/repository-contract scripts;
для WSL IPC proof використовуйте PowerShell 7 (`pwsh`).
Inno Setup має окремі [ліцензійні умови](https://jrsoftware.org/isinfo.php);
MIT-ліцензія проєкту не ліцензує build tools.

Клонуйте репозиторій за URL із GitHub **Code**, відкрийте Windows PowerShell у його корені
та перегляньте скрипти перед виконанням:

```powershell
./scripts/bootstrap-build-tools.ps1
dotnet --version
dotnet restore ./CodexTray.sln --locked-mode
dotnet format ./CodexTray.sln --verify-no-changes --no-restore
dotnet build ./CodexTray.sln --configuration Release --no-restore
powershell.exe -NoProfile -File ./scripts/test-repository-contract.ps1
powershell.exe -NoProfile -File ./scripts/build-release.ps1 -AllowUnsigned
```

Bootstrap встановлює точні версії через Winget, якщо їх немає; потрібні мережа
та, можливо, підтвердження системного встановлення.
Запускайте скрипти лише там, де це дозволяє execution policy;
не послаблюйте глобальні налаштування безпеки заради запуску.

`global.json` закріплює вибір SDK, а NuGet lock files включені в Git.
`src/CodexTray/CodexTray.csproj` — єдине джерело версії продукту, яку release
script передає Inno. Unsigned switch — явне підтвердження, а не підписання.
Без нього unsigned output відхиляється.

Release script замінює лише локальні `artifacts/publish` та `dist`,
спершу штатно зупиняючи попередній запущений `dist/CodexTray.exe`.
Повні тести потребують Ubuntu/Codex-конфігурації, описаної нижче.
`-PureTestsOnly` явно виключає тести з реальними WSL та USB для hosted-збірок;
цей режим не замінює повні локальні тести й ручне приймання релізу.
Успішна збірка створює рівно `CodexTray.exe`, `CodexTraySetup.exe`
і `SHA256SUMS.txt` у `dist`; жоден із цих файлів не повинен бути в Git.
Закріплення tools/packages робить вхідні дані збірки повторюваними,
але не гарантує однакові байти installer на різних машинах.

<a id="tests"></a>

## Тести та ручне приймання

Виконуйте команди з кореня репозиторію після locked restore/build.

Автоматизовані тести без WSL integration і hardware:

```powershell
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'Category!=Integration&Category!=UsbHardware'
```

Повні тести включають реальну WSL-інтеграцію та потребують дистрибутива з назвою
`Ubuntu`, де Codex видимий через `sh -lc`.
USB hardware tests пропускаються без явного ввімкнення.

Тест реального тижневого ліміту також пропускається, якщо
`CODEXTRAY_TEST_WEEKLY_DISTRIBUTION` не задано. Для його свідомого ввімкнення
використовуйте окремі команди нижче.

```powershell
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~WslHookConfigStoreIntegrationTests'
```

Hook-store test використовує ізольований `/tmp/codex-tray-tests-<guid>`,
а не вашу справжню hook-конфігурацію. Він перевіряє atomic install/reinstall/uninstall,
збереження foreign handlers і backup, після чого видаляє свій test-directory.

Для реального WSL → Windows pipe proof спочатку перемкніть USB на Off і закрийте
наявний tray. Після release-збірки виконайте в PowerShell 7:

```powershell
pwsh -NoProfile -File ./scripts/test-wsl-ipc.ps1 -Distribution Ubuntu -Cycles 100
```

Скрипт запускає/зупиняє власний tray, перевіряє Busy/Ready IPC, перезапуск до Inactive,
offline hook fail-open і відсутність per-event runtime files.
Він не доводить роботу реальних Codex hooks.

Opt-in USB hardware tests записують кадри на фізичний екран.
Закрийте vendor apps, вимкніть tray USB output через Off і використовуйте свій COM-порт:

```powershell
$previousUsbPort = $env:CODEXTRAY_TEST_USB_PORT
$previousUsbOrientation = $env:CODEXTRAY_TEST_USB_ORIENTATION
$previousWeeklyDistribution = $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION
try {
    $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION = $null
    $env:CODEXTRAY_TEST_USB_PORT = 'COM3'
    $env:CODEXTRAY_TEST_USB_ORIENTATION = 'Portrait'
    dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'Category=UsbHardware'
}
finally {
    $env:CODEXTRAY_TEST_USB_PORT = $previousUsbPort
    $env:CODEXTRAY_TEST_USB_ORIENTATION = $previousUsbOrientation
    $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION = $previousWeeklyDistribution
}
```

Прев’ю зберігаються в `artifacts/usb-screen-previews`.
Serial-запис не доводить правильні пікселі: перегляньте фізичний екран самостійно,
після чого поверніть Automatic або потрібний порт.

### Тести тижневого ліміту

Автоматизована команда вище перевіряє парсинг ліміту, помилки й відновлення,
скасування, рендеринг та повні/часткові кадри на синтетичних даних, без запитів
до акаунта та записів на фізичний USB-екран. Синтетичні прев’ю зберігаються в
`tests/CodexTray.Tests/bin/Release/net10.0-windows/weekly-limit-previews`
як `Weekly-Portrait.png`, `Weekly-Landscape.png` і відповідні файли `Unavailable-`.

Наступний integration test виконує **автентифікований мережевий запит** через
`codex app-server` у WSL. Використовуйте дистрибутив, де вже виконано вхід у Codex
і команда видима через `sh -lc`; замініть `Ubuntu` на його справжню назву.
Індикатору не потрібні сертифікат, ключ, пароль або файл облікових даних.
Ця окрема команда не запускає tray, не записує на USB і не надсилає prompt.

```powershell
$previousWeeklyDistribution = $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION
try {
    $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION = 'Ubuntu'
    dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~WeeklyLimitIntegrationTests'
    if ($LASTEXITCODE -ne 0) { throw 'Weekly-limit account test failed.' }
}
finally {
    $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION = $previousWeeklyDistribution
}
```

Для успіху потрібен дійсний, непрострочений тижневий ліміт. Непідтримуваний
акаунт/API, відсутній ліміт або помилка входу завершують тест невдачею, хоча
застосунок правильно показує **—**. Тест зберігає `live-limit.json` (відсоток і час
скидання) та `Live-Portrait.png` у тому самому каталозі прев’ю. Це особисті дані
акаунта: залишайте їх локально й не додавайте до публічних звітів. Build/test output
ігнорується Git; Codex app-server усе одно може використовувати звичайну діагностику Codex.

Щоб показати реальний тижневий ліміт на підтримуваному Revision A USB-екрані,
закрийте vendor apps і спочатку виберіть **USB screen → Off** у tray.
Вкажіть справжній COM-порт, який вибирає Automatic detection; інші порти тест
відхиляє. Цей тест і запитує акаунт, і записує один кадр на фізичний екран:

```powershell
$previousWeeklyDistribution = $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION
$previousUsbPort = $env:CODEXTRAY_TEST_USB_PORT
$previousUsbOrientation = $env:CODEXTRAY_TEST_USB_ORIENTATION
try {
    $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION = 'Ubuntu'
    $env:CODEXTRAY_TEST_USB_PORT = 'COM3'
    $env:CODEXTRAY_TEST_USB_ORIENTATION = 'Portrait'
    dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~WeeklyLimitHardwareTests'
    if ($LASTEXITCODE -ne 0) { throw 'Weekly-limit USB test failed.' }
}
finally {
    $env:CODEXTRAY_TEST_WEEKLY_DISTRIBUTION = $previousWeeklyDistribution
    $env:CODEXTRAY_TEST_USB_PORT = $previousUsbPort
    $env:CODEXTRAY_TEST_USB_ORIENTATION = $previousUsbOrientation
}
```

Hardware test пропускається, якщо не задано обидві змінні — дистрибутив і порт.
Для горизонтального режиму задайте `Landscape`; перевернуті режими —
`ReversePortrait` і `ReverseLandscape`. Перевірте відсоток, текст і смугу на самому
екрані: успішний serial-запис не доводить правильні пікселі. Після тесту вручну
поверніть попереднє налаштування USB у tray; блоки `finally` відновлюють лише
змінні середовища. Обидва реальні тести не замінюють приймання lifecycle,
перепідключення та інсталятора.

Перед прийманням релізу вручну перевірте setup, hook Trust, старт сесії → Ready,
prompt → Busy, звичайне завершення → Ready з одним повідомленням, Ctrl+C → Ready,
фактичний кінець сесії → Inactive, Windows startup, reinstall, збереження hooks при uninstall,
USB orientation/reconnect та хеші завантажених файлів.

<a id="reinstall"></a>

## Перевстановлення або оновлення

Запустіть новий перевірений installer у те саме розташування.
Setup зупиняє попередній tray перед заміною.
Hook merge замінює лише handlers із `codex-tray-indicator-v1`, зберігає foreign hooks
і не накопичує дублікати власних handlers.
Перегляньте pending hooks через `/hooks`, виберіть Trust і відкрийте нову сесію.

Якщо WSL hook file некоректний або недоступний, setup повідомляє про помилку.
Не видаляйте весь файл для виправлення: збережіть свої hooks і усуньте повідомлену проблему.

<a id="uninstall"></a>

## Видалення

Для встановленої копії: Windows **Settings → Apps → Installed apps →
Codex Tray Indicator → Uninstall**.

Для portable-копії, поки її початковий шлях і WSL selection доступні:

```powershell
& $codexTray --shutdown
& $codexTray --uninstall
```

Використовуйте змінну шляху з розділу встановлення.
Видаляйте/переміщуйте portable EXE лише після успішного видалення інтеграції.
Uninstall видаляє власні handlers, startup і preferences поточного користувача.
Foreign hooks та початковий `~/.codex/hooks.json.codextray.bak`, якщо він створений,
залишаються. Вибраний WSL-дистрибутив має бути доступний для успішного очищення.
Недоступний дистрибутив або некоректна конфігурація призводять до повідомленої помилки,
а не надають дозвіл стерти чужі hooks.

<a id="troubleshooting"></a>

## Діагностика проблем

Перевірте транспорт у Windows PowerShell установленим EXE:

```powershell
$codexTray = "$env:LOCALAPPDATA/Programs/CodexTray/CodexTray.exe"
& $codexTray --query-state
& $codexTray --hook-test busy
& $codexTray --hook-test ready
```

Query друкує Inactive/Ready/Busy/Error.
Синтетичні команди потребують запущеного tray і перевіряють лише IPC:
не виконують Codex prompt і не створюють completion notifications.

| Симптом | Що перевірити |
|---|---|
| Немає іконки | Перевірте приховані tray icons, запустіть встановлений EXE; дозволена одна копія на Windows-користувача |
| Codex не знайдено | Виконайте WSL probe з підтримуваних конфігурацій; перевірте login-shell PATH і Windows interop |
| Помилка кількох дистрибутивів | Збережіть WslDistribution перед встановленням, як описано вище |
| Постійно Inactive/Ready | Перевірте запуск tray, вибраний дистрибутив, hook path і /hooks Trust; відкрийте нову Codex-сесію |
| Busy після закриття термінала | Дочекайтеся справжнього SessionEnd; стан не визначається за зникненням термінала/процесу |
| Error | Виконайте query/synthetic tests, виправте config/permissions; наступна коректна подія може скинути protocol error |
| Немає повідомлення | Перевірте Notifications, налаштування Windows і чи не залишилася інша Busy-сесія |
| USB недоступний/порожній | Перевірте Revision A, свій порт, orientation, закриття vendor app; виберіть Reconnect screen |
| SDK/compiler не знайдено | Запустіть bootstrap, перевірте точні версії й locked restore; не видаляйте lock files, щоб примусити збірку пройти |

Для звичайних багів використовуйте Issues цього репозиторію.
Додайте версії Windows/app/WSL/CLI, кроки відтворення та очищені від приватних даних diagnostics.
Не додавайте prompts, transcripts або secrets.
Дивіться [CONTRIBUTING.md](CONTRIBUTING.md) і [SECURITY.md](SECURITY.md).

<a id="privacy"></a>

## Архітектура та приватність

Hooks запускають той самий Windows EXE з `--hook`.
Вхід обмежений 65 536 байтами; лише lifecycle event name, session ID, turn ID,
SessionStart source і timestamp, створений застосунком, передаються в
`CodexTray.Status.v1` із `PipeOptions.CurrentUserOnly`.
Prompts, responses, transcript paths, робочі каталоги та model metadata відкидаються.

Hooks працюють fail-open: некоректний вхід, offline tray або IPC timeout повертають
exit code 0, щоб індикатор не блокував Codex.
Бюджет pipe connect — 250 мс, I/O обмежений.
Для читання тижневого ліміту Codex app-server у вибраному WSL-дистрибутиві запитує
ліміти акаунта в OpenAI через мережу з наявною автентифікацією цього Codex-акаунта.
Індикатор зберігає лише тижневий відсоток і час скидання в пам’яті, не читає й не
копіює credentials. Codex app-server може використовувати звичайну діагностику Codex.
Індикатор не пише per-event state files, logs або registry values.
.NET single-file runtime може розпакувати native libraries при запуску; це не status log.

Під час встановлення/налаштування зберігаються `~/.codex/hooks.json`,
одноразовий `hooks.json.codextray.bak`, якщо початковий файл існував,
preferences у `HKCU/Software/CodexTray` і startup entry поточного користувача.
USB discovery читає Windows device information, але не передає стан Codex через registry.
Pipe не захищає від шкідливого ПЗ, яке працює від імені того самого Windows-користувача.

<a id="security"></a>

## Безпека, внески та ліцензія

Про вразливості повідомляйте приватно за [SECURITY.md](SECURITY.md), не через публічні Issues.
Індикатор не є sandbox, механізмом авторизації Codex або сервісом code signing.
Переглядайте hooks перед Trust, зберігайте чужу конфігурацію
та ніколи не передавайте credentials або certificate material для diagnostics.

Внески регулюють [CONTRIBUTING.md](CONTRIBUTING.md) і
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Copyright © 2026 Vasyl Danyliuk.
Код надається за [MIT](LICENSE), без гарантій.
