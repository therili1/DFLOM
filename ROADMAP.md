# SkyLight Launcher — Roadmap кастомізації (технічна частина)

Джерело: Promt.txt (повний список системи кастомізації).
Тут — статус кожного пункту: що реально зроблено технічно (без дизайну/фінального вигляду),
що ще в роботі, і що свідомо відкладено з поясненням чому.

Легенда: ✅ зроблено · 🔶 частково / базова версія · ⬜ не почато

---

## ✅ Готово

### Theme Engine (ядро)
- `IThemeService`/`ThemeService` (singleton), 8 готових тем: Modern, Modern Glow, Fluent,
  Glass, Minimal, Dark, Light, Minecraft Theme.
- Кольори: Accent, Background, Glow, Hover, Border, Card, Text — застосовуються миттєво
  через `Application.Current.Resources`, без перезапуску.
- Персистентність у `theme.json` (`%AppData%/.lrs_launcher/`), завантаження до показу вікна
  (немає "спалаху" дефолтних кольорів при старті).
- `ThemeEditorPage` — галерея пресетів + всі кольори + Opacity/Blur/CornerRadius/Font + live-прев'ю.
- Файли: `Services/IThemeService.cs`, `Services/ThemeService.cs`, `Models/ThemeSettings.cs`,
  `ViewModels/ThemeEditorViewModel.cs`, `Views/ThemeEditorPage.xaml(.cs)`.

### Вікно (частково)
- Corner Radius — керується з Theme Engine (`ControlCornerRadius`/`OverlayCornerRadius`).
- Blur, Window Opacity — поля є в `ThemeSettings`, зберігаються, але поки не прив'язані
  до реального рендеру вікна (DWM backdrop/composition) — див. розділ "Не почато".

### Навігація
- `INavigationSettingsService`/`NavigationSettingsService` (singleton), `navigation.json`.
- Приховати/показати вкладку, порядок (вгору/вниз), Favorite Pages (⭐), позиція панелі.
- `MainWindow` будує `NavigationView.MenuItems` динамічно з сервісу замість хардкоду в XAML.
- Керування — новий блок у `SettingsView`.
- ⚠️ Чесне обмеження: `NavigationView` у WinUI3 нативно підтримує тільки Left/Top — Right і
  Bottom фолбечать на найближчий еквівалент (Left/Top відповідно), бо API іншого не дає.
- Community Hub: пункт меню, що відкриває Discord-лінк замість сторінки.
  **Постав свій реальний invite-лінк** у `MainWindow.xaml.cs` (зараз заглушка `discord.gg/`).
- Файли: `Services/INavigationSettingsService.cs`, `Services/NavigationSettingsService.cs`,
  `Models/NavigationItemSettings.cs`, `MainWindow.xaml(.cs)`, `Views/SettingsView.xaml(.cs)`.

### Анімації (базовий рушій)
- `IAnimationSettingsService`/`AnimationSettingsService` (singleton), `animations.json`.
- Enable/Disable, Speed (множник тривалості), Glow-рівень (Off/Low/Medium/High/Ultra) —
  впливає на прозорість `AppGlowBrush` у Theme Engine.
- Переходи сторінок (`ContentFrame.ContentTransitions`) реагують на Enable/Disable.
- ⚠️ Чесне обмеження: це НЕ повний контроль над Hover/Ripple/Popup/Snackbar/Tooltip/Loading/
  Grid-анімаціями окремо — це вшиті анімації стандартних WinUI3-контролів, і вимкнути кожну
  окремо без переписування control-темплейтів нереально швидко. `GetDuration()` — хелпер,
  яким мають користуватись майбутні КАСТОМНІ контролі (Grid-картки збірок, Dashboard-блоки),
  щоб поважати ці налаштування.
- Файли: `Services/IAnimationSettingsService.cs`, `Services/AnimationSettingsService.cs`.

### Багфікси (до старту кастомізації)
- Стан сторінок більше не скидається при перемиканні вкладок (ViewModels: Transient → Singleton).
- Кастомні інстанси тепер встановлюються/запускаються у власній ізольованій теці
  (`GameDirectory` більше не порожній), а не в спільній — і моди з діалогу профілю реально бачить гра.
- `ComboBoxItem IsSelected="True"` в `InstancesView`/`DownloadCenterPage` падав з
  `NullReferenceException` при старті сторінки — `SelectionChanged` стріляв ще під час
  `InitializeComponent()`, до присвоєння `ViewModel`. Фікс: `ViewModel` тепер присвоюється
  ДО `InitializeComponent()` в обох сторінках.
- Fabric/Quilt без явно вказаної версії лоадера отримували `LaunchVersionId` = голий номер
  лоадера (напр. `"0.19.3"`) замість повного id (`"fabric-loader-0.19.3-1.21.11"`) — гра
  намагалась запустити неіснуючу версію і мовчки нічого не робила. Нормалізовано в `MinecraftService`.
- Кнопка "▶ Грати" на картці Grid View (`InstancesViewModel.LaunchInstanceAsync`) викликала
  ТІЛЬКИ запуск, без встановлення — на відміну від Home, де install завжди йшов першим.
- **Встановлення модпаку через Marketplace (`ModpackInstaller`) качало лише mods/config/
  overrides, але ніколи не встановлювало саму гру** (versions/libraries/assets були відсутні
  в теці інстансу) — `MarketplaceViewModel` тепер викликає `MinecraftService.InstallInstanceAsync`
  одразу після `InstallModpackAsync`.
- Додано логування (`ILogService`) у сам процес запуску гри (`BuildProcessAsync`, старт,
  exit-код, stderr клієнта) — раніше йшло тільки в `Debug.WriteLine`, тобто в нікуди для
  зібраного релізного застосунку.

---

### Сторінка "Збірки" (Grid View)
- `InstancesView` — GridView з картками (іконка/CustomIcon або авто-емодзі за лоадером, назва,
  версія, loader, к-сть модів/світів рахується з диска, дата останнього запуску, кнопка "Грати",
  меню дій) + перемикач Grid⇄List, розмір карток (S/M/L), пошук, сортування.
- Іконка/аватарка профілю тепер реально редагується (див. "Аватарки" нижче) — цей рядок
  раніше стверджував, що вибір готового файлу вже працює, хоча насправді ніде в коді
  не було жодного місця, яке б записувало `CustomIcon` — його тільки читали й показували.
- Файли: `Views/InstancesView.xaml(.cs)`, `ViewModels/InstancesViewModel.cs` (SearchText/SortMode/
  IsGridView/CardSize/FilteredInstances), нові конвертери в `Converters/ValueConverters.cs`.

### Аватарки профілю (drag&drop + crop)
- ⚠️ Виправлення попереднього невірного статусу: раніше в роадмапі значилось "підтримується
  вибір готового файлу" — при перевірці коду виявилось, що такого пікера не було **взагалі
  ніде**: `CustomIcon` лише читався й показувався в `InstancesView`, але жодна кнопка чи
  файл його не записували.
- Нова вкладка "Іконка" в `InstanceSettingsDialog`: drag&drop-зона (`AllowDrop`) + кнопка
  "Обрати файл", обидва шляхи ведуть у спільний пайплайн `CropAndApplyIconAsync`.
- Кроп: новий `Views/IconCropDialog` — квадратна рамка виділення поверх зображення,
  перетягується (move) і розтягується за кутик (resize), з клемпом у межі картинки.
  Координати рамки мапляться назад у піксельні координати оригінального файлу з
  урахуванням `Stretch=Uniform`-масштабування і letterbox-зсуву.
- Обрізка виконується через вбудований `Windows.Graphics.Imaging`
  (`BitmapDecoder`/`SoftwareBitmap`/`BitmapEncoder`) — **без нових NuGet-пакетів**.
  Результат зберігається як PNG у `%AppData%/.lrs_launcher/icons/{instanceId}.png`.
- "Скинути до емодзі за замовчуванням" видаляє кастомний файл і повертає авто-іконку
  за лоадером (`GetLoaderIcon`).
- Файли: `Views/IconCropDialog.xaml(.cs)` (новий), `Views/InstanceSettingsDialog.xaml(.cs)`.

### Download Center
- `IDownloadManager`/`DownloadManager` (вже існував частково, доробив): по-задачні Pause/Resume/
  Retry/Cancel (раніше були лише глобальні), докачування з місця зупинки (HTTP Range), Open Folder.
- `DownloadCenterPage` — окрема вкладка, картки задач з прогрес-баром/%/ETA/швидкістю/статусом,
  пошук за назвою, сортування (нові/назва/статус). Черга одночасно є історією — завершені/
  скасовані/помилкові задачі лишаються в списку, а не зникають.
- Категорії: Minecraft/Java/Mod/Modpack/World/Datapack/ResourcePack/Shader/Loader (enum `DownloadCategory`).
- ⚠️ Чесне обмеження: сюди йдуть завантаження модів/датапаків (вже було підключено з Marketplace).
  Завантаження самого **Minecraft/Java/бібліотек** відбувається всередині CmlLib (сторонньої
  бібліотеки launcher-ядра) і має власний внутрішній прогрес, який поки НЕ проведений через цей
  `DownloadManager` — тобто вони не з'являться рядком у Download Center, хоча категорії для них
  вже заведені. Звести CmlLib-прогрес в один список з рештою - наступний крок, якщо знадобиться.
- Файли: `Services/IDownloadManager.cs`, `Services/DownloadManager.cs`,
  `ViewModels/DownloadCenterViewModel.cs`, `Views/DownloadCenterPage.xaml(.cs)`.

### Шрифти (базовий вибір)
- `ThemeEditorViewModel.AvailableFonts` — Segoe UI / Segoe UI Variable / Inter / Roboto /
  JetBrains Mono / Minecraft Seven / Custom, `ComboBox` замість вільного текстового поля.
- Опція "Custom" відкриває текстове поле для будь-якого шрифту, встановленого в системі.
- ⚠️ Чесне обмеження: у застосунок НЕ вшиті файли шрифтів Inter/Roboto/JetBrains
  Mono/Minecraft Seven — жодного `.ttf`/`.otf` у репозиторії немає. Якщо цих шрифтів
  немає в самій Windows, вибір мовчки відкотиться на дефолтний (WinUI-поведінка,
  не помилка коду). Гарантовано працює лише Segoe UI/Segoe UI Variable (вшиті в Windows).
  Це стосується і `"Minecraft Theme"`-пресету — він і раніше задавав `FontFamily =
  "Minecraft Seven"`, просто без попередження, що шрифт треба ставити окремо.
  Щоб зняти обмеження повністю - потрібно докласти реальні файли шрифтів у
  `Assets/Fonts/` і підключити через `ms-appx:///Assets/Fonts/Inter.ttf#Inter`.
- Файли: `ViewModels/ThemeEditorViewModel.cs` (AvailableFonts/IsCustomFont/SelectedFontOption),
  `Views/ThemeEditorPage.xaml`.

## ⬜ В роботі / далі за планом

1. **Фон** — статика → slideshow → animated gradient → відео (mp4/webm), з Brightness/Blur/
   Opacity/Playback Speed/Loop.
2. **Шрифти — вшиті файли** — реальні `.ttf`/`.otf` для Inter/Roboto/JetBrains Mono/
   Minecraft Seven, щоб вони гарантовано працювали без системної інсталяції (сам вибір
   зі списку вже готовий, див. "Готово" вище).
3. **Звуки** — Hover/Click/Download Complete/Notifications.
4. **Візуальні ефекти** — Snow/Rain/Fireflies/Glow Dust/Particles/Dynamic Background.
5. **Dashboard** — перетягування/приховування/зміна порядку блоків головної сторінки.
6. **Theme Packs через JSON/CSS-подібні файли** — потребує окремо продуманого формату
   (які саме елементи можна перекрити) і валідації, щоб зловмисний пак не зламав UI.
7. **Plugin API** — легкий, реально робочий extensibility-хук (реєстрація сторінок/кнопок/
   меню/віджетів через C#-інтерфейс), **без** повноцінної sandboxed-системи для сторонніх
   плагінів — це окрема серйозна робота з безпеки, її чесно не можна "просто дописати".

---

## Нотатки для наступної сесії/іншої моделі
- Усі нові сервіси реєструються в `App.xaml.cs` (`ConfigureServices`) і мають бути завантажені
  (`LoadAsync()`) у `OnLaunched` ДО створення `MainWindow`, інакше буде "спалах" дефолтних значень.
- Персистентність усього — прості JSON-файли в `%AppData%/.lrs_launcher/` (той самий підхід,
  що й у `InstanceStore`), не Registry і не файли настройок WinUI.
- ViewModels сторінок — `Singleton` у DI (навмисно, щоб стан не скидався при навігації
  через `ContentFrame.Navigate`), тримай це в голові додаючи нові сторінки.
