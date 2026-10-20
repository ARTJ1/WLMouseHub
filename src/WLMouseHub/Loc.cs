using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WLMouseHub;

/// <summary>UI strings. XAML uses {DynamicResource S.Key}; code uses Loc.T("Key").</summary>
public static class Loc
{
    public static readonly (string Code, string Label)[] Languages = { ("en", "EN"), ("ru", "RU"), ("zh", "中文") };

    public static string Lang { get; private set; } = "en";
    public static event Action? Changed;

    private static ResourceDictionary? _current;

    public static string T(string key) =>
        Strings.TryGetValue(key, out var v) ? v[Index(Lang)] : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    public static string Label => Languages.First(l => l.Code == Lang).Label;

    public static void Apply(string lang)
    {
        if (Languages.All(l => l.Code != lang)) lang = "en";
        Lang = lang;
        var dict = new ResourceDictionary();
        foreach (var (key, values) in Strings) dict["S." + key] = values[Index(lang)];
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Changed?.Invoke();
    }

    private static int Index(string lang) => lang switch { "ru" => 1, "zh" => 2, _ => 0 };

    private static readonly Dictionary<string, string[]> Strings = new()
    {
        // header
        ["Main"] = new[] { "Main", "Главная", "主页" },
        ["Settings"] = new[] { "Setting", "Настройки", "设置" },
        ["Firmware"] = new[] { "Firmware", "Прошивка", "固件" },
        ["UserGuide"] = new[] { "User Guide", "Руководство", "使用指南" },
        ["Theme"] = new[] { "Light / dark theme", "Светлая / тёмная тема", "浅色 / 深色主题" },
        ["Language"] = new[] { "Language", "Язык", "语言" },
        ["Minimize"] = new[] { "Minimize", "Свернуть", "最小化" },
        ["Close"] = new[] { "Close", "Закрыть", "关闭" },

        // home
        ["NoDevice"] = new[] { "Connect your WLMOUSE with the cable or its receiver", "Подключите мышь WLMOUSE кабелем или через ресивер", "请使用数据线或接收器连接 WLMOUSE 鼠标" },
        ["Supported"] = new[] { "Supported product models", "Поддерживаемые модели", "支持的产品型号" },
        ["Searching"] = new[] { "Looking for a mouse…", "Поиск мыши…", "正在搜索鼠标…" },
        ["OpenSettings"] = new[] { "Settings", "Настройки", "设置" },
        ["Dongle"] = new[] { "Dongle", "Ресивер", "接收器" },
        ["Mouse"] = new[] { "Mouse", "Мышь", "鼠标" },
        ["Wired"] = new[] { "Wired", "Кабель", "有线" },
        ["Wireless"] = new[] { "Wireless", "Беспроводная", "无线" },
        ["StartWithWindows"] = new[] { "Start with Windows", "Запускать вместе с Windows", "开机启动" },
        ["KeepInTray"] = new[] { "Keep running in the tray when closed", "При закрытии оставаться в трее", "关闭时最小化到托盘" },
        ["AppOptions"] = new[] { "App", "Приложение", "应用" },

        // settings
        ["ResetProfile"] = new[] { "Reset Profile", "Сбросить профиль", "重置配置" },
        ["FactoryReset"] = new[] { "Factory Reset", "Полный сброс", "恢复出厂" },
        ["Profile"] = new[] { "Profile{0}", "Профиль {0}", "配置{0}" },
        ["Performance"] = new[] { "Performance", "Сенсор", "性能" },
        ["DPI"] = new[] { "DPI", "DPI", "DPI" },
        ["Macro"] = new[] { "Macro", "Макросы", "宏" },
        ["Lighting"] = new[] { "Wheel Lighting", "Подсветка", "灯光" },
        ["PollingRate"] = new[] { "Polling Rate", "Частота опроса", "回报率" },
        ["LOD"] = new[] { "LOD", "Высота отрыва", "静默高度" },
        ["Debounce"] = new[] { "Debounce Time", "Антидребезг", "去抖时间" },
        ["AngleTune"] = new[] { "Angle Tune", "Поворот сенсора", "角度调整" },
        ["SleepTime"] = new[] { "Sleep Time", "Время до сна", "休眠时间" },
        ["HighSpeed"] = new[] { "High-Speed Mode", "Скоростной режим", "高速模式" },
        ["Turbo"] = new[] { "Turbo Mode", "Турбо-режим", "Turbo 模式" },
        ["MotionSync"] = new[] { "Motion Sync", "Motion Sync", "Motion Sync" },
        ["ButtonCombo"] = new[] { "Button Combination", "Комбинации кнопок", "组合键" },
        ["Ripple"] = new[] { "Ripple Control", "Ripple Control", "波纹控制" },
        ["AngleSnap"] = new[] { "Angle Snap", "Выпрямление линий", "直线修正" },
        ["DpiIndicator"] = new[] { "DPI Indicator", "Индикатор DPI", "DPI 指示灯" },
        ["EnableXY"] = new[] { "Enable-XY", "Раздельно X/Y", "XY 分离" },
        ["SelectColor"] = new[] { "Select Color", "Цвет ступени", "选择颜色" },
        ["DpiStage"] = new[] { "DPI Stage", "Ступеней DPI", "DPI 档位" },
        ["Record"] = new[] { "Record", "Запись", "录制" },
        ["StopRecord"] = new[] { "Stop", "Стоп", "停止" },
        ["Insert"] = new[] { "Insert", "Вставить", "插入" },
        ["SaveMacro"] = new[] { "Save Macro", "Сохранить", "保存宏" },
        ["DeleteMacro"] = new[] { "Delete Macro", "Удалить", "删除宏" },
        ["RecordDelaySettings"] = new[] { "RECORD DELAY SETTINGS", "ЗАДЕРЖКИ ПРИ ЗАПИСИ", "录制延时设置" },
        ["KeyDelay"] = new[] { "Key Delay(ms)", "Задержка (мс)", "按键延时(ms)" },
        ["RecordDelay"] = new[] { "Record Delay", "Как записано", "录制延时" },
        ["DefaultDelay"] = new[] { "Default Delay", "Фиксированная", "默认延时" },
        ["NoDelay"] = new[] { "No Delay", "Без задержек", "无延时" },
        ["MacroN"] = new[] { "Macro{0}", "Макрос {0}", "宏{0}" },
        ["MacroHint"] = new[] { "Press Record and type, or use Insert.", "Нажмите «Запись» и печатайте или используйте «Вставить».", "点击录制后按键，或使用插入。" },
        ["Delay"] = new[] { "Delay", "Задержка", "延时" },
        ["MouseDown"] = new[] { "Press", "Нажать", "按下" },
        ["MouseUp"] = new[] { "Release", "Отпустить", "松开" },
        ["Click"] = new[] { "Click", "Клик", "单击" },
        ["WheelUp"] = new[] { "Wheel up", "Колесо вверх", "滚轮上" },
        ["WheelDown"] = new[] { "Wheel down", "Колесо вниз", "滚轮下" },
        ["MoveUp"] = new[] { "Move up", "Выше", "上移" },
        ["MoveDown"] = new[] { "Move down", "Ниже", "下移" },
        ["Remove"] = new[] { "Remove", "Удалить шаг", "删除" },
        ["LightingEffect"] = new[] { "Lighting Effect", "Эффект подсветки", "灯光效果" },
        ["Effect"] = new[] { "Effect", "Эффект", "效果" },
        ["Bright"] = new[] { "Bright", "Яркость", "亮度" },
        ["Speed"] = new[] { "Speed", "Скорость", "速度" },
        ["Color"] = new[] { "Color", "Цвет", "颜色" },
        ["Recent"] = new[] { "Recent", "Недавние", "最近" },
        ["Disable"] = new[] { "Disable", "Выключено", "关闭" },
        ["Static"] = new[] { "Static", "Статичный", "常亮" },
        ["Breathing"] = new[] { "Breathing", "Дыхание", "呼吸" },
        ["Spectrum"] = new[] { "Spectrum", "Спектр", "光谱" },
        ["Wave"] = new[] { "Wave", "Волна", "波浪" },
        ["NoLighting"] = new[] { "This connection does not support lighting.", "Подсветка недоступна при этом подключении.", "当前连接不支持灯光。" },

        // buttons
        ["B1"] = new[] { "1-Left Click", "1 — Левая", "1-左键" },
        ["B2"] = new[] { "2-Wheel Click", "2 — Колесо", "2-中键" },
        ["B3"] = new[] { "3-Right Click", "3 — Правая", "3-右键" },
        ["B4"] = new[] { "4-Forward", "4 — Вперёд", "4-前进" },
        ["B5"] = new[] { "5-Backward", "5 — Назад", "5-后退" },
        ["LeftClick"] = new[] { "Left Click", "Левый клик", "左键" },
        ["RightClick"] = new[] { "Right Click", "Правый клик", "右键" },
        ["WheelClick"] = new[] { "Wheel Click", "Клик колесом", "中键" },
        ["Forward"] = new[] { "Forward", "Вперёд", "前进" },
        ["Backward"] = new[] { "Backward", "Назад", "后退" },
        ["ScrollUp"] = new[] { "Scroll Up", "Прокрутка вверх", "向上滚动" },
        ["ScrollDown"] = new[] { "Scroll Down", "Прокрутка вниз", "向下滚动" },
        ["DoubleClick"] = new[] { "Left Double Click", "Двойной клик", "左键双击" },
        ["DpiCycle"] = new[] { "DPI Loop", "DPI по кругу", "DPI 循环" },
        ["DpiUp"] = new[] { "DPI +", "DPI +", "DPI +" },
        ["DpiDown"] = new[] { "DPI -", "DPI -", "DPI -" },
        ["DpiLock"] = new[] { "DPI Lock", "Фикс. DPI", "DPI 锁定" },
        ["ProfileCycle"] = new[] { "Profile Loop", "Профиль по кругу", "配置循环" },
        ["PollingCycle"] = new[] { "Polling Rate Loop", "Частота по кругу", "回报率循环" },
        ["LodCycle"] = new[] { "LOD Loop", "LOD по кругу", "静默高度循环" },
        ["PlayPause"] = new[] { "Play / Pause", "Пауза / воспр.", "播放/暂停" },
        ["NextTrack"] = new[] { "Next Track", "Следующий трек", "下一曲" },
        ["PrevTrack"] = new[] { "Previous Track", "Предыдущий трек", "上一曲" },
        ["Stop"] = new[] { "Stop", "Стоп", "停止" },
        ["VolUp"] = new[] { "Volume +", "Громкость +", "音量 +" },
        ["VolDown"] = new[] { "Volume -", "Громкость -", "音量 -" },
        ["Mute"] = new[] { "Mute", "Без звука", "静音" },
        ["MediaPlayer"] = new[] { "Media Player", "Медиаплеер", "媒体播放器" },
        ["Calculator"] = new[] { "Calculator", "Калькулятор", "计算器" },
        ["MyComputer"] = new[] { "My Computer", "Этот компьютер", "我的电脑" },
        ["Browser"] = new[] { "Browser", "Браузер", "浏览器" },
        ["Email"] = new[] { "Email", "Почта", "邮件" },
        ["WebHome"] = new[] { "Browser Home", "Домашняя страница", "浏览器主页" },
        ["WebRefresh"] = new[] { "Browser Refresh", "Обновить страницу", "刷新" },
        ["Disabled"] = new[] { "Disabled", "Отключено", "禁用" },
        ["KeyCombo"] = new[] { "Keyboard…", "Клавиатура…", "键盘…" },
        ["MacroAssign"] = new[] { "Macro…", "Макрос…", "宏…" },
        ["DpiLockAssign"] = new[] { "DPI Lock…", "Фикс. DPI…", "DPI 锁定…" },
        ["GroupMouse"] = new[] { "Mouse", "Мышь", "鼠标" },
        ["GroupMedia"] = new[] { "Multimedia", "Мультимедиа", "多媒体" },
        ["GroupDpi"] = new[] { "DPI / Profile", "DPI / профиль", "DPI / 配置" },
        ["GroupSystem"] = new[] { "System", "Система", "系统" },
        ["RestoreButtons"] = new[] { "Restore defaults", "Сбросить кнопки", "恢复默认" },

        // tips: the hub's own texts (EN / ZH verbatim from WL MOUSE HUB, RU translated)
        ["Tip.Polling"] = new[] {
            "Mouse data update Polling Rate Hz\n\nHold Backward + Wheel + R for 3s.\nStages: 125 / 250 / 500 / 1000 / 2000 / 4000 / 8000 Hz (Default: 1000 Hz)",
            "Частота обновления данных мыши, Гц\n\nУдерживайте Назад + Колесо + Правую 3 с.\nСтупени: 125 / 250 / 500 / 1000 / 2000 / 4000 / 8000 Гц (по умолчанию 1000 Гц)",
            "鼠标数据更新频率 Hz\n\n回报率: 后侧键、滚轮、右键同时长按3S (切换成功后，会有灯光提示)\n125、250、500、1000、2000、4000、8000 七档位切换,默认1000" },
        ["Tip.LOD"] = new[] {
            "The height at which the pointer cursor completely becomes unresponsive after the mouse is lifted away from the tracking surface\n\nHold Forward + L + Wheel for 3s. Stages: 0.7 mm / 1.0 mm / 2.0 mm",
            "Высота, на которой курсор полностью перестаёт реагировать, когда мышь поднята над поверхностью (коврик, стол)\n\nУдерживайте Вперёд + Левую + Колесо 3 с. Ступени: 0,7 / 1,0 / 2,0 мм",
            "鼠标从追踪表面 (鼠标垫、桌面等)抬离后，指针光标完全失去响应的高度\n\nLOD高度：前侧键、左键、滚轮同时长按3S（切换成功后，会有灯光提示）" },
        ["Tip.Debounce"] = new[] {
            "Increasing button debounce time appropriately can decrease the chance of double clicks",
            "Небольшое увеличение времени антидребезга снижает вероятность ложных двойных кликов",
            "适当增加按键消抖时间可提高按键稳定性" },
        ["Tip.AngleTune"] = new[] {
            "Get precise horizontal movement with any kind of mouse-grip style",
            "Точное горизонтальное движение при любом хвате мыши",
            "根据抓握方式调整鼠标传感器角度,即使在鼠标倾斜时也能实现精确的水平移动。" },
        ["Tip.Sleep"] = new[] {
            "The mouse enters sleep mode after being idle for a certain period",
            "Мышь переходит в режим сна после заданного времени бездействия",
            "鼠标静置该时间后，进入休眠状态" },
        ["Tip.HighSpeed"] = new[] {
            "Improves responsiveness by lowering input lag, speeding up sensor processing, and making polling more consistent. Slightly reduces battery life",
            "Улучшает отклик: снижает задержку ввода, ускоряет обработку сенсора и делает опрос стабильнее. Немного сокращает время работы от батареи",
            "高速模式有利于提高性能，但会增加功耗，可以和高回报率同时开启" },
        ["Tip.Turbo"] = new[] {
            "Sets a fixed 20K FPS, allowing the sensor to read the surface with extreme precision and minimal latency. Requires High-Speed Mode to be enabled",
            "Фиксирует 20K FPS: сенсор считывает поверхность с предельной точностью и минимальной задержкой. Требует включённого скоростного режима",
            "20K扫描率，极致性能，但会增加功耗，可以和高回报率同时开启,此模式需先开启高速模式" },
        ["Tip.MotionSync"] = new[] {
            "Make the pointer and mouse movement more synchronized and the trajectory more accurate",
            "Делает движение курсора более синхронным с мышью, а траекторию — точнее",
            "使指针和鼠标移动更加同步，轨迹更准确" },
        ["Tip.Combo"] = new[] {
            "Modify mouse settings by simultaneously clicking specific mouse buttons. After turning off, the pairing and 1K/8K receiver switching functions of key combinations cannot be used",
            "Изменение настроек одновременным нажатием определённых кнопок мыши. Если выключить, сопряжение и переключение ресивера 1K/8K сочетаниями кнопок будут недоступны",
            "通过同时按压鼠标组合按键修改鼠标设置，关闭后，无法使用组合键的配对和1K/8K接收器切换功能" },
        ["Tip.Ripple"] = new[] {
            "Minimizes jitter and shake during rapid, high-DPI mouse movements",
            "Уменьшает дрожание при быстрых движениях на высоком DPI",
            "消除鼠标高速移动时指针的波纹抖动" },
        ["Tip.AngleSnap"] = new[] {
            "Straightens vertical and horizontal mouse movements to reduce minor user errors",
            "Выпрямляет вертикальные и горизонтальные движения, сглаживая мелкие неточности руки",
            "修正鼠标移动时指针的偏移抖动" },
        ["Tip.SelectColor"] = new[] {
            "Lighting color when pressing lock combination or adjusting DPI via the driver",
            "Цвет подсветки при нажатии сочетания кнопок или смене DPI из программы",
            "组合按键或者驱动调节DPI时的灯光提示颜色" },
        ["Tip.DpiStage"] = new[] {
            "Hold L + Wheel + R for 3s.\nStages: 400 / 800 / 1600 / 3200 / 6400 / 30000 (Default: 1600)",
            "Удерживайте Левую + Колесо + Правую 3 с.\nСтупени: 400 / 800 / 1600 / 3200 / 6400 / 30000 (по умолчанию 1600)",
            "DPI：鼠标左键、中键、右键长按3S（切换成功后，会有灯光提示。）\n400、800、1600、3200、6400、30000 6级调节，默认1600，驱动可调级数" },
        ["Tip.Profile"] = new[] {
            "Personalized custom mouse parameter configuration scheme",
            "Собственный набор параметров мыши",
            "个性化定义鼠标参数配置方案" },
        ["Tip.Model"] = new[] { "Battery, firmware and connection of the connected mouse.", "Батарея, прошивка и подключение мыши.", "已连接鼠标的电量、固件和连接方式。" },
        ["SleepOff1"] = new[] { "Disabling Sleep Mode May Reduce Battery Life", "Отключение сна может сократить время работы от батареи", "禁用睡眠模式可能会缩短电池续航时间" },
        ["SleepOff2"] = new[] {
            "Sleep Mode lowers power usage when the mouse is idle. If disabled, the mouse will stay awake and consume more battery.",
            "Режим сна снижает энергопотребление, пока мышь не используется. Если его отключить, мышь не будет засыпать и будет тратить больше заряда.",
            "睡眠模式可在鼠标闲置时降低功耗。如果禁用，鼠标将保持唤醒状态，从而消耗更多电量。" },
        ["SleepOff3"] = new[] { "We recommend keeping it enabled.", "Рекомендуем оставить его включённым.", "我们建议保持该功能启用。" },
        ["WakeUp"] = new[] { "Please wake up mouse or switch to 2.4G on the bottom", "Разбудите мышь движением или переключите её снизу в режим 2.4G", "请拨动底部开关到2.4G或移动唤醒鼠标" },
        ["NoteTips"] = new[] { "Please do not remove the device during the process", "Не отключайте устройство во время процесса", "更新过程中，请不要拔掉设备" },
        ["MouseColor"] = new[] { "Mouse colour", "Цвет мыши", "鼠标颜色" },

        // receiver screen (HUAN)
        ["DongleScreen"] = new[] { "Dongle Screen", "Экран ресивера", "接收器屏幕" },
        ["ScreenLibrary"] = new[] { "WLmouse gif", "Галерея WLmouse", "WLmouse 动图" },
        ["Customize"] = new[] { "Customize", "Свои картинки", "自定义" },
        ["ScreenMain"] = new[] { "Main", "Главная", "主图" },
        ["ScreenRandom"] = new[] { "Random", "Случайные", "随机" },
        ["ScreenStatus"] = new[] { "STATUS", "СТАТУС", "参数显示" },
        ["ScreenEffect"] = new[] { "SCREEN EFFECT", "ЭФФЕКТ ЭКРАНА", "屏幕特效" },
        ["ScreenVoice"] = new[] { "VOICE", "ГОЛОС", "语音" },
        ["Delete"] = new[] { "Delete", "Удалить", "删除" },
        ["Tip.ScreenLibrary"] = new[] { "Click an animation, then click the Main circle or a Random circle (or drag it there).", "Нажмите на анимацию, затем на круг «Главная» или «Случайные» (или перетащите её).", "点击动图后再点击主图或随机位置（也可直接拖动）。" },
        ["Tip.Customize"] = new[] { "Add your own picture (PNG/JPG, cropped to 360×360) or a 224×224 GIF.", "Добавьте свою картинку (PNG/JPG, обрежется до 360×360) или GIF 224×224.", "添加自定义图片（PNG/JPG，裁剪为360×360）或224×224 GIF。" },
        ["Tip.ScreenMain"] = new[] { "Screen saver emoji. Only one is allowed. Drag an image here to replace it and click Update to apply.", "Заставка экрана, только одна. Перетащите сюда картинку и нажмите «Обновить».", "屏保表情，仅限一个。拖入图片替换后点击更新生效。" },
        ["Tip.ScreenRandom"] = new[] { "Random emojis. Drag images here; right-click one to remove it; click Update to apply.", "Случайные картинки. Перетащите их сюда; правый клик — убрать; затем «Обновить».", "随机表情。拖入图片，右键删除，点击更新生效。" },
        ["ScreenReading"] = new[] { "Reading the screen pictures…", "Чтение картинок экрана…", "正在读取屏幕图片…" },
        ["ScreenWriting"] = new[] { "Writing to the screen… {0}%", "Запись на экран… {0}%", "正在写入屏幕… {0}%" },
        ["ScreenNeedMain"] = new[] { "Please select one main gif.", "Выберите картинку для «Главная».", "请选择一个主图。" },
        ["ScreenTooBig"] = new[] { "The pictures are larger than 650 KB together.", "Картинки вместе больше 650 КБ.", "图片总大小超过650KB。" },
        ["ScreenBadGif"] = new[] { "A GIF must be 224×224 pixels.", "GIF должен быть 224×224 пикселя.", "GIF 尺寸需为 224×224。" },
        ["ScreenDeleteConfirm"] = new[] { "Delete all main and random emojis of the device?", "Удалить с ресивера главную и все случайные картинки?", "删除设备的所有主图和随机表情？" },
        ["ScreenUsed"] = new[] { "{0} / 650 KB", "{0} / 650 КБ", "{0} / 650 KB" },

        // firmware
        ["FwTitle"] = new[] { "{0} Dongle & Mouse Firmware Update", "Обновление прошивки {0}: ресивер и мышь", "{0} 接收器与鼠标固件升级" },
        ["FwStep1"] = new[] { "1. Connect the dongle (or the mouse) with a Type-C cable.", "1. Подключите ресивер (или мышь) кабелем Type-C.", "1. 使用 Type-C 数据线连接接收器（或鼠标）。" },
        ["FwStep2"] = new[] { "2. Click Update next to it. Do not unplug anything until it finishes.", "2. Нажмите «Обновить» рядом с ним. Ничего не отключайте до завершения.", "2. 点击旁边的升级按钮，完成前请勿拔出设备。" },
        ["FwStep3"] = new[] { "3. Then connect the other device the same way and update it.", "3. Затем так же подключите второе устройство и обновите его.", "3. 然后以同样方式连接另一设备并升级。" },
        ["FwStep4"] = new[] { "4. Updating the mouse resets its settings to defaults.", "4. Обновление мыши сбрасывает её настройки.", "4. 升级鼠标会将设置恢复为默认。" },
        ["Receiver8K"] = new[] { "Receiver 8K", "Ресивер 8K", "8K 接收器" },
        ["NewFw"] = new[] { "New Firmware Version: {0}", "Новая версия: {0}", "最新固件版本：{0}" },
        ["CurrentFw"] = new[] { "Installed: {0}", "Установлена: {0}", "当前版本：{0}" },
        ["Update"] = new[] { "Update", "Обновить", "升级" },
        ["Reinstall"] = new[] { "Reinstall", "Переустановить", "重新安装" },
        ["Latest"] = new[] { "Firmware Version is latest", "Установлена последняя версия", "已是最新版本" },
        ["NotConnected"] = new[] { "Not connected", "Не подключено", "未连接" },
        ["CableRequired"] = new[] { "Connect the mouse with the cable to update it.", "Для обновления подключите мышь кабелем.", "请用数据线连接鼠标后升级。" },
        ["FwUnsupported"] = new[] { "Use the official WL MOUSE HUB for this device.", "Для этого устройства используйте официальный WL MOUSE HUB.", "此设备请使用官方 WL MOUSE HUB。" },
        ["FwConfirm"] = new[] { "Update {0} firmware to {1}?\n\nKeep the device plugged in and do not close the app until it finishes.", "Обновить прошивку {0} до {1}?\n\nНе отключайте устройство и не закрывайте приложение до завершения.", "将{0}固件升级到 {1}？\n\n完成前请勿拔出设备或关闭程序。" },
        ["FwBusy"] = new[] { "Updating… do not unplug.", "Идёт обновление… не отключайте.", "正在升级…请勿拔出。" },
        ["ReleaseNotes"] = new[] { "Release notes", "Что нового", "更新说明" },

        // messages
        ["Connected"] = new[] { "Connected to {0}.", "Подключено: {0}.", "已连接 {0}。" },
        ["Disconnected"] = new[] { "Mouse disconnected.", "Мышь отключена.", "鼠标已断开。" },
        ["Reading"] = new[] { "Reading settings…", "Чтение настроек…", "正在读取设置…" },
        ["Saved"] = new[] { "Saved.", "Сохранено.", "已保存。" },
        ["AtLeastOneLeft"] = new[] { "At least one button must stay a left click.", "Хотя бы одна кнопка должна остаться левым кликом.", "至少保留一个左键。" },
        ["ConfirmResetProfile"] = new[] { "After reset, you need to reconfigure settings", "После сброса настройки профиля нужно будет задать заново", "重置配置后需要重新设置参数" },
        ["ConfirmFactory"] = new[] { "Factory reset will erase all data. Proceed carefully!", "Сброс к заводским настройкам сотрёт все данные. Будьте осторожны!", "恢复出厂设置会清除所有数据，请谨慎操作!" },
        ["ConfirmDeleteMacro"] = new[] { "Delete {0} from the mouse? Buttons that use it go back to their defaults.", "Удалить {0} из мыши? Кнопки с этим макросом вернутся к умолчаниям.", "从鼠标删除{0}？使用它的按键将恢复默认。" },
        ["MacroEmpty"] = new[] { "The macro is empty.", "Макрос пуст.", "宏为空。" },
        ["Rename"] = new[] { "Rename", "Переименовать", "重命名" },
        ["NewName"] = new[] { "New name:", "Новое имя:", "新名称：" },
        ["OK"] = new[] { "OK", "ОК", "确定" },
        ["Cancel"] = new[] { "Cancel", "Отмена", "取消" },
        ["Assign"] = new[] { "Assign", "Назначить", "确定" },
        ["PressKeys"] = new[] { "Click here and press a key or combination", "Нажмите сюда и затем клавишу или сочетание", "点击此处后按下按键或组合键" },
        ["DpiRange"] = new[] { "DPI must be between 50 and {0}.", "DPI должен быть от 50 до {0}.", "DPI 需在 50 到 {0} 之间。" },
        ["RunTimes"] = new[] { "Run N times", "Выполнить N раз", "执行 N 次" },
        ["RepeatHold"] = new[] { "Repeat while held", "Повторять, пока нажата", "按住时重复" },
        ["ToggleRepeat"] = new[] { "Toggle repeat on press", "Вкл/выкл повтор по нажатию", "按下切换循环" },
        ["Times"] = new[] { "Times", "Раз", "次数" },
        ["Never"] = new[] { "Never", "Никогда", "从不" },
        ["Minutes"] = new[] { "min", "мин", "分钟" },
        ["Charging"] = new[] { "charging", "заряжается", "充电中" },
        ["Exit"] = new[] { "Exit", "Выход", "退出" },
        ["Open"] = new[] { "Open", "Открыть", "打开" },
        ["Error"] = new[] { "Error", "Ошибка", "错误" },
    };
}
