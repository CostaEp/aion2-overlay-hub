using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Aion2PetsOverlay
{
    public class OverlayWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int HOTKEY_CLICKTHROUGH = 9001; // F9
        private const int HOTKEY_TOGGLEVIS = 9002;    // F10
        private const uint VK_F9 = 0x78;
        private const uint VK_F10 = 0x79;

        private static readonly int[] STEPS = new int[] { 5, 25, 75 };
        private const int MAX_LEVEL = 3;
        private const int TOTAL_SOULS_LV3 = 105;

        private bool _isClickThrough = false;
        private string _lang = "HEB"; // "HEB", "ENG", "RUS"
        private string _currentTab = "area";
        private bool _showHubPanel = false;
        private bool _showOptionsPanel = true;
        private bool _updatingLangChecks = false;

        // Active Overlay Modules (App Hub)
        private bool _modPetsTracker = true;
        private bool _modZoneMap = false;

        // Checkbox-controlled display flags for Pets Tracker
        private bool _optShowIcons = true;
        private bool _optShowDistance = true;
        private bool _optShowAroundPin = true;
        private bool _optShowDiamonds = true;
        private bool _optShowRemaining3 = true;
        private bool _optShowPetAndMob = true;
        private bool _optHideLv3 = false;

        private string _baseDir;
        private string _iconsDir;
        private string _stateFilePath;
        private string _overrideFilePath;
        private string _settingsFilePath;
        private string _questlogDir;
        private string _qlTrackerPath;
        private string _qlSelfPath;

        private Process _snifferProc;
        private DispatcherTimer _timer;
        private DateTime _lastStateWrite = DateTime.MinValue;
        private DateTime _lastQlTrackerWrite = DateTime.MinValue;
        private DateTime _lastQlSelfWrite = DateTime.MinValue;
        private Dictionary<string, object> _latestData;
        private bool _updatingMapCombo = false;

        // Interactive Map state
        private double _mapZoom = 1.0;
        private double _mapPanX = 0.0; // in world units
        private double _mapPanY = 0.0; // in world units
        private bool _isMapDragging = false;
        private Point _mapDragStart;
        private int _selectedPetId = 0;

        // Icon cache in memory
        private readonly Dictionary<int, ImageSource> _iconCache = new Dictionary<int, ImageSource>();
        private readonly HashSet<int> _iconDownloading = new HashSet<int>();

        // UI Elements
        private Border _rootBorder;
        private TextBlock _headerTitleText;
        private Button _hubBtn;
        private Button _gearBtn;
        private Button _lockBtn;

        // Hub Panel (Overlay App Selector)
        private Border _hubPanel;
        private TextBlock _hubTitleText;
        private CheckBox _cbModPets;
        private CheckBox _cbModMap;
        private CheckBox _cbModCombatFuture;
        private CheckBox _cbModTimersFuture;

        // Language Checkboxes (Separated!)
        private TextBlock _langLabelText;
        private CheckBox _cbLangHeb;
        private CheckBox _cbLangEng;
        private CheckBox _cbLangRus;

        // Options Panel & Checkboxes
        private Border _optionsPanel;
        private CheckBox _cbIcons;
        private CheckBox _cbDistance;
        private CheckBox _cbMiniMap;
        private CheckBox _cbAroundPin;
        private CheckBox _cbDiamonds;
        private CheckBox _cbRemaining3;
        private CheckBox _cbPetAndMob;
        private CheckBox _cbHideLv3;

        // Pets Tracker Content Container
        private Grid _petsTrackerContainer;
        private TextBlock _summaryAccentText;
        private TextBlock _summaryRestText;
        private ComboBox _mapCombo;
        private TextBox _searchBox;
        private Button _tabAroundBtn;
        private Button _tabAreaBtn;
        private Button _tabMapBtn;
        private Button _tabAllBtn;

        // Mini-Map Panel
        private Border _miniMapBorder;
        private Canvas _miniMapCanvas;
        private TextBlock _miniMapCaption;

        // Cards List
        private StackPanel _cardsStack;

        public OverlayWindow()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _iconsDir = System.IO.Path.Combine(_baseDir, "data", "icons");
            _stateFilePath = System.IO.Path.Combine(_baseDir, "data", "overlay-state.json");
            _overrideFilePath = System.IO.Path.Combine(_baseDir, "data", "map-override.json");
            _settingsFilePath = System.IO.Path.Combine(_baseDir, "data", "ui-settings.json");

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _questlogDir = System.IO.Path.Combine(appData, "Questlog", "aion-2");
            _qlTrackerPath = System.IO.Path.Combine(_questlogDir, "map-tracker.json");
            _qlSelfPath = System.IO.Path.Combine(_questlogDir, "self.json");

            try { if (!Directory.Exists(_iconsDir)) Directory.CreateDirectory(_iconsDir); } catch { }

            Title = "Aion 2 Overlay Hub";
            Width = 450;
            Height = 600;
            MinWidth = 310;
            MinHeight = 220;
            Left = 24;
            Top = 48;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            // Enable resize on all 4 edges + corners via WindowChrome
            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(7),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            };
            WindowChrome.SetWindowChrome(this, chrome);

            LoadSettings();
            FlowDirection = (_lang == "HEB") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            BuildUI();
            ApplyLanguageToStaticUI();
            StartSnifferBackend();

            Loaded += OnLoaded;
            Closed += OnClosed;
            SizeChanged += (s, e) => SaveSettings();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Tick += (s, e) => PollAllStateSources();
            _timer.Start();
            PollAllStateSources();
        }

        private string Tr(string heb, string eng, string rus)
        {
            if (_lang == "ENG") return eng;
            if (_lang == "RUS") return rus;
            return heb;
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;
                string json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) return;
                if (d.ContainsKey("lang") && d["lang"] != null) _lang = d["lang"].ToString();
                if (d.ContainsKey("tab") && d["tab"] != null) _currentTab = d["tab"].ToString();
                if (d.ContainsKey("showHub")) _showHubPanel = GetBool(d, "showHub", false);
                if (d.ContainsKey("showOptions")) _showOptionsPanel = GetBool(d, "showOptions", true);
                if (d.ContainsKey("modPets")) _modPetsTracker = GetBool(d, "modPets", true);
                if (d.ContainsKey("modMap")) _modZoneMap = GetBool(d, "modMap", false);
                if (d.ContainsKey("icons")) _optShowIcons = GetBool(d, "icons", true);
                if (d.ContainsKey("distance")) _optShowDistance = GetBool(d, "distance", true);
                if (d.ContainsKey("aroundPin")) _optShowAroundPin = GetBool(d, "aroundPin", true);
                if (d.ContainsKey("diamonds")) _optShowDiamonds = GetBool(d, "diamonds", true);
                if (d.ContainsKey("remaining3")) _optShowRemaining3 = GetBool(d, "remaining3", true);
                if (d.ContainsKey("petAndMob")) _optShowPetAndMob = GetBool(d, "petAndMob", true);
                if (d.ContainsKey("hideLv3")) _optHideLv3 = GetBool(d, "hideLv3", false);
                if (d.ContainsKey("width"))
                {
                    double w = GetDouble(d, "width", 450);
                    if (w >= 310 && w <= 1600) Width = w;
                }
                if (d.ContainsKey("height"))
                {
                    double h = GetDouble(d, "height", 600);
                    if (h >= 220 && h <= 1400) Height = h;
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var d = new Dictionary<string, object>
                {
                    { "lang", _lang },
                    { "tab", _currentTab },
                    { "showHub", _showHubPanel },
                    { "showOptions", _showOptionsPanel },
                    { "modPets", _modPetsTracker },
                    { "modMap", _modZoneMap },
                    { "icons", _optShowIcons },
                    { "distance", _optShowDistance },
                    { "aroundPin", _optShowAroundPin },
                    { "diamonds", _optShowDiamonds },
                    { "remaining3", _optShowRemaining3 },
                    { "petAndMob", _optShowPetAndMob },
                    { "hideLv3", _optHideLv3 },
                    { "width", Math.Round(Width) },
                    { "height", Math.Round(Height) }
                };
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(_settingsFilePath, ser.Serialize(d), Encoding.UTF8);
            }
            catch { }
        }

        private void SetLanguage(string newLang)
        {
            if (_updatingLangChecks) return;
            _lang = newLang;
            _updatingLangChecks = true;
            _cbLangHeb.IsChecked = (_lang == "HEB");
            _cbLangEng.IsChecked = (_lang == "ENG");
            _cbLangRus.IsChecked = (_lang == "RUS");
            _updatingLangChecks = false;

            FlowDirection = (_lang == "HEB") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            SaveSettings();
            ApplyLanguageToStaticUI();
            RefreshView();
        }

        private void ApplyLanguageToStaticUI()
        {
            _updatingLangChecks = true;
            _cbLangHeb.IsChecked = (_lang == "HEB");
            _cbLangEng.IsChecked = (_lang == "ENG");
            _cbLangRus.IsChecked = (_lang == "RUS");
            _updatingLangChecks = false;

            _hubBtn.Content = Tr("🧩 Overlays", "🧩 Overlays", "🧩 Оверлеи");
            _hubBtn.ToolTip = Tr("בחירת Overlays פעילים", "Select Active Overlays", "Выбор активных оверлеев");
            _lockBtn.ToolTip = Tr("מעבר עכבר (F9)", "Click-Through (F9)", "Сквозной клик (F9)");
            _gearBtn.ToolTip = Tr("אפשרויות תצוגה ושפה", "Display & Language Options", "Настройки и язык");

            _hubTitleText.Text = Tr(
                "🧩 מרכז ה-Overlays (בחר אילו Overlays להפעיל):",
                "🧩 Overlay App Hub (Select active overlays):",
                "🧩 Центр Оверлеев (Выберите активные оверлеи):"
            );
            _cbModPets.Content = Tr(
                "🐾 מעקב חיות ונשמות לשלב 3 (Pets & Souls Tracker)",
                "🐾 Pets & Souls Tracker (Level 3 Progress)",
                "🐾 Трекер питомцев и душ (До Ур. 3)"
            );
            _cbModMap.Content = Tr(
                "🗺 מפת אזור וספאונים חיה (Zone Spawns Map)",
                "🗺 Live Zone & Spawns Map",
                "🗺 Карта зоны и спавнов"
            );
            _cbModCombatFuture.Content = Tr(
                "⚔️ מד נזק וקרב (DPS Meter — יתווסף בהמשך)",
                "⚔️ Combat / DPS Meter (Coming Soon)",
                "⚔️ ДПС Метр (Скоро)"
            );
            _cbModTimersFuture.Content = Tr(
                "⏱️ טיימרים של בוסים ואירועים (Timers — יתווסף בהמשך)",
                "⏱️ Boss & Event Timers (Coming Soon)",
                "⏱️ Таймеры боссов и событий (Скоро)"
            );

            _langLabelText.Text = Tr("🌐 שפה:", "🌐 Language:", "🌐 Язык:");

            _tabAroundBtn.Content = Tr("🟢 סביבי", "🟢 Around", "🟢 Рядом");
            _tabAreaBtn.Content = Tr("📍 באזור", "📍 Here", "📍 В зоне");
            _tabMapBtn.Content = Tr("🗺 במפה", "🗺 Map", "🗺 Карта");
            _tabAllBtn.Content = Tr("הכל", "All", "Все");

            _cbIcons.Content = Tr("🖼 תמונות חיות", "🖼 Pet Icons", "🖼 Иконки");
            _cbDistance.Content = Tr("🧭 מרחק וכיוון (m)", "🧭 Distance & Arrow", "🧭 Дистанция (m)");
            _cbMiniMap.Content = Tr("🗺 הצג מפה", "🗺 Show Map", "🗺 Показать карту");
            _cbAroundPin.Content = Tr("📍 כמות סביבי", "📍 Spawn/Around Pin", "📍 Кол-во рядом");
            _cbDiamonds.Content = Tr("💎 יהלומי שלב (◆◆◆)", "💎 Level Diamonds", "💎 Уровень (◆◆◆)");
            _cbRemaining3.Content = Tr("🎯 כמה חסר לשלב 3", "🎯 Missing to Lv.3", "🎯 Осталось до Ур.3");
            _cbPetAndMob.Content = Tr("🐾 שם מפלצת + חיה", "🐾 Monster + Pet Name", "🐾 Монстр + Питомец");
            _cbHideLv3.Content = Tr("👁 הסתר שלב 3 (נשאר 0)", "👁 Hide Lv.3 (0 left)", "👁 Скрыть Ур.3 (0)");
        }

        private void StartSnifferBackend()
        {
            try
            {
                string snifferScript = System.IO.Path.Combine(_baseDir, "sniffer.cjs");
                string nodeRunner = @"C:\Users\costa\AppData\Local\Programs\antigravity\Antigravity.exe";
                if (!File.Exists(nodeRunner) || !File.Exists(snifferScript)) return;

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = nodeRunner,
                    Arguments = "\"" + snifferScript + "\"",
                    WorkingDirectory = _baseDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
                _snifferProc = Process.Start(psi);
            }
            catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null) src.AddHook(WndProc);
            RegisterHotKey(hwnd, HOTKEY_CLICKTHROUGH, 0, VK_F9);
            RegisterHotKey(hwnd, HOTKEY_TOGGLEVIS, 0, VK_F10);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_CLICKTHROUGH);
            UnregisterHotKey(hwnd, HOTKEY_TOGGLEVIS);
            try
            {
                if (_snifferProc != null && !_snifferProc.HasExited)
                    _snifferProc.Kill();
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_CLICKTHROUGH)
                {
                    ToggleClickThrough();
                    handled = true;
                }
                else if (id == HOTKEY_TOGGLEVIS)
                {
                    Visibility = (Visibility == Visibility.Visible) ? Visibility.Hidden : Visibility.Visible;
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void ToggleClickThrough()
        {
            _isClickThrough = !_isClickThrough;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (_isClickThrough)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                _lockBtn.Content = "🔒";
                _lockBtn.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                _rootBorder.Opacity = 0.85;
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                _lockBtn.Content = "🔓";
                _lockBtn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                _rootBorder.Opacity = 0.96;
            }
        }

        private void BuildUI()
        {
            _rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(238, 13, 16, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(85, 56, 189, 248)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 8, 10, 6)
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: App Hub Panel (Overlay Selector)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Checkbox & Language Options Panel
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Active Overlays Body
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Bottom Resize Bar

            // ================================================================
            // 0. Top Header Bar
            // ================================================================
            Border headerBar = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(2, 0, 2, 6),
                Cursor = Cursors.SizeAll
            };
            headerBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            DockPanel headerDock = new DockPanel { LastChildFill = true };

            StackPanel actionBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _hubBtn = new Button
            {
                Content = "🧩 Overlays",
                Height = 22,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromArgb(65, 56, 189, 248)),
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            _hubBtn.Click += (s, e) =>
            {
                _showHubPanel = !_showHubPanel;
                _hubPanel.Visibility = _showHubPanel ? Visibility.Visible : Visibility.Collapsed;
                SaveSettings();
            };
            actionBtns.Children.Add(_hubBtn);

            _gearBtn = CreateIconBtn("⚙", () =>
            {
                _showOptionsPanel = !_showOptionsPanel;
                _optionsPanel.Visibility = _showOptionsPanel ? Visibility.Visible : Visibility.Collapsed;
                SaveSettings();
            });
            actionBtns.Children.Add(_gearBtn);

            _lockBtn = CreateIconBtn("🔓", ToggleClickThrough);
            actionBtns.Children.Add(_lockBtn);

            Button closeBtn = CreateIconBtn("✕", Close);
            closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            actionBtns.Children.Add(closeBtn);

            DockPanel.SetDock(actionBtns, Dock.Left);
            headerDock.Children.Add(actionBtns);

            _headerTitleText = new TextBlock
            {
                Text = "Tracker · ...",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerDock.Children.Add(_headerTitleText);

            headerBar.Child = headerDock;
            Grid.SetRow(headerBar, 0);
            mainGrid.Children.Add(headerBar);

            // ================================================================
            // 1. Overlay App Hub Panel (Select which Overlays to use!)
            // ================================================================
            _hubPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 16, 24, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = _showHubPanel ? Visibility.Visible : Visibility.Collapsed
            };

            StackPanel hubStack = new StackPanel();
            _hubTitleText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            };
            hubStack.Children.Add(_hubTitleText);

            _cbModPets = CreateOptionCheck(_modPetsTracker, v =>
            {
                _modPetsTracker = v;
                _petsTrackerContainer.Visibility = _modPetsTracker ? Visibility.Visible : Visibility.Collapsed;
                SaveSettings();
            });
            _cbModPets.FontWeight = FontWeights.Bold;
            hubStack.Children.Add(_cbModPets);

            _cbModMap = CreateOptionCheck(_modZoneMap, v =>
            {
                _modZoneMap = v;
                _cbMiniMap.IsChecked = v;
                _miniMapBorder.Visibility = _modZoneMap ? Visibility.Visible : Visibility.Collapsed;
                SaveSettings();
                RefreshView();
            });
            hubStack.Children.Add(_cbModMap);

            _cbModCombatFuture = new CheckBox
            {
                IsChecked = false,
                IsEnabled = false,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 2)
            };
            hubStack.Children.Add(_cbModCombatFuture);

            _cbModTimersFuture = new CheckBox
            {
                IsChecked = false,
                IsEnabled = false,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 2)
            };
            hubStack.Children.Add(_cbModTimersFuture);

            _hubPanel.Child = hubStack;
            Grid.SetRow(_hubPanel, 1);
            mainGrid.Children.Add(_hubPanel);

            // ================================================================
            // 2. Options & Separated Language Checkboxes Panel
            // ================================================================
            _optionsPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(205, 20, 26, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = _showOptionsPanel ? Visibility.Visible : Visibility.Collapsed
            };

            StackPanel optStack = new StackPanel();

            // Row A: 3 Separate Language Checkboxes (HEB / ENG / RUS)
            Border langRowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 10, 15, 25)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 6)
            };
            WrapPanel langWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            _langLabelText = new TextBlock
            {
                Text = "🌐 שפה:",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            langWrap.Children.Add(_langLabelText);

            _cbLangHeb = CreateOptionCheck(_lang == "HEB", v => { if (v) SetLanguage("HEB"); else SetLanguage(_lang); });
            _cbLangHeb.Content = "עברית (HEB)";
            _cbLangEng = CreateOptionCheck(_lang == "ENG", v => { if (v) SetLanguage("ENG"); else SetLanguage(_lang); });
            _cbLangEng.Content = "English (ENG)";
            _cbLangRus = CreateOptionCheck(_lang == "RUS", v => { if (v) SetLanguage("RUS"); else SetLanguage(_lang); });
            _cbLangRus.Content = "Русский (RUS)";

            langWrap.Children.Add(_cbLangHeb);
            langWrap.Children.Add(_cbLangEng);
            langWrap.Children.Add(_cbLangRus);
            langRowBorder.Child = langWrap;
            optStack.Children.Add(langRowBorder);

            // Row B: Display Feature Checkboxes
            WrapPanel cbWrap = new WrapPanel();

            _cbIcons = CreateOptionCheck(_optShowIcons, v => { _optShowIcons = v; OnOptionChanged(); });
            _cbDistance = CreateOptionCheck(_optShowDistance, v => { _optShowDistance = v; OnOptionChanged(); });
            _cbMiniMap = CreateOptionCheck(_modZoneMap, v =>
            {
                _modZoneMap = v;
                _cbModMap.IsChecked = v;
                OnOptionChanged();
            });
            _cbAroundPin = CreateOptionCheck(_optShowAroundPin, v => { _optShowAroundPin = v; OnOptionChanged(); });
            _cbDiamonds = CreateOptionCheck(_optShowDiamonds, v => { _optShowDiamonds = v; OnOptionChanged(); });
            _cbRemaining3 = CreateOptionCheck(_optShowRemaining3, v => { _optShowRemaining3 = v; OnOptionChanged(); });
            _cbPetAndMob = CreateOptionCheck(_optShowPetAndMob, v => { _optShowPetAndMob = v; OnOptionChanged(); });
            _cbHideLv3 = CreateOptionCheck(_optHideLv3, v => { _optHideLv3 = v; OnOptionChanged(); });

            cbWrap.Children.Add(_cbIcons);
            cbWrap.Children.Add(_cbDistance);
            cbWrap.Children.Add(_cbMiniMap);
            cbWrap.Children.Add(_cbAroundPin);
            cbWrap.Children.Add(_cbDiamonds);
            cbWrap.Children.Add(_cbRemaining3);
            cbWrap.Children.Add(_cbPetAndMob);
            cbWrap.Children.Add(_cbHideLv3);
            optStack.Children.Add(cbWrap);

            // Row C: Map Selector + Search Box
            DockPanel filterRow = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            _mapCombo = new ComboBox
            {
                Width = 135,
                Height = 22,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                DisplayMemberPath = "Value",
                SelectedValuePath = "Key"
            };
            _mapCombo.SelectionChanged += OnMapSelectionChanged;
            DockPanel.SetDock(_mapCombo, Dock.Right);
            filterRow.Children.Add(_mapCombo);

            _searchBox = new TextBox
            {
                Height = 22,
                Background = new SolidColorBrush(Color.FromRgb(13, 18, 28)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Padding = new Thickness(5, 0, 5, 0)
            };
            _searchBox.TextChanged += (s, e) => RefreshView();
            filterRow.Children.Add(_searchBox);

            optStack.Children.Add(filterRow);
            _optionsPanel.Child = optStack;

            Grid.SetRow(_optionsPanel, 2);
            mainGrid.Children.Add(_optionsPanel);

            // ================================================================
            // 3. Active Overlay Body: Pets Tracker + Zone Map
            // ================================================================
            _petsTrackerContainer = new Grid
            {
                Visibility = _modPetsTracker ? Visibility.Visible : Visibility.Collapsed
            };
            _petsTrackerContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Summary + Tabs
            _petsTrackerContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Interactive Zone Map
            _petsTrackerContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Cards List

            // 3.0 Summary + Scope Tabs
            DockPanel subBar = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
            StackPanel tabsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _tabAroundBtn = CreateTabBtn("🟢", "around");
            _tabAreaBtn = CreateTabBtn("📍", "area");
            _tabMapBtn = CreateTabBtn("🗺", "map");
            _tabAllBtn = CreateTabBtn("All", "all");
            tabsPanel.Children.Add(_tabAroundBtn);
            tabsPanel.Children.Add(_tabAreaBtn);
            tabsPanel.Children.Add(_tabMapBtn);
            tabsPanel.Children.Add(_tabAllBtn);
            UpdateAllTabVisuals();

            DockPanel.SetDock(tabsPanel, Dock.Left);
            subBar.Children.Add(tabsPanel);

            WrapPanel summaryWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            _summaryAccentText = new TextBlock
            {
                Text = "Get missing pets: ",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };
            _summaryRestText = new TextBlock
            {
                Text = "...",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 12
            };
            summaryWrap.Children.Add(_summaryAccentText);
            summaryWrap.Children.Add(_summaryRestText);
            subBar.Children.Add(summaryWrap);

            Grid.SetRow(subBar, 0);
            _petsTrackerContainer.Children.Add(subBar);

            // 3.1 Interactive Zone & Spawns Map (Pan with mouse drag, Zoom with wheel/buttons, Labeled Pet Clusters!)
            _miniMapBorder = new Border
            {
                Height = 200,
                Background = new SolidColorBrush(Color.FromArgb(235, 8, 12, 20)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(95, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 6),
                ClipToBounds = true,
                Visibility = _modZoneMap ? Visibility.Visible : Visibility.Collapsed
            };

            Grid mapContainer = new Grid();
            _miniMapCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            _miniMapCanvas.SizeChanged += (s, e) => RenderMiniMap();
            _miniMapCanvas.MouseWheel += (s, e) =>
            {
                if (e.Delta > 0) _mapZoom = Math.Min(6.0, _mapZoom * 1.25);
                else _mapZoom = Math.Max(0.25, _mapZoom / 1.25);
                RenderMiniMap();
            };
            _miniMapCanvas.MouseLeftButtonDown += (s, e) =>
            {
                _isMapDragging = true;
                _mapDragStart = e.GetPosition(_miniMapCanvas);
                _miniMapCanvas.CaptureMouse();
            };
            _miniMapCanvas.MouseMove += (s, e) =>
            {
                if (!_isMapDragging) return;
                Point cur = e.GetPosition(_miniMapCanvas);
                double dx = cur.X - _mapDragStart.X;
                double dy = cur.Y - _mapDragStart.Y;
                _mapDragStart = cur;
                double w = Math.Max(100, _miniMapCanvas.ActualWidth);
                double h = Math.Max(100, _miniMapCanvas.ActualHeight);
                double scale = (Math.Min(w, h) * 0.42) / (32000.0 / _mapZoom);
                // Account for RTL canvas transform if Hebrew
                double dir = (_lang == "HEB") ? -1.0 : 1.0;
                _mapPanX -= (dx * dir) / scale;
                _mapPanY -= dy / scale;
                RenderMiniMap();
            };
            _miniMapCanvas.MouseLeftButtonUp += (s, e) =>
            {
                _isMapDragging = false;
                _miniMapCanvas.ReleaseMouseCapture();
            };
            mapContainer.Children.Add(_miniMapCanvas);

            // Map Controls: +, −, Reset, Taller/Shorter
            StackPanel mapBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4)
            };
            mapBtns.Children.Add(CreateIconBtn("+", () => { _mapZoom = Math.Min(6.0, _mapZoom * 1.35); RenderMiniMap(); }));
            mapBtns.Children.Add(CreateIconBtn("−", () => { _mapZoom = Math.Max(0.25, _mapZoom / 1.35); RenderMiniMap(); }));
            mapBtns.Children.Add(CreateIconBtn("🎯", () => { _mapPanX = 0; _mapPanY = 0; _mapZoom = 1.0; RenderMiniMap(); }));
            mapBtns.Children.Add(CreateIconBtn("↕", () =>
            {
                _miniMapBorder.Height = (_miniMapBorder.Height < 260) ? 300 : 190;
                RenderMiniMap();
            }));
            mapContainer.Children.Add(mapBtns);

            _miniMapCaption = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6, 0, 6, 4),
                IsHitTestVisible = false
            };
            mapContainer.Children.Add(_miniMapCaption);

            _miniMapBorder.Child = mapContainer;
            Grid.SetRow(_miniMapBorder, 1);
            _petsTrackerContainer.Children.Add(_miniMapBorder);

            // 3.2 Cards List
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _cardsStack = new StackPanel { Orientation = Orientation.Vertical };
            scroll.Content = _cardsStack;

            Grid.SetRow(scroll, 2);
            _petsTrackerContainer.Children.Add(scroll);

            Grid.SetRow(_petsTrackerContainer, 3);
            mainGrid.Children.Add(_petsTrackerContainer);

            // ================================================================
            // 4. Bottom Resize Status & Grip Bar
            // ================================================================
            DockPanel bottomBar = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            TextBlock resizeHint = new TextBlock
            {
                Text = "⤡ Drag edges or corner to resize",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            bottomBar.Children.Add(resizeHint);

            Grid.SetRow(bottomBar, 4);
            mainGrid.Children.Add(bottomBar);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;
        }

        private Button CreateIconBtn(string text, Action onClick)
        {
            Button b = new Button
            {
                Content = text,
                Width = 24,
                Height = 22,
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private CheckBox CreateOptionCheck(bool initial, Action<bool> onChanged)
        {
            CheckBox cb = new CheckBox
            {
                IsChecked = initial,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 10, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            cb.Checked += (s, e) => onChanged(true);
            cb.Unchecked += (s, e) => onChanged(false);
            return cb;
        }

        private void OnOptionChanged()
        {
            _miniMapBorder.Visibility = _modZoneMap ? Visibility.Visible : Visibility.Collapsed;
            SaveSettings();
            RefreshView();
        }

        private Button CreateTabBtn(string label, string key)
        {
            Button b = new Button
            {
                Content = label,
                Tag = key,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };
            b.Click += (s, e) =>
            {
                _currentTab = key;
                SaveSettings();
                UpdateAllTabVisuals();
                RefreshView();
            };
            return b;
        }

        private void UpdateAllTabVisuals()
        {
            UpdateTabBtnStyle(_tabAroundBtn, _currentTab == "around");
            UpdateTabBtnStyle(_tabAreaBtn, _currentTab == "area");
            UpdateTabBtnStyle(_tabMapBtn, _currentTab == "map");
            UpdateTabBtnStyle(_tabAllBtn, _currentTab == "all");
        }

        private void UpdateTabBtnStyle(Button b, bool active)
        {
            if (b == null) return;
            if (active)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(75, 56, 189, 248));
                b.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            }
            else
            {
                b.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
                b.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                b.BorderBrush = Brushes.Transparent;
            }
        }

        private void OnMapSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingMapCombo || _mapCombo.SelectedValue == null) return;
            string mapId = _mapCombo.SelectedValue.ToString();
            try
            {
                long nowMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                string json = "{\"mapId\":\"" + mapId + "\",\"at\":" + nowMs + "}";
                File.WriteAllText(_overrideFilePath, json, Encoding.UTF8);
                _mapPanX = 0;
                _mapPanY = 0;
            }
            catch { }
        }

        private static string ReadFileShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        // ====================================================================
        // Real-Time Polling: Reads overlay-state.json AND Questlog live files!
        // ====================================================================
        private void PollAllStateSources()
        {
            try
            {
                bool stateChanged = false;
                bool qlChanged = false;

                if (File.Exists(_stateFilePath))
                {
                    DateTime wt = File.GetLastWriteTimeUtc(_stateFilePath);
                    if (wt != _lastStateWrite || _latestData == null)
                    {
                        JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        _latestData = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(_stateFilePath));
                        _lastStateWrite = wt;
                        stateChanged = true;
                    }
                }

                if (_latestData == null) return;

                if (File.Exists(_qlTrackerPath))
                {
                    DateTime wt = File.GetLastWriteTimeUtc(_qlTrackerPath);
                    if (wt != _lastQlTrackerWrite)
                    {
                        _lastQlTrackerWrite = wt;
                        qlChanged = true;
                    }
                }

                if (File.Exists(_qlSelfPath))
                {
                    DateTime wt = File.GetLastWriteTimeUtc(_qlSelfPath);
                    if (wt != _lastQlSelfWrite)
                    {
                        _lastQlSelfWrite = wt;
                        qlChanged = true;
                    }
                }

                if (stateChanged || qlChanged)
                {
                    ApplyLiveQuestlogPatch();
                    RefreshView();
                }
            }
            catch { }
        }

        private void ApplyLiveQuestlogPatch()
        {
            if (_latestData == null) return;
            JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            Dictionary<string, object> levelsDict = null;
            Dictionary<string, object> soulsDict = null;
            Dictionary<int, int> aroundByNpc = new Dictionary<int, int>();
            bool hasLiveSelf = false;

            try
            {
                if (File.Exists(_qlTrackerPath))
                {
                    var trk = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(_qlTrackerPath));
                    if (trk != null && trk.ContainsKey("character"))
                    {
                        var ch = trk["character"] as Dictionary<string, object>;
                        if (ch != null)
                        {
                            levelsDict = ch.ContainsKey("levels") ? ch["levels"] as Dictionary<string, object> : null;
                            soulsDict = ch.ContainsKey("souls") ? ch["souls"] as Dictionary<string, object> : null;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (File.Exists(_qlSelfPath))
                {
                    var sf = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(_qlSelfPath));
                    if (sf != null && sf.ContainsKey("npcs"))
                    {
                        var npcsArr = sf["npcs"] as ArrayList;
                        if (npcsArr != null)
                        {
                            hasLiveSelf = true;
                            foreach (object nObj in npcsArr)
                            {
                                var nd = nObj as Dictionary<string, object>;
                                if (nd != null)
                                {
                                    int npcId = GetInt(nd, "npc", 0);
                                    if (npcId > 0)
                                        aroundByNpc[npcId] = (aroundByNpc.ContainsKey(npcId) ? aroundByNpc[npcId] : 0) + 1;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var petsList = _latestData.ContainsKey("pets") ? _latestData["pets"] as ArrayList : null;
            if (petsList == null) return;

            foreach (object itemObj in petsList)
            {
                var p = itemObj as Dictionary<string, object>;
                if (p == null) continue;
                int petId = GetInt(p, "id", 0);
                string petKey = petId.ToString();

                int rawLv = GetInt(p, "level", 0);
                int rawSouls = GetInt(p, "souls", 0);

                if (levelsDict != null && levelsDict.ContainsKey(petKey))
                    rawLv = Math.Max(rawLv, GetInt(levelsDict, petKey, rawLv));
                if (soulsDict != null && soulsDict.ContainsKey(petKey))
                    rawSouls = GetInt(soulsDict, petKey, rawSouls);

                // Recalculate exact progress to Level 3
                int effLv = rawLv;
                int effSouls = rawSouls;
                while (effLv < MAX_LEVEL && effSouls >= STEPS[effLv])
                {
                    effSouls -= STEPS[effLv];
                    effLv++;
                }

                int neededFromRawTo3 = 0;
                for (int l = rawLv; l < MAX_LEVEL; l++) neededFromRawTo3 += STEPS[l];

                bool isLv3 = rawLv >= MAX_LEVEL;
                int remTo3 = isLv3 ? 0 : Math.Max(0, neededFromRawTo3 - rawSouls);
                int nextNeed = effLv < MAX_LEVEL ? STEPS[effLv] : 0;
                int inStep = effLv >= MAX_LEVEL ? 0 : Math.Max(0, effSouls);
                double fill = isLv3 ? 1.0 : (nextNeed > 0 ? Math.Min(1.0, (double)inStep / nextNeed) : 0.0);

                p["level"] = rawLv;
                p["effectiveLevel"] = effLv;
                p["souls"] = rawSouls;
                p["soulsInStep"] = inStep;
                p["nextStepNeed"] = nextNeed;
                p["remainingToLevel3"] = remTo3;
                p["isLevel3"] = isLv3;
                p["stepFillRatio"] = fill;

                if (hasLiveSelf && p.ContainsKey("monsters"))
                {
                    var mobs = p["monsters"] as ArrayList;
                    if (mobs != null)
                    {
                        int cnt = 0;
                        string topMob = null;
                        foreach (object mObj in mobs)
                        {
                            var md = mObj as Dictionary<string, object>;
                            if (md != null)
                            {
                                int mid = GetInt(md, "id", 0);
                                if (aroundByNpc.ContainsKey(mid))
                                {
                                    cnt += aroundByNpc[mid];
                                    if (topMob == null) topMob = GetStr(md, "name", null);
                                }
                            }
                        }
                        p["aroundCount"] = cnt;
                        if (topMob != null) p["primaryMonsterName"] = topMob;
                    }
                }
            }
        }

        private ImageSource GetOrDownloadPetIcon(int petId, string iconUrl)
        {
            if (_iconCache.ContainsKey(petId)) return _iconCache[petId];

            string localPath = System.IO.Path.Combine(_iconsDir, petId + ".webp");
            if (File.Exists(localPath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(localPath);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        if (dec.Frames.Count > 0)
                        {
                            BitmapSource frame = dec.Frames[0];
                            frame.Freeze();
                            _iconCache[petId] = frame;
                            return frame;
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(iconUrl) && !_iconDownloading.Contains(petId))
            {
                _iconDownloading.Add(petId);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (WebClient wc = new WebClient())
                        {
                            byte[] data = wc.DownloadData(iconUrl);
                            if (data != null && data.Length > 100)
                            {
                                try { File.WriteAllBytes(localPath, data); } catch { }
                                using (MemoryStream ms = new MemoryStream(data))
                                {
                                    var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                    if (dec.Frames.Count > 0)
                                    {
                                        BitmapSource frame = dec.Frames[0];
                                        frame.Freeze();
                                        Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            _iconCache[petId] = frame;
                                            RefreshView();
                                        }));
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                });
            }

            return null;
        }

        private static string GetStr(Dictionary<string, object> dict, string key, string defVal = "")
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return defVal;
            return dict[key].ToString();
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int defVal = 0)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return defVal;
            int v;
            return int.TryParse(dict[key].ToString(), out v) ? v : defVal;
        }

        private static double GetDouble(Dictionary<string, object> dict, string key, double defVal = 0.0)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return defVal;
            double v;
            return double.TryParse(dict[key].ToString(), out v) ? v : defVal;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key, bool defVal = false)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return defVal;
            bool v;
            return bool.TryParse(dict[key].ToString(), out v) ? v : defVal;
        }

        private void RefreshView()
        {
            if (_latestData == null) return;

            var locDict = _latestData.ContainsKey("location") ? _latestData["location"] as Dictionary<string, object> : null;
            var worldsDict = _latestData.ContainsKey("worlds") ? _latestData["worlds"] as Dictionary<string, object> : null;

            string curAreaName = GetStr(locDict, "areaName", "");
            string curMapName = GetStr(locDict, "mapName", "Altgard");
            string headerPlace = !string.IsNullOrEmpty(curAreaName) ? curAreaName : curMapName;
            _headerTitleText.Text = Tr("מעקב · ", "Tracker · ", "Трекер · ") + headerPlace;

            if (worldsDict != null && _mapCombo.Items.Count == 0)
            {
                _updatingMapCombo = true;
                var items = new List<KeyValuePair<string, string>>();
                foreach (var kv in worldsDict)
                {
                    var wObj = kv.Value as Dictionary<string, object>;
                    string wName = GetStr(wObj, "name", kv.Key);
                    items.Add(new KeyValuePair<string, string>(kv.Key, wName));
                }
                _mapCombo.ItemsSource = items;
                _updatingMapCombo = false;
            }

            string curMapId = GetStr(locDict, "mapId", "");
            if (!string.IsNullOrEmpty(curMapId) && (_mapCombo.SelectedValue == null || _mapCombo.SelectedValue.ToString() != curMapId))
            {
                _updatingMapCombo = true;
                _mapCombo.SelectedValue = curMapId;
                _updatingMapCombo = false;
            }

            string q = (_searchBox.Text ?? "").Trim().ToLowerInvariant();
            var petsList = _latestData.ContainsKey("pets") ? _latestData["pets"] as ArrayList : null;
            if (petsList == null) return;

            var filteredPets = new List<Dictionary<string, object>>();
            int missingPetsHere = 0;
            int missingSoulsHere = 0;

            foreach (object itemObj in petsList)
            {
                var p = itemObj as Dictionary<string, object>;
                if (p == null) continue;

                int aroundCount = GetInt(p, "aroundCount", 0);
                int spawnsInArea = GetInt(p, "spawnsInCurrentArea", 0);
                int spawnsInMap = GetInt(p, "spawnsInCurrentMap", 0);
                bool isLv3 = GetBool(p, "isLevel3", false);
                int remTo3 = GetInt(p, "remainingToLevel3", 0);
                string name = GetStr(p, "name", "");
                string mobName = GetStr(p, "primaryMonsterName", name);

                var areasArr = p.ContainsKey("currentMapAreas") ? p["currentMapAreas"] as ArrayList : null;
                bool isInCurArea = spawnsInArea > 0;
                if (!isInCurArea && areasArr != null && !string.IsNullOrEmpty(curAreaName))
                {
                    foreach (object a in areasArr)
                        if (a != null && a.ToString() == curAreaName) isInCurArea = true;
                }

                if (_currentTab == "around" && aroundCount <= 0) continue;
                if (_currentTab == "area" && aroundCount <= 0 && !isInCurArea) continue;
                if (_currentTab == "map" && spawnsInMap <= 0 && aroundCount <= 0) continue;
                if (_optHideLv3 && isLv3) continue;

                if (!string.IsNullOrEmpty(q))
                {
                    if (!name.ToLowerInvariant().Contains(q) && !mobName.ToLowerInvariant().Contains(q))
                        continue;
                }

                if (!isLv3)
                {
                    missingPetsHere++;
                    missingSoulsHere += remTo3;
                }

                filteredPets.Add(p);
            }

            // Sort so live Around pets are always at the very top!
            filteredPets.Sort((a, b) =>
            {
                int arA = GetInt(a, "aroundCount", 0);
                int arB = GetInt(b, "aroundCount", 0);
                if ((arB > 0) != (arA > 0)) return (arB > 0) ? 1 : -1;
                if (arB != arA) return arB - arA;
                bool lv3A = GetBool(a, "isLevel3", false);
                bool lv3B = GetBool(b, "isLevel3", false);
                if (lv3A != lv3B) return lv3A ? 1 : -1;
                return GetInt(a, "remainingToLevel3", 0) - GetInt(b, "remainingToLevel3", 0);
            });

            _summaryAccentText.Text = Tr("חיות להשלמה: ", "Get missing pets: ", "Питомцы: ");
            _summaryRestText.Text = string.Format(
                Tr("{0} כאן · {1} נשמות חסרות", "{0} here · {1} souls left", "{0} здесь · {1} душ осталось"),
                missingPetsHere, missingSoulsHere
            );

            _cardsStack.Children.Clear();
            int maxCards = Math.Min(filteredPets.Count, 40);
            for (int i = 0; i < maxCards; i++)
            {
                _cardsStack.Children.Add(BuildPetCard(filteredPets[i]));
            }

            if (_modZoneMap)
            {
                RenderMiniMap();
            }
        }

        private UIElement BuildPetCard(Dictionary<string, object> p)
        {
            int petId = GetInt(p, "id", 0);
            string petName = GetStr(p, "name", "");
            string mobName = GetStr(p, "primaryMonsterName", petName);
            string iconUrl = GetStr(p, "icon", "");
            int level = GetInt(p, "level", 0);
            bool isLv3 = GetBool(p, "isLevel3", false);
            int soulsInStep = GetInt(p, "soulsInStep", 0);
            int nextStepNeed = GetInt(p, "nextStepNeed", 0);
            int remTo3 = GetInt(p, "remainingToLevel3", 0);
            double fillRatio = GetDouble(p, "stepFillRatio", 0.0);
            int aroundCount = GetInt(p, "aroundCount", 0);
            int spawnsInArea = GetInt(p, "spawnsInCurrentArea", 0);
            int spawnsInMap = GetInt(p, "spawnsInCurrentMap", 0);
            bool hasDist = p.ContainsKey("nearestDistanceM") && p["nearestDistanceM"] != null;
            int distM = hasDist ? GetInt(p, "nearestDistanceM", 0) : -1;
            bool isLiveDist = GetBool(p, "isLiveDistance", false);
            string dirArrow = GetStr(p, "directionArrow", "←");

            bool isSelected = (_selectedPetId == petId);

            Border cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 26, 38)),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                    : (aroundCount > 0 ? new SolidColorBrush(Color.FromArgb(110, 52, 211, 153)) : Brushes.Transparent),
                BorderThickness = new Thickness(isSelected || aroundCount > 0 ? 1 : 0),
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 0, 4),
                MinHeight = 46,
                ClipToBounds = true,
                Cursor = Cursors.Hand
            };

            cardBorder.MouseLeftButtonDown += (s, e) =>
            {
                _selectedPetId = (_selectedPetId == petId) ? 0 : petId;
                if (_modZoneMap) RenderMiniMap();
                RefreshView();
            };

            Grid cardGrid = new Grid();

            if (fillRatio > 0.0)
            {
                Grid fillGrid = new Grid { IsHitTestVisible = false };
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, fillRatio), GridUnitType.Star) });
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0, 1.0 - fillRatio), GridUnitType.Star) });

                Color fillColor = isLv3
                    ? Color.FromArgb(65, 16, 185, 129)
                    : Color.FromArgb(85, 30, 88, 148);
                Color edgeColor = isLv3
                    ? Color.FromRgb(52, 211, 153)
                    : Color.FromRgb(56, 189, 248);

                Border fillBar = new Border
                {
                    Background = new SolidColorBrush(fillColor),
                    BorderBrush = new SolidColorBrush(edgeColor),
                    BorderThickness = new Thickness(0, 0, 2, 0)
                };
                Grid.SetColumn(fillBar, 0);
                fillGrid.Children.Add(fillBar);
                cardGrid.Children.Add(fillGrid);
            }

            DockPanel contentDock = new DockPanel
            {
                Margin = new Thickness(7, 5, 9, 5),
                LastChildFill = true
            };

            if (_optShowIcons)
            {
                Border iconBox = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromRgb(10, 14, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ClipToBounds = true
                };

                ImageSource imgSrc = GetOrDownloadPetIcon(petId, iconUrl);
                if (imgSrc != null)
                {
                    iconBox.Child = new Image { Source = imgSrc, Stretch = Stretch.UniformToFill };
                }
                else
                {
                    iconBox.Child = new TextBlock
                    {
                        Text = "🐾",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 14
                    };
                }

                DockPanel.SetDock(iconBox, Dock.Left);
                contentDock.Children.Add(iconBox);
            }

            StackPanel rightPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };

            if (_optShowAroundPin)
            {
                int pinCount = aroundCount > 0 ? aroundCount : (spawnsInArea > 0 ? spawnsInArea : spawnsInMap);
                TextBlock pinText = new TextBlock
                {
                    Text = (aroundCount > 0 ? "🟢 " + pinCount : pinCount.ToString()) + " 📍",
                    Foreground = aroundCount > 0
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 11.5,
                    FontWeight = aroundCount > 0 ? FontWeights.ExtraBold : FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                rightPanel.Children.Add(pinText);
            }

            if (_optShowDistance && hasDist && distM >= 0)
            {
                string distStr = distM < 1000 ? (distM + " m") : ((distM / 1000.0).ToString("0.1") + " km");
                TextBlock distText = new TextBlock
                {
                    Text = (string.IsNullOrEmpty(dirArrow) ? "←" : dirArrow) + " " + distStr,
                    Foreground = isLiveDist
                        ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
                        : Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                rightPanel.Children.Add(distText);
            }

            DockPanel.SetDock(rightPanel, Dock.Right);
            contentDock.Children.Add(rightPanel);

            StackPanel centerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            string displayTitle = mobName;
            if (_optShowPetAndMob && !string.Equals(mobName, petName, StringComparison.OrdinalIgnoreCase))
            {
                displayTitle = mobName + "  (" + petName + ")";
            }

            TextBlock titleBlock = new TextBlock
            {
                Text = displayTitle,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            centerStack.Children.Add(titleBlock);

            WrapPanel metaRow = new WrapPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };

            if (_optShowDiamonds)
            {
                for (int d = 1; d <= 3; d++)
                {
                    bool reached = level >= d;
                    metaRow.Children.Add(new TextBlock
                    {
                        Text = "◆",
                        Foreground = reached
                            ? (isLv3 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(56, 189, 248)))
                            : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                        FontSize = 11,
                        Margin = new Thickness(0, 0, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                string stepStr = isLv3 ? "MAX" : (soulsInStep + "/" + nextStepNeed);
                metaRow.Children.Add(new TextBlock
                {
                    Text = " " + stepStr,
                    Foreground = isLv3
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (_optShowRemaining3)
            {
                string remLabel = isLv3
                    ? Tr("נשאר: 0 ✔", "Left: 0 ✔", "Осталось: 0 ✔")
                    : string.Format(Tr("חסר ל-3: {0}", "To Lv.3: {0}", "До Ур.3: {0}"), remTo3);

                metaRow.Children.Add(new TextBlock
                {
                    Text = remLabel,
                    Foreground = isLv3
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            centerStack.Children.Add(metaRow);
            contentDock.Children.Add(centerStack);

            cardGrid.Children.Add(contentDock);
            cardBorder.Child = cardGrid;
            return cardBorder;
        }

        // ====================================================================
        // Upgraded Interactive Zone & Spawns Map (Auto-Fit, Pan/Zoom, Labels!)
        // ====================================================================
        private void RenderMiniMap()
        {
            if (!_modZoneMap || _latestData == null || _miniMapCanvas == null) return;
            _miniMapCanvas.Children.Clear();

            double w = _miniMapCanvas.ActualWidth;
            double h = _miniMapCanvas.ActualHeight;
            if (w < 30 || h < 30) return;

            var mm = _latestData.ContainsKey("miniMap") ? _latestData["miniMap"] as Dictionary<string, object> : null;
            if (mm == null) return;

            var playerArr = mm.ContainsKey("player") ? mm["player"] as ArrayList : null;
            var allAreasArr = mm.ContainsKey("allAreas") ? mm["allAreas"] as ArrayList : null;
            var clustersArr = mm.ContainsKey("clusters") ? mm["clusters"] as ArrayList : null;
            var spawnsArr = mm.ContainsKey("spawns") ? mm["spawns"] as ArrayList : null;
            var liveArr = mm.ContainsKey("live") ? mm["live"] as ArrayList : null;

            bool hasPlayer = playerArr != null && playerArr.Count >= 2 && playerArr[0] != null && playerArr[1] != null;
            double baseCenterX = 0, baseCenterY = 0;
            bool centerFound = false;

            // If user clicked a Pet Card, center on that pet's cluster!
            if (_selectedPetId > 0 && clustersArr != null)
            {
                foreach (object cObj in clustersArr)
                {
                    var cd = cObj as Dictionary<string, object>;
                    if (cd != null && GetInt(cd, "petId", 0) == _selectedPetId)
                    {
                        baseCenterX = GetDouble(cd, "x", 0);
                        baseCenterY = GetDouble(cd, "y", 0);
                        centerFound = true;
                        break;
                    }
                }
            }

            // Otherwise center on current Area or Player
            if (!centerFound && allAreasArr != null)
            {
                foreach (object aObj in allAreasArr)
                {
                    var ad = aObj as Dictionary<string, object>;
                    if (ad != null && GetBool(ad, "isCurrent", false))
                    {
                        baseCenterX = GetDouble(ad, "cx", 0);
                        baseCenterY = GetDouble(ad, "cy", 0);
                        centerFound = true;
                        break;
                    }
                }
            }

            if (!centerFound && hasPlayer)
            {
                baseCenterX = Convert.ToDouble(playerArr[0]);
                baseCenterY = Convert.ToDouble(playerArr[1]);
                centerFound = true;
            }

            if (!centerFound && clustersArr != null && clustersArr.Count > 0)
            {
                var first = clustersArr[0] as Dictionary<string, object>;
                if (first != null)
                {
                    baseCenterX = GetDouble(first, "x", 0);
                    baseCenterY = GetDouble(first, "y", 0);
                }
            }

            double centerX = baseCenterX + _mapPanX;
            double centerY = baseCenterY + _mapPanY;
            double scale = (Math.Min(w, h) * 0.42) / (32000.0 / _mapZoom);

            Func<double, double, Point> toScreen = (wx, wy) =>
                new Point(w / 2.0 + (wx - centerX) * scale, h / 2.0 + (wy - centerY) * scale);

            // 1. Draw All Area Polygons (Current Area highlighted in Cyan, Neighbors in subtle slate)
            if (allAreasArr != null)
            {
                foreach (object aObj in allAreasArr)
                {
                    var ad = aObj as Dictionary<string, object>;
                    if (ad == null) continue;
                    bool isCur = GetBool(ad, "isCurrent", false);
                    var polys = ad.ContainsKey("polygons") ? ad["polygons"] as ArrayList : null;
                    if (polys != null)
                    {
                        foreach (object polyObj in polys)
                        {
                            var pts = polyObj as ArrayList;
                            if (pts == null || pts.Count < 3) continue;
                            Polygon poly = new Polygon
                            {
                                Stroke = isCur
                                    ? new SolidColorBrush(Color.FromArgb(190, 56, 189, 248))
                                    : new SolidColorBrush(Color.FromArgb(55, 148, 163, 184)),
                                StrokeThickness = isCur ? 1.8 : 0.8,
                                Fill = isCur
                                    ? new SolidColorBrush(Color.FromArgb(32, 56, 189, 248))
                                    : new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
                            };
                            foreach (object ptObj in pts)
                            {
                                var pair = ptObj as ArrayList;
                                if (pair != null && pair.Count >= 2)
                                    poly.Points.Add(toScreen(Convert.ToDouble(pair[0]), Convert.ToDouble(pair[1])));
                            }
                            _miniMapCanvas.Children.Add(poly);
                        }
                    }

                    // Area Name Label
                    Point ac = toScreen(GetDouble(ad, "cx", 0), GetDouble(ad, "cy", 0));
                    if (ac.X > 10 && ac.X < w - 40 && ac.Y > 10 && ac.Y < h - 15)
                    {
                        TextBlock areaLbl = new TextBlock
                        {
                            Text = GetStr(ad, "name", ""),
                            Foreground = isCur
                                ? new SolidColorBrush(Color.FromArgb(210, 125, 211, 252))
                                : new SolidColorBrush(Color.FromArgb(110, 148, 163, 184)),
                            FontSize = isCur ? 10.5 : 9,
                            FontWeight = isCur ? FontWeights.Bold : FontWeights.Normal,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(areaLbl, ac.X - 30);
                        Canvas.SetTop(areaLbl, ac.Y - 7);
                        _miniMapCanvas.Children.Add(areaLbl);
                    }
                }
            }

            // 2. Draw Individual Spawn Points in Current Area
            if (spawnsArr != null)
            {
                foreach (object spObj in spawnsArr)
                {
                    var sp = spObj as Dictionary<string, object>;
                    if (sp == null) continue;
                    Point p = toScreen(GetDouble(sp, "x", 0), GetDouble(sp, "y", 0));
                    if (p.X < 0 || p.X > w || p.Y < 0 || p.Y > h) continue;

                    int spPetId = GetInt(sp, "petId", 0);
                    bool isSel = (spPetId == _selectedPetId);
                    bool isLv3 = GetBool(sp, "isLevel3", false);

                    Ellipse dot = new Ellipse
                    {
                        Width = isSel ? 8 : 5,
                        Height = isSel ? 8 : 5,
                        Fill = isSel
                            ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                            : (isLv3
                                ? new SolidColorBrush(Color.FromArgb(110, 52, 211, 153))
                                : new SolidColorBrush(Color.FromArgb(185, 56, 189, 248)))
                    };
                    Canvas.SetLeft(dot, p.X - dot.Width / 2);
                    Canvas.SetTop(dot, p.Y - dot.Height / 2);
                    _miniMapCanvas.Children.Add(dot);
                }
            }

            // 3. Draw Labeled Pet Spawn Clusters
            if (clustersArr != null)
            {
                foreach (object cObj in clustersArr)
                {
                    var cd = cObj as Dictionary<string, object>;
                    if (cd == null) continue;
                    bool isLv3 = GetBool(cd, "isLevel3", false);
                    if (_optHideLv3 && isLv3) continue;

                    int cPetId = GetInt(cd, "petId", 0);
                    bool inCurArea = GetBool(cd, "inCurrentArea", false);
                    int aroundCnt = GetInt(cd, "aroundCount", 0);
                    bool isSel = (cPetId == _selectedPetId);

                    if (_currentTab == "around" && aroundCnt <= 0 && !isSel) continue;
                    if (_currentTab == "area" && !inCurArea && aroundCnt <= 0 && !isSel) continue;

                    Point cp = toScreen(GetDouble(cd, "x", 0), GetDouble(cd, "y", 0));
                    if (cp.X < -20 || cp.X > w + 20 || cp.Y < -10 || cp.Y > h + 10) continue;

                    string mobName = GetStr(cd, "monsterName", "");
                    int cnt = GetInt(cd, "count", 0);

                    Border tagBorder = new Border
                    {
                        Background = isSel
                            ? new SolidColorBrush(Color.FromArgb(230, 180, 83, 9))
                            : (aroundCnt > 0
                                ? new SolidColorBrush(Color.FromArgb(220, 6, 95, 70))
                                : new SolidColorBrush(Color.FromArgb(200, 15, 23, 42))),
                        BorderBrush = isSel
                            ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                            : (aroundCnt > 0
                                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                                : new SolidColorBrush(Color.FromArgb(140, 56, 189, 248))),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(4, 1, 4, 1),
                        IsHitTestVisible = false
                    };
                    tagBorder.Child = new TextBlock
                    {
                        Text = mobName + " ×" + cnt,
                        Foreground = Brushes.White,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold
                    };

                    Canvas.SetLeft(tagBorder, cp.X - 28);
                    Canvas.SetTop(tagBorder, cp.Y - 8);
                    _miniMapCanvas.Children.Add(tagBorder);
                }
            }

            // 4. Draw Live Nearby Monsters
            if (liveArr != null)
            {
                foreach (object lObj in liveArr)
                {
                    var lv = lObj as Dictionary<string, object>;
                    if (lv == null) continue;
                    if (!GetBool(lv, "isPet", false)) continue;
                    Point p = toScreen(GetDouble(lv, "x", 0), GetDouble(lv, "y", 0));
                    if (p.X < 0 || p.X > w || p.Y < 0 || p.Y > h) continue;

                    Ellipse dot = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                        Stroke = Brushes.White,
                        StrokeThickness = 1.2
                    };
                    Canvas.SetLeft(dot, p.X - 4);
                    Canvas.SetTop(dot, p.Y - 4);
                    _miniMapCanvas.Children.Add(dot);
                }
            }

            // 5. Draw Player Marker
            if (hasPlayer)
            {
                Point pp = toScreen(Convert.ToDouble(playerArr[0]), Convert.ToDouble(playerArr[1]));
                if (pp.X >= -10 && pp.X <= w + 10 && pp.Y >= -10 && pp.Y <= h + 10)
                {
                    Ellipse playerDot = new Ellipse
                    {
                        Width = 11,
                        Height = 11,
                        Fill = Brushes.White,
                        Stroke = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                        StrokeThickness = 2.5
                    };
                    Canvas.SetLeft(playerDot, pp.X - 5.5);
                    Canvas.SetTop(playerDot, pp.Y - 5.5);
                    _miniMapCanvas.Children.Add(playerDot);
                }
            }

            string areaName = GetStr(mm, "areaName", "");
            _miniMapCaption.Text = (string.IsNullOrEmpty(areaName) ? "" : areaName + " · ")
                + Tr("גרור להזזה · גלגלת לזום · לחץ על חיה למיקוד", "Drag to pan · Wheel to zoom · Click pet to focus", "Перетащите · Колесо — зум · Клик по питомцу");
        }

        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.Run(new OverlayWindow());
        }
    }
}
