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
    public class CraftRecipeMat
    {
        public string ItemId { get; set; }
        public int Count { get; set; }
        public string Name { get; set; }
    }

    public class CraftItem
    {
        public string Id { get; set; }
        public string NameHeb { get; set; }
        public string NameEng { get; set; }
        public string Category { get; set; }
        public string Emoji { get; set; }
        public int GatherLevel { get; set; }
        public string BestMap { get; set; }
        public string BestArea { get; set; }
        public bool IsRecipe { get; set; }
        public List<CraftRecipeMat> RecipeMats { get; set; }

        public CraftItem()
        {
            RecipeMats = new List<CraftRecipeMat>();
        }
    }

    public class CraftWishlistItem
    {
        public string ItemId { get; set; }
        public int TargetCount { get; set; }
    }

    public class PetRowComparer : IComparer<Dictionary<string, object>>, IComparer
    {
        public int Compare(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null || b == null) return 0;

            int arA = a.ContainsKey("aroundCount") ? Convert.ToInt32(a["aroundCount"]) : 0;
            int arB = b.ContainsKey("aroundCount") ? Convert.ToInt32(b["aroundCount"]) : 0;
            if ((arB > 0) != (arA > 0)) return (arB > 0) ? 1 : -1;
            if (arB != arA) return arB - arA;

            int spAreaA = a.ContainsKey("spawnsInCurrentArea") ? Convert.ToInt32(a["spawnsInCurrentArea"]) : 0;
            int spAreaB = b.ContainsKey("spawnsInCurrentArea") ? Convert.ToInt32(b["spawnsInCurrentArea"]) : 0;
            if ((spAreaB > 0) != (spAreaA > 0)) return (spAreaB > 0) ? 1 : -1;

            int spMapA = a.ContainsKey("spawnsInCurrentMap") ? Convert.ToInt32(a["spawnsInCurrentMap"]) : 0;
            int spMapB = b.ContainsKey("spawnsInCurrentMap") ? Convert.ToInt32(b["spawnsInCurrentMap"]) : 0;
            if ((spMapB > 0) != (spMapA > 0)) return (spMapB > 0) ? 1 : -1;

            double distA = a.ContainsKey("nearestDistanceM") && a["nearestDistanceM"] != null ? Convert.ToDouble(a["nearestDistanceM"]) : 999999.0;
            double distB = b.ContainsKey("nearestDistanceM") && b["nearestDistanceM"] != null ? Convert.ToDouble(b["nearestDistanceM"]) : 999999.0;
            if (Math.Abs(distA - distB) > 15.0) return distA < distB ? -1 : 1;

            bool lv3A = a.ContainsKey("isLevel3") && Convert.ToBoolean(a["isLevel3"]);
            bool lv3B = b.ContainsKey("isLevel3") && Convert.ToBoolean(b["isLevel3"]);
            if (lv3A != lv3B) return lv3A ? 1 : -1;

            int remA = a.ContainsKey("remainingToLevel3") ? Convert.ToInt32(a["remainingToLevel3"]) : 0;
            int remB = b.ContainsKey("remainingToLevel3") ? Convert.ToInt32(b["remainingToLevel3"]) : 0;
            return remA - remB;
        }

        public int Compare(object x, object y)
        {
            return Compare(x as Dictionary<string, object>, y as Dictionary<string, object>);
        }
    }

    public class OverlayWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int HOTKEY_CLICKTHROUGH = 9001;
        private const int HOTKEY_TOGGLEVIS = 9002;
        private const int VK_F9 = 0x78;
        private const int VK_F10 = 0x79;

        private static readonly int[] STEPS = new int[] { 5, 25, 75 };
        private const int MAX_LEVEL = 3;
        private const int TOTAL_SOULS_LV3 = 105;

        private bool _isClickThrough = false;
        private string _lang = "HEB"; // "HEB", "ENG", "RUS"
        private string _currentTab = "area";
        private string _activeView = "pets"; // "pets" or "craft"
        private bool _showHubPanel = false;
        private bool _showOptionsPanel = true;
        private bool _updatingLangChecks = false;

        // Active Overlay Modules
        private bool _modPetsTracker = true;
        private bool _modZoneMap = false;
        private bool _modCraftingTracker = true;

        // Display flags for Pets Tracker
        private bool _optShowIcons = true;
        private bool _optShowDistance = true;
        private bool _optShowAroundPin = true;
        private bool _optShowDiamonds = true;
        private bool _optShowRemaining3 = true;
        private bool _optShowPetAndMob = true;
        private bool _optHideLv3 = false;

        private string _baseDir;
        private string _iconsDir;
        private string _petsDbPath;
        private string _stateFilePath;
        private string _overrideFilePath;
        private string _settingsFilePath;
        private string _wishlistFilePath;
        private string _questlogDir;
        private string _qlTrackerPath;

        private Process _snifferProc;
        private DispatcherTimer _timer;
        private DateTime _lastStateWrite = DateTime.MinValue;
        private DateTime _lastQlTrackerWrite = DateTime.MinValue;
        private Dictionary<string, object> _latestData;
        private Dictionary<string, object> _petsDb;
        private bool _updatingMapCombo = false;

        // Crafting Catalog & Wishlist
        private readonly List<CraftItem> _craftCatalog = new List<CraftItem>();
        private readonly List<CraftWishlistItem> _craftWishlist = new List<CraftWishlistItem>();
        private string _craftCategoryFilter = "all";

        // Interactive Map state
        private double _mapZoom = 1.0;
        private double _mapPanX = 0.0;
        private double _mapPanY = 0.0;
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

        // Hub Panel
        private Border _hubPanel;
        private TextBlock _hubTitleText;
        private CheckBox _cbModPets;
        private CheckBox _cbModMap;
        private CheckBox _cbModCrafting;
        private CheckBox _cbModCombatFuture;

        // Language Checkboxes
        private TextBlock _langLabelText;
        private CheckBox _cbLangHeb;
        private CheckBox _cbLangEng;
        private CheckBox _cbLangRus;

        // Options Panel
        private Border _optionsPanel;
        private CheckBox _cbIcons;
        private CheckBox _cbDistance;
        private CheckBox _cbMiniMap;
        private CheckBox _cbAroundPin;
        private CheckBox _cbDiamonds;
        private CheckBox _cbRemaining3;
        private CheckBox _cbPetAndMob;
        private CheckBox _cbHideLv3;

        // Top View Switcher Buttons
        private Button _viewPetsBtn;
        private Button _viewCraftBtn;

        // Pets Tracker Container
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

        // Crafting Tracker Container
        private Grid _craftingContainer;
        private TextBlock _craftSummaryText;
        private StackPanel _craftCardsStack;
        private TextBox _craftSearchBox;
        private Border _craftAddModal;
        private StackPanel _craftCatalogStack;

        public OverlayWindow()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _iconsDir = System.IO.Path.Combine(_baseDir, "data", "icons");
            _petsDbPath = System.IO.Path.Combine(_baseDir, "data", "pets-db.json");
            _stateFilePath = System.IO.Path.Combine(_baseDir, "data", "overlay-state.json");
            _overrideFilePath = System.IO.Path.Combine(_baseDir, "data", "map-override.json");
            _settingsFilePath = System.IO.Path.Combine(_baseDir, "data", "ui-settings.json");
            _wishlistFilePath = System.IO.Path.Combine(_baseDir, "data", "crafting-wishlist.json");

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _questlogDir = System.IO.Path.Combine(appData, "Questlog", "aion-2");
            _qlTrackerPath = System.IO.Path.Combine(_questlogDir, "map-tracker.json");

            try { if (!Directory.Exists(_iconsDir)) Directory.CreateDirectory(_iconsDir); } catch { }

            Title = "Aion 2 Overlay Hub";
            Width = 460;
            Height = 620;
            MinWidth = 320;
            MinHeight = 240;
            Left = 24;
            Top = 48;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(7),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            };
            WindowChrome.SetWindowChrome(this, chrome);

            InitCraftCatalog();
            LoadWishlist();
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

            // Run direct C# engine immediately so data appears in 0 ms!
            BuildDirectStateFromPetsDb();
            RefreshView();
        }

        private string Tr(string heb, string eng, string rus)
        {
            if (_lang == "ENG") return eng;
            if (_lang == "RUS") return rus;
            return heb;
        }

        private void InitCraftCatalog()
        {
            _craftCatalog.Clear();

            // Herbs
            _craftCatalog.Add(new CraftItem { Id = "610610013", NameHeb = "אנג'ליקה (Angelica)", NameEng = "Angelica", Category = "herb", Emoji = "🌿", GatherLevel = 1, BestMap = "Altgard / Verteron", BestArea = "Dredgion Crash Site" });
            _craftCatalog.Add(new CraftItem { Id = "610610014", NameHeb = "לובודה (Lobuda)", NameEng = "Lobuda", Category = "herb", Emoji = "🌿", GatherLevel = 15, BestMap = "Altgard / Verteron", BestArea = "Basfelt Village" });
            _craftCatalog.Add(new CraftItem { Id = "610610015", NameHeb = "קוניום (Conium)", NameEng = "Conium", Category = "herb", Emoji = "🌿", GatherLevel = 35, BestMap = "Altgard / Verteron", BestArea = "Gribade Crater" });
            _craftCatalog.Add(new CraftItem { Id = "610610016", NameHeb = "טאנגה (Tange)", NameEng = "Tange", Category = "herb", Emoji = "🌿", GatherLevel = 65, BestMap = "Morheim / Eltnen", BestArea = "Morheim Snowfield" });

            // Ores
            _craftCatalog.Add(new CraftItem { Id = "610650011", NameHeb = "עפרת ברזל (Iron Ore)", NameEng = "Iron Ore", Category = "ore", Emoji = "⛏️", GatherLevel = 1, BestMap = "Altgard / Verteron", BestArea = "Moslan Forest" });
            _craftCatalog.Add(new CraftItem { Id = "610650012", NameHeb = "עפרת טיטניום (Titani Ore)", NameEng = "Titani Ore", Category = "ore", Emoji = "⛏️", GatherLevel = 25, BestMap = "Altgard / Verteron", BestArea = "Dredgion Crash Site" });
            _craftCatalog.Add(new CraftItem { Id = "610650013", NameHeb = "אדמנטיום (Adamantium Ore)", NameEng = "Adamantium Ore", Category = "ore", Emoji = "⛏️", GatherLevel = 50, BestMap = "Altgard / Morheim", BestArea = "Altgard Ice Lake" });
            _craftCatalog.Add(new CraftItem { Id = "610650014", NameHeb = "עפרת כסף (Silver Ore)", NameEng = "Silver Ore", Category = "ore", Emoji = "⛏️", GatherLevel = 75, BestMap = "Morheim / Eltnen", BestArea = "Salintus Desert" });
            _craftCatalog.Add(new CraftItem { Id = "610650016", NameHeb = "עפרת זהב (Gold Ore)", NameEng = "Gold Ore", Category = "ore", Emoji = "⛏️", GatherLevel = 100, BestMap = "Morheim / Eltnen", BestArea = "Desert Fortress" });
            _craftCatalog.Add(new CraftItem { Id = "610650018", NameHeb = "פלטינה (Platinum Ore)", NameEng = "Platinum Ore", Category = "ore", Emoji = "⛏️", GatherLevel = 150, BestMap = "Beluslan / Heiron", BestArea = "Beluslan Fortress" });

            // Gems
            _craftCatalog.Add(new CraftItem { Id = "610660025", NameHeb = "טופז (Topaz)", NameEng = "Topaz", Category = "gem", Emoji = "💎", GatherLevel = 15, BestMap = "Altgard / Verteron", BestArea = "Dredgion Crash Site" });
            _craftCatalog.Add(new CraftItem { Id = "610660026", NameHeb = "טורקיז (Turquoise)", NameEng = "Turquoise", Category = "gem", Emoji = "💎", GatherLevel = 35, BestMap = "Altgard / Verteron", BestArea = "Gribade Crater" });
            _craftCatalog.Add(new CraftItem { Id = "610660027", NameHeb = "אקוומרין (Aquamarine)", NameEng = "Aquamarine", Category = "gem", Emoji = "💎", GatherLevel = 60, BestMap = "Altgard / Morheim", BestArea = "Altgard Coast" });
            _craftCatalog.Add(new CraftItem { Id = "610660028", NameHeb = "זירקון (Zircon)", NameEng = "Zircon", Category = "gem", Emoji = "💎", GatherLevel = 85, BestMap = "Morheim / Eltnen", BestArea = "Morheim Canyon" });
            _craftCatalog.Add(new CraftItem { Id = "610660029", NameHeb = "גרנט (Garnet)", NameEng = "Garnet", Category = "gem", Emoji = "💎", GatherLevel = 110, BestMap = "Morheim / Eltnen", BestArea = "Red Lava Cliff" });
            _craftCatalog.Add(new CraftItem { Id = "610660030", NameHeb = "אמטיסט (Amethyst)", NameEng = "Amethyst", Category = "gem", Emoji = "💎", GatherLevel = 135, BestMap = "Beluslan / Heiron", BestArea = "Kidorun Campsite" });
            _craftCatalog.Add(new CraftItem { Id = "610660031", NameHeb = "אמרלד / ברקת (Emerald)", NameEng = "Emerald", Category = "gem", Emoji = "💎", GatherLevel = 160, BestMap = "Beluslan / Heiron", BestArea = "Anair Harbor" });
            _craftCatalog.Add(new CraftItem { Id = "610660032", NameHeb = "אודם / רובי (Ruby)", NameEng = "Ruby", Category = "gem", Emoji = "💎", GatherLevel = 185, BestMap = "Beluslan / Heiron", BestArea = "Besfer Refugee Camp" });
            _craftCatalog.Add(new CraftItem { Id = "610660033", NameHeb = "יהלום (Diamond)", NameEng = "Diamond", Category = "gem", Emoji = "💎", GatherLevel = 210, BestMap = "Reshanta (Abyss)", BestArea = "Core / Asteria" });

            // Aether
            _craftCatalog.Add(new CraftItem { Id = "610670041", NameHeb = "חלקיק את'ר (Aether Particle)", NameEng = "Aether Particle", Category = "aether", Emoji = "✨", GatherLevel = 1, BestMap = "Altgard / Verteron", BestArea = "Fortress Sky" });
            _craftCatalog.Add(new CraftItem { Id = "610670043", NameHeb = "קריסטל את'ר קטן (Small Aether)", NameEng = "Small Aether Crystal", Category = "aether", Emoji = "✨", GatherLevel = 25, BestMap = "Altgard / Verteron", BestArea = "Dredgion Sky" });
            _craftCatalog.Add(new CraftItem { Id = "610670044", NameHeb = "קריסטל את'ר (Aether Crystal)", NameEng = "Regular Aether Crystal", Category = "aether", Emoji = "✨", GatherLevel = 50, BestMap = "Altgard / Morheim", BestArea = "Moslan Sky" });
            _craftCatalog.Add(new CraftItem { Id = "610670047", NameHeb = "את'ר טהור (Pure Aether)", NameEng = "Pure Aether", Category = "aether", Emoji = "✨", GatherLevel = 75, BestMap = "Morheim / Eltnen", BestArea = "Morheim Sky" });
            _craftCatalog.Add(new CraftItem { Id = "610670048", NameHeb = "את'ר נוצץ (Brilliant Aether)", NameEng = "Brilliant Aether", Category = "aether", Emoji = "✨", GatherLevel = 100, BestMap = "Morheim / Eltnen", BestArea = "Desert Sky" });

            // Wood
            _craftCatalog.Add(new CraftItem { Id = "610620013", NameHeb = "עץ אסוואט (Asvat Wood)", NameEng = "Asvat Wood", Category = "wood", Emoji = "🪵", GatherLevel = 1, BestMap = "Altgard / Verteron", BestArea = "Moslan Forest" });
            _craftCatalog.Add(new CraftItem { Id = "610620014", NameHeb = "עץ ערבה (Willow Wood)", NameEng = "Willow Wood", Category = "wood", Emoji = "🪵", GatherLevel = 25, BestMap = "Altgard / Verteron", BestArea = "Dredgion Crash Site" });
            _craftCatalog.Add(new CraftItem { Id = "610620015", NameHeb = "עץ דוקו (Duku Wood)", NameEng = "Duku Wood", Category = "wood", Emoji = "🪵", GatherLevel = 55, BestMap = "Morheim / Eltnen", BestArea = "Morheim Valley" });
            _craftCatalog.Add(new CraftItem { Id = "610620016", NameHeb = "עץ טיק (Teak Wood)", NameEng = "Teak Wood", Category = "wood", Emoji = "🪵", GatherLevel = 85, BestMap = "Beluslan / Heiron", BestArea = "Heiron Forest" });

            // Crystals
            _craftCatalog.Add(new CraftItem { Id = "610680045", NameHeb = "קריסטל רוח (Wind Crystal)", NameEng = "Wind Crystal", Category = "crystal", Emoji = "🔮", GatherLevel = 1, BestMap = "All Worlds", BestArea = "Elemental Spawns" });
            _craftCatalog.Add(new CraftItem { Id = "610680047", NameHeb = "קריסטל מים (Water Crystal)", NameEng = "Water Crystal", Category = "crystal", Emoji = "🔮", GatherLevel = 1, BestMap = "All Worlds", BestArea = "Elemental Spawns" });
            _craftCatalog.Add(new CraftItem { Id = "610680049", NameHeb = "קריסטל אדמה (Earth Crystal)", NameEng = "Earth Crystal", Category = "crystal", Emoji = "🔮", GatherLevel = 1, BestMap = "All Worlds", BestArea = "Elemental Spawns" });
            _craftCatalog.Add(new CraftItem { Id = "610680050", NameHeb = "קריסטל אש (Fire Crystal)", NameEng = "Fire Crystal", Category = "crystal", Emoji = "🔮", GatherLevel = 1, BestMap = "All Worlds", BestArea = "Elemental Spawns" });
            _craftCatalog.Add(new CraftItem { Id = "610680051", NameHeb = "אבן אלמנטלית מובחרת", NameEng = "Greater Elemental Stone", Category = "crystal", Emoji = "🔮", GatherLevel = 25, BestMap = "All Worlds", BestArea = "Refine / Gathering" });
            _craftCatalog.Add(new CraftItem { Id = "610680053", NameHeb = "תמצית אלמנט טהורה", NameEng = "Pure Elemental Essence", Category = "crystal", Emoji = "🔮", GatherLevel = 50, BestMap = "Morheim / Eltnen", BestArea = "Refine / Craft" });
            _craftCatalog.Add(new CraftItem { Id = "610680054", NameHeb = "קריסטל כאוס (Chaos Crystal)", NameEng = "Chaos Crystal", Category = "crystal", Emoji = "🔮", GatherLevel = 75, BestMap = "Abyss / Beluslan", BestArea = "Chaos Rifts" });

            // Crafting Recipes
            var pLife = new CraftItem
            {
                Id = "530116001",
                NameHeb = "שיקוי חיים מובחר (Major Life Potion)",
                NameEng = "Major Life Potion",
                Category = "potion",
                Emoji = "🧪",
                IsRecipe = true,
                BestMap = "Alchemy Table",
                BestArea = "Pandaemonium / Sanctum"
            };
            pLife.RecipeMats.Add(new CraftRecipeMat { ItemId = "610610013", Count = 2, Name = "Angelica" });
            pLife.RecipeMats.Add(new CraftRecipeMat { ItemId = "610650012", Count = 1, Name = "Titani Ore" });
            pLife.RecipeMats.Add(new CraftRecipeMat { ItemId = "610680047", Count = 1, Name = "Water Crystal" });
            _craftCatalog.Add(pLife);

            var pMana = new CraftItem
            {
                Id = "530116005",
                NameHeb = "שיקוי מאנה מובחר (Major Mana Potion)",
                NameEng = "Major Mana Potion",
                Category = "potion",
                Emoji = "🧪",
                IsRecipe = true,
                BestMap = "Alchemy Table",
                BestArea = "Pandaemonium / Sanctum"
            };
            pMana.RecipeMats.Add(new CraftRecipeMat { ItemId = "610610014", Count = 2, Name = "Lobuda" });
            pMana.RecipeMats.Add(new CraftRecipeMat { ItemId = "610650011", Count = 1, Name = "Iron Ore" });
            pMana.RecipeMats.Add(new CraftRecipeMat { ItemId = "610680045", Count = 1, Name = "Wind Crystal" });
            _craftCatalog.Add(pMana);

            var pSerum = new CraftItem
            {
                Id = "530116006",
                NameHeb = "סרום התאוששות כפול (Major Recovery Serum)",
                NameEng = "Major Recovery Serum",
                Category = "potion",
                Emoji = "🧪",
                IsRecipe = true,
                BestMap = "Alchemy Table",
                BestArea = "Pandaemonium / Sanctum"
            };
            pSerum.RecipeMats.Add(new CraftRecipeMat { ItemId = "610610015", Count = 2, Name = "Conium" });
            pSerum.RecipeMats.Add(new CraftRecipeMat { ItemId = "610660025", Count = 1, Name = "Topaz" });
            pSerum.RecipeMats.Add(new CraftRecipeMat { ItemId = "610680050", Count = 1, Name = "Fire Crystal" });
            _craftCatalog.Add(pSerum);

            var pGreatLife = new CraftItem
            {
                Id = "530126003",
                NameHeb = "שיקוי חיים עליון (Greater Life Potion)",
                NameEng = "Greater Life Potion",
                Category = "potion",
                Emoji = "🧪",
                IsRecipe = true,
                BestMap = "Alchemy Table",
                BestArea = "Pandaemonium / Sanctum"
            };
            pGreatLife.RecipeMats.Add(new CraftRecipeMat { ItemId = "610610016", Count = 3, Name = "Tange" });
            pGreatLife.RecipeMats.Add(new CraftRecipeMat { ItemId = "610660027", Count = 1, Name = "Aquamarine" });
            pGreatLife.RecipeMats.Add(new CraftRecipeMat { ItemId = "610680047", Count = 2, Name = "Water Crystal" });
            _craftCatalog.Add(pGreatLife);

            var pGreatSerum = new CraftItem
            {
                Id = "530126005",
                NameHeb = "סרום התאוששות עליון (Greater Recovery Serum)",
                NameEng = "Greater Recovery Serum",
                Category = "potion",
                Emoji = "🧪",
                IsRecipe = true,
                BestMap = "Alchemy Table",
                BestArea = "Pandaemonium / Sanctum"
            };
            pGreatSerum.RecipeMats.Add(new CraftRecipeMat { ItemId = "610610016", Count = 4, Name = "Tange" });
            pGreatSerum.RecipeMats.Add(new CraftRecipeMat { ItemId = "610650013", Count = 2, Name = "Adamantium" });
            pGreatSerum.RecipeMats.Add(new CraftRecipeMat { ItemId = "610670047", Count = 1, Name = "Pure Aether" });
            _craftCatalog.Add(pGreatSerum);
        }

        private void LoadWishlist()
        {
            _craftWishlist.Clear();
            try
            {
                if (File.Exists(_wishlistFilePath))
                {
                    string json = File.ReadAllText(_wishlistFilePath, Encoding.UTF8);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    var arr = ser.Deserialize<ArrayList>(json);
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            var d = o as Dictionary<string, object>;
                            if (d != null)
                            {
                                string itm = GetStr(d, "ItemId", "");
                                int tgt = GetInt(d, "TargetCount", 50);
                                if (!string.IsNullOrEmpty(itm))
                                    _craftWishlist.Add(new CraftWishlistItem { ItemId = itm, TargetCount = tgt });
                            }
                        }
                    }
                }
            }
            catch { }

            // Default wishlist if empty
            if (_craftWishlist.Count == 0)
            {
                _craftWishlist.Add(new CraftWishlistItem { ItemId = "610610013", TargetCount = 100 }); // Angelica
                _craftWishlist.Add(new CraftWishlistItem { ItemId = "610650012", TargetCount = 100 }); // Titani Ore
                _craftWishlist.Add(new CraftWishlistItem { ItemId = "610660025", TargetCount = 50 });  // Topaz
                _craftWishlist.Add(new CraftWishlistItem { ItemId = "610670043", TargetCount = 50 });  // Small Aether
                _craftWishlist.Add(new CraftWishlistItem { ItemId = "530116001", TargetCount = 10 });  // Major Life Potion
                SaveWishlist();
            }
        }

        private void SaveWishlist()
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(_craftWishlist);
                File.WriteAllText(_wishlistFilePath, json, Encoding.UTF8);
            }
            catch { }
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
                if (d.ContainsKey("activeView") && d["activeView"] != null) _activeView = d["activeView"].ToString();
                if (d.ContainsKey("showHub")) _showHubPanel = GetBool(d, "showHub", false);
                if (d.ContainsKey("showOptions")) _showOptionsPanel = GetBool(d, "showOptions", true);
                if (d.ContainsKey("modPets")) _modPetsTracker = GetBool(d, "modPets", true);
                if (d.ContainsKey("modMap")) _modZoneMap = GetBool(d, "modMap", false);
                if (d.ContainsKey("modCrafting")) _modCraftingTracker = GetBool(d, "modCrafting", true);
                if (d.ContainsKey("icons")) _optShowIcons = GetBool(d, "icons", true);
                if (d.ContainsKey("distance")) _optShowDistance = GetBool(d, "distance", true);
                if (d.ContainsKey("aroundPin")) _optShowAroundPin = GetBool(d, "aroundPin", true);
                if (d.ContainsKey("diamonds")) _optShowDiamonds = GetBool(d, "diamonds", true);
                if (d.ContainsKey("remaining3")) _optShowRemaining3 = GetBool(d, "remaining3", true);
                if (d.ContainsKey("petAndMob")) _optShowPetAndMob = GetBool(d, "petAndMob", true);
                if (d.ContainsKey("hideLv3")) _optHideLv3 = GetBool(d, "hideLv3", false);
                if (d.ContainsKey("width"))
                {
                    double w = GetDouble(d, "width", 460);
                    if (w >= 320 && w <= 1600) Width = w;
                }
                if (d.ContainsKey("height"))
                {
                    double h = GetDouble(d, "height", 620);
                    if (h >= 240 && h <= 1400) Height = h;
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
                    { "activeView", _activeView },
                    { "showHub", _showHubPanel },
                    { "showOptions", _showOptionsPanel },
                    { "modPets", _modPetsTracker },
                    { "modMap", _modZoneMap },
                    { "modCrafting", _modCraftingTracker },
                    { "icons", _optShowIcons },
                    { "distance", _optShowDistance },
                    { "aroundPin", _optShowAroundPin },
                    { "diamonds", _optShowDiamonds },
                    { "remaining3", _optShowRemaining3 },
                    { "petAndMob", _optShowPetAndMob },
                    { "hideLv3", _optHideLv3 },
                    { "width", ActualWidth },
                    { "height", ActualHeight }
                };
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(_settingsFilePath, ser.Serialize(d), Encoding.UTF8);
            }
            catch { }
        }

        private void SetLanguage(string newLang)
        {
            if (_lang == newLang && !_updatingLangChecks) return;
            _lang = newLang;
            _updatingLangChecks = true;
            try
            {
                _cbLangHeb.IsChecked = (_lang == "HEB");
                _cbLangEng.IsChecked = (_lang == "ENG");
                _cbLangRus.IsChecked = (_lang == "RUS");
                FlowDirection = (_lang == "HEB") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                ApplyLanguageToStaticUI();
                RefreshView();
            }
            finally
            {
                _updatingLangChecks = false;
            }
            SaveSettings();
        }

        private void ApplyLanguageToStaticUI()
        {
            _hubBtn.Content = Tr("🧩 תוספים", "🧩 Overlays", "🧩 Модули");
            _hubTitleText.Text = Tr("בחר אילו תוספים להפעיל:", "Select Overlays to display:", "Выберите оверлеи:");
            _cbModPets.Content = Tr("🐾 מעקב חיות מחמד (Pets Tracker)", "🐾 Pets Tracker", "🐾 Оверлей питомцев");
            _cbModMap.Content = Tr("🗺 מפת אזור אינטראקטיבית (Zone Map)", "🗺 Interactive Zone Map", "🗺 Интерактивная карта");
            _cbModCrafting.Content = Tr("⚒️ מעקב קראפטינג ואיסוף (Crafting & Essence Tracker)", "⚒️ Crafting & Essence Tracker", "⚒️ Крафт и сбор ресурсов");
            _cbModCombatFuture.Content = Tr("⚔ מעקב נזק DPS (בקרוב)", "⚔ DPS / Combat Meter (Soon)", "⚔ ДПС-метр (Скоро)");

            _langLabelText.Text = Tr("🌐 שפה:", "🌐 Language:", "🌐 Язык:");

            _cbIcons.Content = Tr("🖼 תמונות חיות", "🖼 Pet Icons", "🖼 Иконки");
            _cbDistance.Content = Tr("🧭 מרחק וכיוון (m)", "🧭 Distance & Arrow", "🧭 Дистанция (m)");
            _cbMiniMap.Content = Tr("🗺 הצג מפה", "🗺 Show Map", "🗺 Показать карту");
            _cbAroundPin.Content = Tr("📍 כמות סביבי", "📍 Spawn/Around Pin", "📍 Кол-во рядом");
            _cbDiamonds.Content = Tr("💎 יהלומי שלב (◆◆◆)", "💎 Level Diamonds", "💎 Уровень (◆◆◆)");
            _cbRemaining3.Content = Tr("🎯 כמה חסר לשלב 3", "🎯 Missing to Lv.3", "🎯 Осталось до Ур.3");
            _cbPetAndMob.Content = Tr("🐾 שם מפלצת + חיה", "🐾 Monster + Pet Name", "🐾 Монстр + Питомец");
            _cbHideLv3.Content = Tr("👁 הסתר שלב 3 (נשאר 0)", "👁 Hide Lv.3 (0 left)", "👁 Скрыть Ур.3 (0)");

            _viewPetsBtn.Content = Tr("🐾 חיות (Pets)", "🐾 Pets", "🐾 Питомцы");
            _viewCraftBtn.Content = Tr("⚒️ איסוף וקראפטינג (Crafting)", "⚒️ Crafting", "⚒️ Сбор и крафт");
        }

        private void StartSnifferBackend()
        {
            try
            {
                string snifferScript = System.IO.Path.Combine(_baseDir, "sniffer.cjs");
                if (!File.Exists(snifferScript)) return;

                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                string[] candidates = new string[]
                {
                    "node.exe",
                    System.IO.Path.Combine(progFiles, "nodejs", "node.exe"),
                    System.IO.Path.Combine(localApp, @"Programs\antigravity\Antigravity.exe"),
                    System.IO.Path.Combine(localApp, @"Programs\Microsoft VS Code\Code.exe"),
                    System.IO.Path.Combine(localApp, @"Programs\cursor\Cursor.exe")
                };

                string chosenRunner = null;
                bool isElectron = false;
                foreach (string c in candidates)
                {
                    if (c == "node.exe")
                    {
                        try
                        {
                            ProcessStartInfo psiCheck = new ProcessStartInfo("node.exe", "-v")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using (Process p = Process.Start(psiCheck))
                            {
                                p.WaitForExit(1000);
                                if (p.ExitCode == 0) { chosenRunner = "node.exe"; break; }
                            }
                        }
                        catch { }
                    }
                    else if (File.Exists(c))
                    {
                        chosenRunner = c;
                        if (c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !c.EndsWith("node.exe", StringComparison.OrdinalIgnoreCase))
                            isElectron = true;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(chosenRunner)) return;

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = chosenRunner,
                    Arguments = "\"" + snifferScript + "\"",
                    WorkingDirectory = _baseDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (isElectron)
                {
                    psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
                }
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
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Top Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: App Hub Panel
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Options Panel
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: View Switcher (Pets vs Craft)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4: Overlays Body
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Resize bar

            // 0. Top Header Bar
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
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerDock.Children.Add(_headerTitleText);

            headerBar.Child = headerDock;
            Grid.SetRow(headerBar, 0);
            mainGrid.Children.Add(headerBar);

            // 1. Overlay App Hub Panel
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
                UpdateActiveViewContainers();
                SaveSettings();
            });
            _cbModPets.FontWeight = FontWeights.Bold;
            hubStack.Children.Add(_cbModPets);

            _cbModCrafting = CreateOptionCheck(_modCraftingTracker, v =>
            {
                _modCraftingTracker = v;
                UpdateActiveViewContainers();
                SaveSettings();
            });
            _cbModCrafting.FontWeight = FontWeights.Bold;
            hubStack.Children.Add(_cbModCrafting);

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

            _hubPanel.Child = hubStack;
            Grid.SetRow(_hubPanel, 1);
            mainGrid.Children.Add(_hubPanel);

            // 2. Options & Language Checkboxes Panel
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

            DockPanel filterRow = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            _mapCombo = new ComboBox
            {
                Width = 140,
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

            // 3. View Switcher Tabs (🐾 חיות | ⚒️ קראפטינג)
            Border viewSwitcherBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(120, 10, 16, 28)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 6)
            };
            UniformGrid viewSwitcherGrid = new UniformGrid { Columns = 2 };

            _viewPetsBtn = new Button
            {
                Content = "🐾 חיות (Pets)",
                Height = 26,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            _viewPetsBtn.Click += (s, e) => { _activeView = "pets"; UpdateActiveViewContainers(); SaveSettings(); };

            _viewCraftBtn = new Button
            {
                Content = "⚒️ איסוף וקראפטינג (Crafting)",
                Height = 26,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            _viewCraftBtn.Click += (s, e) => { _activeView = "craft"; UpdateActiveViewContainers(); SaveSettings(); };

            viewSwitcherGrid.Children.Add(_viewPetsBtn);
            viewSwitcherGrid.Children.Add(_viewCraftBtn);
            viewSwitcherBorder.Child = viewSwitcherGrid;

            Grid.SetRow(viewSwitcherBorder, 3);
            mainGrid.Children.Add(viewSwitcherBorder);

            // 4. Overlays Body Container
            Grid bodyGrid = new Grid();

            // 4.1 Pets Tracker Container
            _petsTrackerContainer = new Grid();
            _petsTrackerContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Summary + Scope Tabs
            _petsTrackerContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: MiniMap
            _petsTrackerContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Cards List

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

            // MiniMap
            _miniMapBorder = new Border
            {
                Height = 150,
                Background = new SolidColorBrush(Color.FromArgb(235, 10, 14, 22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(85, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 6),
                ClipToBounds = true,
                Visibility = _modZoneMap ? Visibility.Visible : Visibility.Collapsed
            };

            Grid mapGrid = new Grid();
            _miniMapCanvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
            _miniMapCanvas.MouseLeftButtonDown += (s, e) =>
            {
                _isMapDragging = true;
                _mapDragStart = e.GetPosition(_miniMapCanvas);
                _miniMapCanvas.CaptureMouse();
            };
            _miniMapCanvas.MouseMove += (s, e) =>
            {
                if (_isMapDragging)
                {
                    Point cur = e.GetPosition(_miniMapCanvas);
                    double dx = cur.X - _mapDragStart.X;
                    double dy = cur.Y - _mapDragStart.Y;
                    _mapPanX -= (dx * 16.0) / _mapZoom;
                    _mapPanY += (dy * 16.0) / _mapZoom;
                    _mapDragStart = cur;
                    RenderMiniMap();
                }
            };
            _miniMapCanvas.MouseLeftButtonUp += (s, e) =>
            {
                _isMapDragging = false;
                _miniMapCanvas.ReleaseMouseCapture();
            };
            _miniMapCanvas.MouseWheel += (s, e) =>
            {
                if (e.Delta > 0) _mapZoom = Math.Min(4.0, _mapZoom * 1.25);
                else _mapZoom = Math.Max(0.4, _mapZoom / 1.25);
                RenderMiniMap();
            };
            mapGrid.Children.Add(_miniMapCanvas);

            StackPanel zoomBtns = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6)
            };
            Button btnZoomIn = CreateSmallMapBtn("+", () => { _mapZoom = Math.Min(4.0, _mapZoom * 1.3); RenderMiniMap(); });
            Button btnZoomOut = CreateSmallMapBtn("−", () => { _mapZoom = Math.Max(0.4, _mapZoom / 1.3); RenderMiniMap(); });
            Button btnReset = CreateSmallMapBtn("⟲", () => { _mapZoom = 1.0; _mapPanX = 0; _mapPanY = 0; _selectedPetId = 0; RenderMiniMap(); });
            zoomBtns.Children.Add(btnZoomIn);
            zoomBtns.Children.Add(btnZoomOut);
            zoomBtns.Children.Add(btnReset);
            mapGrid.Children.Add(zoomBtns);

            _miniMapCaption = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(170, 203, 213, 225)),
                FontSize = 9.5,
                Margin = new Thickness(6, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };
            mapGrid.Children.Add(_miniMapCaption);

            _miniMapBorder.Child = mapGrid;
            Grid.SetRow(_miniMapBorder, 1);
            _petsTrackerContainer.Children.Add(_miniMapBorder);

            // Cards Scroll
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _cardsStack = new StackPanel();
            scroll.Content = _cardsStack;
            Grid.SetRow(scroll, 2);
            _petsTrackerContainer.Children.Add(scroll);

            bodyGrid.Children.Add(_petsTrackerContainer);

            // 4.2 Crafting & Essence Extraction Tracker Container
            _craftingContainer = BuildCraftingContainer();
            bodyGrid.Children.Add(_craftingContainer);

            Grid.SetRow(bodyGrid, 4);
            mainGrid.Children.Add(bodyGrid);

            // 5. Bottom Resize / Status bar
            Border botBar = new Border { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel botDock = new DockPanel();

            TextBlock hintText = new TextBlock
            {
                Text = "F9: שקיפות/נעילה · F10: הסתרה/הצגה · גרור פינות לשינוי גודל",
                Foreground = new SolidColorBrush(Color.FromArgb(110, 148, 163, 184)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            botDock.Children.Add(hintText);
            botBar.Child = botDock;

            Grid.SetRow(botBar, 5);
            mainGrid.Children.Add(botBar);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;

            UpdateActiveViewContainers();
        }

        private Grid BuildCraftingContainer()
        {
            Grid g = new Grid { Visibility = Visibility.Collapsed };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header & Action
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Category Chips
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Cards List

            // 0. Top Bar of Crafting
            DockPanel craftTop = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            Button addTargetBtn = new Button
            {
                Content = Tr("+ הוסף חומר / מתכון ליעד", "+ Add Craft / Material Goal", "+ Добавить цель крафта"),
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromArgb(70, 16, 185, 129)),
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            addTargetBtn.Click += (s, e) =>
            {
                _craftAddModal.Visibility = (_craftAddModal.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
                RefreshCraftCatalogModal();
            };
            DockPanel.SetDock(addTargetBtn, Dock.Left);
            craftTop.Children.Add(addTargetBtn);

            _craftSummaryText = new TextBlock
            {
                Text = "...",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            craftTop.Children.Add(_craftSummaryText);
            Grid.SetRow(craftTop, 0);
            g.Children.Add(craftTop);

            // 1. Category Filter Chips
            WrapPanel chipsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            chipsWrap.Children.Add(CreateCraftChip("🌟 הכל", "all"));
            chipsWrap.Children.Add(CreateCraftChip("🌿 צמחים", "herb"));
            chipsWrap.Children.Add(CreateCraftChip("⛏️ עפרות", "ore"));
            chipsWrap.Children.Add(CreateCraftChip("💎 אבני חן", "gem"));
            chipsWrap.Children.Add(CreateCraftChip("✨ את'ר", "aether"));
            chipsWrap.Children.Add(CreateCraftChip("🪵 עץ", "wood"));
            chipsWrap.Children.Add(CreateCraftChip("🔮 קריסטלים", "crystal"));
            chipsWrap.Children.Add(CreateCraftChip("🧪 שיקויים", "potion"));
            Grid.SetRow(chipsWrap, 1);
            g.Children.Add(chipsWrap);

            // 2. Body Grid with Cards + Add Modal
            Grid bodyArea = new Grid();
            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _craftCardsStack = new StackPanel();
            sv.Content = _craftCardsStack;
            bodyArea.Children.Add(sv);

            // Add Target Modal Dialog
            _craftAddModal = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Visibility = Visibility.Collapsed
            };
            DockPanel modalDock = new DockPanel();
            DockPanel modalHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            Button closeModalBtn = CreateIconBtn("✕", () => { _craftAddModal.Visibility = Visibility.Collapsed; });
            closeModalBtn.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            DockPanel.SetDock(closeModalBtn, Dock.Left);
            modalHeader.Children.Add(closeModalBtn);

            TextBlock modalTitle = new TextBlock
            {
                Text = Tr("בחר חומר גלם או מתכון להוספה ליעדים:", "Pick material or recipe to track:", "Выберите ресурс для отслеживания:"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            modalHeader.Children.Add(modalTitle);
            DockPanel.SetDock(modalHeader, Dock.Top);
            modalDock.Children.Add(modalHeader);

            _craftSearchBox = new TextBox
            {
                Height = 22,
                Background = new SolidColorBrush(Color.FromRgb(10, 15, 26)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(0, 0, 0, 6)
            };
            _craftSearchBox.TextChanged += (s, e) => RefreshCraftCatalogModal();
            DockPanel.SetDock(_craftSearchBox, Dock.Top);
            modalDock.Children.Add(_craftSearchBox);

            ScrollViewer catScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _craftCatalogStack = new StackPanel();
            catScroll.Content = _craftCatalogStack;
            modalDock.Children.Add(catScroll);

            _craftAddModal.Child = modalDock;
            bodyArea.Children.Add(_craftAddModal);

            Grid.SetRow(bodyArea, 2);
            g.Children.Add(bodyArea);

            return g;
        }

        private Button CreateCraftChip(string text, string catKey)
        {
            Button btn = new Button
            {
                Content = text,
                Height = 20,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            Action updateVisual = () =>
            {
                bool active = (_craftCategoryFilter == catKey);
                btn.Background = active ? new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                btn.Foreground = active ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
                btn.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : Brushes.Transparent;
            };
            updateVisual();
            btn.Click += (s, e) =>
            {
                _craftCategoryFilter = catKey;
                RefreshCraftingView();
            };
            return btn;
        }

        private void RefreshCraftCatalogModal()
        {
            _craftCatalogStack.Children.Clear();
            string q = (_craftSearchBox.Text ?? "").Trim().ToLowerInvariant();

            foreach (var item in _craftCatalog)
            {
                string match = (item.NameHeb + " " + item.NameEng + " " + item.BestArea + " " + item.BestMap).ToLowerInvariant();
                if (!string.IsNullOrEmpty(q) && !match.Contains(q)) continue;

                Border row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                DockPanel dp = new DockPanel();

                Button pinBtn = new Button
                {
                    Content = Tr("+ עקוב", "+ Track", "+ Отслеживать"),
                    Height = 20,
                    Padding = new Thickness(6, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromArgb(70, 16, 185, 129)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                pinBtn.Click += (s, e) =>
                {
                    var existing = _craftWishlist.Find(w => w.ItemId == item.Id);
                    if (existing != null) existing.TargetCount += 50;
                    else _craftWishlist.Add(new CraftWishlistItem { ItemId = item.Id, TargetCount = item.IsRecipe ? 10 : 50 });
                    SaveWishlist();
                    _craftAddModal.Visibility = Visibility.Collapsed;
                    RefreshCraftingView();
                };
                DockPanel.SetDock(pinBtn, Dock.Left);
                dp.Children.Add(pinBtn);

                StackPanel info = new StackPanel();
                TextBlock nameTb = new TextBlock
                {
                    Text = item.Emoji + " " + (_lang == "ENG" ? item.NameEng : item.NameHeb),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5
                };
                TextBlock locTb = new TextBlock
                {
                    Text = string.Format(Tr("📍 {0} · {1} (דרישה: רמה {2})", "📍 {0} · {1} (Req: Lv.{2})", "📍 {0} · {1} (Ур.{2})"), item.BestMap, item.BestArea, item.GatherLevel),
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 10
                };
                info.Children.Add(nameTb);
                info.Children.Add(locTb);
                dp.Children.Add(info);

                row.Child = dp;
                _craftCatalogStack.Children.Add(row);
            }
        }

        private void UpdateActiveViewContainers()
        {
            bool showPets = _modPetsTracker && (_activeView == "pets");
            bool showCraft = _modCraftingTracker && (_activeView == "craft");

            if (!showPets && !showCraft)
            {
                if (_modPetsTracker) { _activeView = "pets"; showPets = true; }
                else if (_modCraftingTracker) { _activeView = "craft"; showCraft = true; }
            }

            _petsTrackerContainer.Visibility = showPets ? Visibility.Visible : Visibility.Collapsed;
            _craftingContainer.Visibility = showCraft ? Visibility.Visible : Visibility.Collapsed;

            _viewPetsBtn.Background = (_activeView == "pets")
                ? new SolidColorBrush(Color.FromArgb(90, 56, 189, 248))
                : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            _viewPetsBtn.Foreground = (_activeView == "pets")
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            _viewCraftBtn.Background = (_activeView == "craft")
                ? new SolidColorBrush(Color.FromArgb(90, 16, 185, 129))
                : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            _viewCraftBtn.Foreground = (_activeView == "craft")
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }

        private void OnOptionChanged()
        {
            _cbModMap.IsChecked = _modZoneMap;
            _miniMapBorder.Visibility = _modZoneMap ? Visibility.Visible : Visibility.Collapsed;
            SaveSettings();
            RefreshView();
        }

        private void UpdateAllTabVisuals()
        {
            ApplyTabBtnStyle(_tabAroundBtn, _currentTab == "around");
            ApplyTabBtnStyle(_tabAreaBtn, _currentTab == "area");
            ApplyTabBtnStyle(_tabMapBtn, _currentTab == "map");
            ApplyTabBtnStyle(_tabAllBtn, _currentTab == "all");
        }

        private Button CreateTabBtn(string label, string tabKey)
        {
            Button b = new Button
            {
                Content = label,
                Width = 32,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) =>
            {
                _currentTab = tabKey;
                UpdateAllTabVisuals();
                RefreshView();
                SaveSettings();
            };
            return b;
        }

        private static void ApplyTabBtnStyle(Button b, bool active)
        {
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

        private Button CreateIconBtn(string icon, Action onClick)
        {
            Button b = new Button
            {
                Content = icon,
                Width = 24,
                Height = 22,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Margin = new Thickness(1, 0, 1, 0)
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private CheckBox CreateOptionCheck(bool initVal, Action<bool> onChange)
        {
            CheckBox cb = new CheckBox
            {
                IsChecked = initVal,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 8, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cb.Checked += (s, e) => onChange(true);
            cb.Unchecked += (s, e) => onChange(false);
            return cb;
        }

        private Button CreateSmallMapBtn(string label, Action onClick)
        {
            Button b = new Button
            {
                Content = label,
                Width = 22,
                Height = 20,
                Margin = new Thickness(0, 0, 0, 3),
                Background = new SolidColorBrush(Color.FromArgb(200, 15, 23, 42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
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
                BuildDirectStateFromPetsDb();
                RefreshView();
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

        private void PollAllStateSources()
        {
            try
            {
                bool stateChanged = false;

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

                if (File.Exists(_qlTrackerPath))
                {
                    DateTime wt = File.GetLastWriteTimeUtc(_qlTrackerPath);
                    if (wt != _lastQlTrackerWrite)
                    {
                        _lastQlTrackerWrite = wt;
                        stateChanged = true;
                    }
                }

                // If sniffer hasn't written state or state is missing, run Direct C# engine!
                if (_latestData == null || (DateTime.UtcNow - _lastStateWrite).TotalSeconds > 2.5)
                {
                    if (BuildDirectStateFromPetsDb())
                    {
                        stateChanged = true;
                    }
                }

                if (stateChanged && _latestData != null)
                {
                    RefreshView();
                }
            }
            catch { }
        }

        private bool BuildDirectStateFromPetsDb()
        {
            try
            {
                if (_petsDb == null)
                {
                    if (!File.Exists(_petsDbPath)) return false;
                    JavaScriptSerializer serInit = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    _petsDb = serInit.Deserialize<Dictionary<string, object>>(ReadFileShared(_petsDbPath));
                }
                if (_petsDb == null) return false;

                JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

                Dictionary<string, object> tracker = null;
                if (File.Exists(_qlTrackerPath))
                {
                    try { tracker = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(_qlTrackerPath)); } catch { }
                }

                // Live inventory from tickets.json
                string ticketsPath = System.IO.Path.Combine(_questlogDir, "tickets.json");
                Dictionary<string, int> inventory = new Dictionary<string, int>();
                if (File.Exists(ticketsPath))
                {
                    try
                    {
                        var tdata = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(ticketsPath));
                        if (tdata != null && tdata.ContainsKey("characters"))
                        {
                            var charsDict = tdata["characters"] as Dictionary<string, object>;
                            if (charsDict != null && charsDict.Count > 0)
                            {
                                Dictionary<string, object> chosenChar = null;
                                string charName = "";
                                if (tracker != null && tracker.ContainsKey("character"))
                                {
                                    var ch = tracker["character"] as Dictionary<string, object>;
                                    if (ch != null) charName = GetStr(ch, "name", "");
                                }
                                foreach (var kvp in charsDict)
                                {
                                    if (!string.IsNullOrEmpty(charName) && kvp.Key.EndsWith(":" + charName))
                                    {
                                        chosenChar = kvp.Value as Dictionary<string, object>;
                                        break;
                                    }
                                }
                                if (chosenChar == null)
                                {
                                    foreach (var kvp in charsDict) { chosenChar = kvp.Value as Dictionary<string, object>; break; }
                                }
                                if (chosenChar != null && chosenChar.ContainsKey("stacks"))
                                {
                                    var stacksArr = chosenChar["stacks"] as ArrayList;
                                    if (stacksArr != null)
                                    {
                                        foreach (object sObj in stacksArr)
                                        {
                                            var sd = sObj as Dictionary<string, object>;
                                            if (sd != null)
                                            {
                                                string itm = GetStr(sd, "item", "");
                                                int cnt = GetInt(sd, "count", 0);
                                                if (!string.IsNullOrEmpty(itm) && cnt > 0)
                                                    inventory[itm] = (inventory.ContainsKey(itm) ? inventory[itm] : 0) + cnt;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Gathering session from loot-sessions/index.json
                string lootPath = System.IO.Path.Combine(_questlogDir, "loot-sessions", "index.json");
                Dictionary<string, object> craftingSession = null;
                if (File.Exists(lootPath))
                {
                    try
                    {
                        var ldata = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(lootPath));
                        if (ldata != null && ldata.ContainsKey("entries"))
                        {
                            var entriesArr = ldata["entries"] as ArrayList;
                            if (entriesArr != null && entriesArr.Count > 0)
                            {
                                craftingSession = entriesArr[0] as Dictionary<string, object>;
                            }
                        }
                    }
                    catch { }
                }

                Dictionary<string, object> levelsDict = new Dictionary<string, object>();
                Dictionary<string, object> soulsDict = new Dictionary<string, object>();
                Dictionary<string, object> killsDict = new Dictionary<string, object>();
                Dictionary<string, object> soulsGainedDict = new Dictionary<string, object>();
                Dictionary<int, int> aroundMap = new Dictionary<int, int>();
                string charNameStr = "";
                string charServerStr = "";
                string charRaceStr = "dark";

                if (tracker != null)
                {
                    if (tracker.ContainsKey("character"))
                    {
                        var ch = tracker["character"] as Dictionary<string, object>;
                        if (ch != null)
                        {
                            charNameStr = GetStr(ch, "name", "");
                            charServerStr = GetStr(ch, "server", "");
                            charRaceStr = GetStr(ch, "race", "dark");
                            if (ch.ContainsKey("levels")) levelsDict = ch["levels"] as Dictionary<string, object> ?? levelsDict;
                            if (ch.ContainsKey("souls")) soulsDict = ch["souls"] as Dictionary<string, object> ?? soulsDict;
                        }
                    }
                    if (tracker.ContainsKey("kills")) killsDict = tracker["kills"] as Dictionary<string, object> ?? killsDict;
                    if (tracker.ContainsKey("soulsGained")) soulsGainedDict = tracker["soulsGained"] as Dictionary<string, object> ?? soulsGainedDict;
                    if (tracker.ContainsKey("around"))
                    {
                        var ar = tracker["around"] as Dictionary<string, object>;
                        if (ar != null)
                        {
                            foreach (var kvp in ar)
                            {
                                int nid;
                                if (int.TryParse(kvp.Key, out nid))
                                    aroundMap[nid] = Convert.ToInt32(kvp.Value);
                            }
                        }
                    }
                }

                string curMapId = (charRaceStr == "light") ? "1010" : "1110";
                string curAreaId = "";
                string curAreaName = "";
                double? playerMapX = null;
                double? playerMapY = null;
                long nowMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

                if (tracker != null && tracker.ContainsKey("killedAt"))
                {
                    var killedAt = tracker["killedAt"] as Dictionary<string, object>;
                    if (killedAt != null)
                    {
                        string newestNpc = null;
                        long newestTime = 0;
                        foreach (var kvp in killedAt)
                        {
                            long t = Convert.ToInt64(kvp.Value);
                            if (t > newestTime)
                            {
                                newestTime = t;
                                newestNpc = kvp.Key;
                            }
                            if (nowMs - t < 300000)
                            {
                                int nid;
                                if (int.TryParse(kvp.Key, out nid) && !aroundMap.ContainsKey(nid))
                                    aroundMap[nid] = 1;
                            }
                        }

                        if (!string.IsNullOrEmpty(newestNpc) && (nowMs - newestTime < 2 * 3600 * 1000))
                        {
                            int npcInt;
                            if (int.TryParse(newestNpc, out npcInt))
                            {
                                var petsArr = _petsDb["pets"] as ArrayList;
                                if (petsArr != null)
                                {
                                    foreach (object pObj in petsArr)
                                    {
                                        var pd = pObj as Dictionary<string, object>;
                                        if (pd == null) continue;
                                        var npcsArr = pd["npcs"] as ArrayList;
                                        if (npcsArr != null && npcsArr.Contains(npcInt))
                                        {
                                            var spawnsByWorld = pd["spawnsByWorld"] as Dictionary<string, object>;
                                            if (spawnsByWorld != null)
                                            {
                                                foreach (var wkvp in spawnsByWorld)
                                                {
                                                    var sList = wkvp.Value as ArrayList;
                                                    if (sList != null)
                                                    {
                                                        double sumX = 0, sumY = 0;
                                                        int matchCount = 0;
                                                        string foundAreaId = "";
                                                        string foundAreaName = "";
                                                        foreach (object spObj in sList)
                                                        {
                                                            var sp = spObj as Dictionary<string, object>;
                                                            if (sp != null && GetInt(sp, "npc", 0) == npcInt)
                                                            {
                                                                sumX += GetDouble(sp, "x", 0);
                                                                sumY += GetDouble(sp, "y", 0);
                                                                matchCount++;
                                                                if (string.IsNullOrEmpty(foundAreaId))
                                                                {
                                                                    foundAreaId = GetStr(sp, "areaId", "");
                                                                    foundAreaName = GetStr(sp, "areaName", "");
                                                                }
                                                            }
                                                        }
                                                        if (matchCount > 0)
                                                        {
                                                            curMapId = wkvp.Key;
                                                            playerMapX = sumX / matchCount;
                                                            playerMapY = sumY / matchCount;
                                                            curAreaId = foundAreaId;
                                                            curAreaName = foundAreaName;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (File.Exists(_overrideFilePath))
                {
                    try
                    {
                        var ov = ser.Deserialize<Dictionary<string, object>>(ReadFileShared(_overrideFilePath));
                        if (ov != null && ov.ContainsKey("mapId"))
                        {
                            string ovMap = GetStr(ov, "mapId", "");
                            if (!string.IsNullOrEmpty(ovMap)) curMapId = ovMap;
                        }
                    }
                    catch { }
                }

                ArrayList rows = new ArrayList();
                int totalRegistered = 0;
                int totalLevel3 = 0;
                int totalMissingSoulsAll = 0;
                int inCurrentMapPetsCount = 0;
                int inCurrentMapLevel3Count = 0;
                int totalMissingSoulsCurrentMap = 0;
                int totalMissingSoulsCurrentArea = 0;
                int missingPetsInCurrentArea = 0;
                int totalAroundPets = 0;
                int totalNpcsAroundCount = 0;

                var allPets = _petsDb["pets"] as ArrayList;
                if (allPets != null)
                {
                    foreach (object pObj in allPets)
                    {
                        var pet = pObj as Dictionary<string, object>;
                        if (pet == null) continue;
                        int petId = GetInt(pet, "id", 0);
                        string petKey = petId.ToString();

                        int rawLv = levelsDict.ContainsKey(petKey) ? GetInt(levelsDict, petKey, 0) : 0;
                        int rawSouls = soulsDict.ContainsKey(petKey) ? GetInt(soulsDict, petKey, 0) : 0;

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
                        double fillRatio = isLv3 ? 1.0 : (nextNeed > 0 ? Math.Min(1.0, (double)inStep / nextNeed) : 0.0);

                        if (rawLv >= 1) totalRegistered++;
                        if (isLv3) totalLevel3++;
                        bool farmable = GetBool(pet, "farmable", true);
                        if (farmable) totalMissingSoulsAll += remTo3;

                        int aroundCount = 0;
                        string primaryMonsterName = GetStr(pet, "name", "");
                        var mobs = pet["monsters"] as ArrayList;
                        if (mobs != null)
                        {
                            foreach (object mObj in mobs)
                            {
                                var md = mObj as Dictionary<string, object>;
                                if (md != null)
                                {
                                    int mid = GetInt(md, "id", 0);
                                    if (aroundMap.ContainsKey(mid))
                                    {
                                        aroundCount += aroundMap[mid];
                                        primaryMonsterName = GetStr(md, "name", primaryMonsterName);
                                    }
                                }
                            }
                        }
                        if (aroundCount > 0)
                        {
                            totalAroundPets++;
                            totalNpcsAroundCount += aroundCount;
                        }

                        int spawnsInMap = 0;
                        int spawnsInArea = 0;
                        double? nearestDist = null;
                        double? targetX = null, targetY = null;

                        var spawnsByWorld = pet["spawnsByWorld"] as Dictionary<string, object>;
                        if (spawnsByWorld != null && spawnsByWorld.ContainsKey(curMapId))
                        {
                            var sList = spawnsByWorld[curMapId] as ArrayList;
                            if (sList != null)
                            {
                                spawnsInMap = sList.Count;
                                foreach (object spObj in sList)
                                {
                                    var sp = spObj as Dictionary<string, object>;
                                    if (sp == null) continue;
                                    string sArea = GetStr(sp, "areaId", "");
                                    if (!string.IsNullOrEmpty(curAreaId) && sArea == curAreaId)
                                    {
                                        spawnsInArea++;
                                    }
                                    if (playerMapX.HasValue && playerMapY.HasValue)
                                    {
                                        double dx = GetDouble(sp, "x", 0) - playerMapX.Value;
                                        double dy = GetDouble(sp, "y", 0) - playerMapY.Value;
                                        double d = Math.Round(Math.Sqrt(dx * dx + dy * dy) / 100.0);
                                        if (!nearestDist.HasValue || d < nearestDist.Value)
                                        {
                                            nearestDist = d;
                                            targetX = GetDouble(sp, "x", 0);
                                            targetY = GetDouble(sp, "y", 0);
                                        }
                                    }
                                }
                            }
                        }

                        string directionArrow = "";
                        double? directionAngle = null;
                        if (playerMapX.HasValue && playerMapY.HasValue && targetX.HasValue && targetY.HasValue)
                        {
                            double dx = targetX.Value - playerMapX.Value;
                            double dy = targetY.Value - playerMapY.Value;
                            if (Math.Sqrt(dx * dx + dy * dy) > 100)
                            {
                                directionAngle = (Math.Atan2(dy, dx) * 180.0) / Math.PI;
                                directionArrow = AngleToArrow(directionAngle.Value);
                            }
                        }

                        if (spawnsInMap > 0)
                        {
                            inCurrentMapPetsCount++;
                            if (isLv3) inCurrentMapLevel3Count++;
                            if (farmable) totalMissingSoulsCurrentMap += remTo3;
                        }
                        if ((spawnsInArea > 0 || aroundCount > 0) && farmable && !isLv3)
                        {
                            missingPetsInCurrentArea++;
                            totalMissingSoulsCurrentArea += remTo3;
                        }

                        Dictionary<string, object> row = new Dictionary<string, object>();
                        row["id"] = petId;
                        row["name"] = GetStr(pet, "name", "");
                        row["primaryMonsterName"] = primaryMonsterName;
                        row["icon"] = GetStr(pet, "icon", "");
                        row["genus"] = GetStr(pet, "genus", "");
                        row["farmable"] = farmable;
                        row["monsters"] = pet["monsters"];
                        row["worlds"] = pet["worlds"];
                        row["totalSpawns"] = GetInt(pet, "totalSpawns", 0);
                        row["level"] = rawLv;
                        row["effectiveLevel"] = effLv;
                        row["souls"] = rawSouls;
                        row["soulsInStep"] = inStep;
                        row["nextStepNeed"] = nextNeed;
                        row["totalRequiredFor3"] = TOTAL_SOULS_LV3;
                        row["remainingToLevel3"] = remTo3;
                        row["isLevel3"] = isLv3;
                        row["stepFillRatio"] = fillRatio;
                        row["aroundCount"] = aroundCount;
                        row["spawnsInCurrentMap"] = spawnsInMap;
                        row["spawnsInCurrentArea"] = spawnsInArea;
                        row["nearestDistanceM"] = nearestDist.HasValue ? (object)nearestDist.Value : null;
                        row["isLiveDistance"] = false;
                        row["directionArrow"] = directionArrow;
                        row["directionAngleDeg"] = directionAngle.HasValue ? (object)directionAngle.Value : null;
                        rows.Add(row);
                    }
                }

                rows.Sort(new PetRowComparer());

                ArrayList miniMapClusters = new ArrayList();
                ArrayList miniMapSpawns = new ArrayList();
                foreach (object rObj in rows)
                {
                    var r = rObj as Dictionary<string, object>;
                    if (r == null) continue;
                    int rSpawnsInMap = GetInt(r, "spawnsInCurrentMap", 0);
                    int rAround = GetInt(r, "aroundCount", 0);
                    if (rSpawnsInMap <= 0 && rAround <= 0) continue;

                    int rPetId = GetInt(r, "id", 0);
                    var petData = FindPetInDb(rPetId);
                    if (petData != null)
                    {
                        var spawnsByWorld = petData["spawnsByWorld"] as Dictionary<string, object>;
                        if (spawnsByWorld != null && spawnsByWorld.ContainsKey(curMapId))
                        {
                            var spList = spawnsByWorld[curMapId] as ArrayList;
                            if (spList != null && spList.Count > 0)
                            {
                                double sx = 0, sy = 0;
                                foreach (object spObj in spList)
                                {
                                    var sp = spObj as Dictionary<string, object>;
                                    if (sp != null) { sx += GetDouble(sp, "x", 0); sy += GetDouble(sp, "y", 0); }
                                }
                                Dictionary<string, object> cl = new Dictionary<string, object>();
                                cl["petId"] = rPetId;
                                cl["petName"] = GetStr(r, "name", "");
                                cl["monsterName"] = GetStr(r, "primaryMonsterName", "");
                                cl["x"] = sx / spList.Count;
                                cl["y"] = sy / spList.Count;
                                cl["count"] = spList.Count;
                                cl["aroundCount"] = rAround;
                                cl["inCurrentArea"] = GetInt(r, "spawnsInCurrentArea", 0) > 0;
                                cl["isLevel3"] = GetBool(r, "isLevel3", false);
                                cl["remainingToLevel3"] = GetInt(r, "remainingToLevel3", 0);
                                miniMapClusters.Add(cl);

                                foreach (object spObj in spList)
                                {
                                    if (miniMapSpawns.Count >= 200) break;
                                    var sp = spObj as Dictionary<string, object>;
                                    if (sp != null)
                                    {
                                        Dictionary<string, object> sEntry = new Dictionary<string, object>();
                                        sEntry["x"] = GetDouble(sp, "x", 0);
                                        sEntry["y"] = GetDouble(sp, "y", 0);
                                        sEntry["petId"] = rPetId;
                                        sEntry["name"] = GetStr(r, "primaryMonsterName", "");
                                        sEntry["isLevel3"] = GetBool(r, "isLevel3", false);
                                        miniMapSpawns.Add(sEntry);
                                    }
                                }
                            }
                        }
                    }
                }

                ArrayList playerArr = null;
                if (playerMapX.HasValue && playerMapY.HasValue)
                {
                    playerArr = new ArrayList { playerMapX.Value, playerMapY.Value };
                }

                Dictionary<string, object> miniMapObj = new Dictionary<string, object>();
                miniMapObj["player"] = playerArr;
                miniMapObj["areaName"] = curAreaName;
                miniMapObj["clusters"] = miniMapClusters;
                miniMapObj["spawns"] = miniMapSpawns;
                miniMapObj["live"] = new ArrayList();

                Dictionary<string, object> locObj = new Dictionary<string, object>();
                locObj["mapId"] = curMapId;
                locObj["areaId"] = curAreaId;
                locObj["areaName"] = curAreaName;

                Dictionary<string, object> charObj = new Dictionary<string, object>();
                charObj["name"] = charNameStr;
                charObj["server"] = charServerStr;
                charObj["race"] = charRaceStr;

                Dictionary<string, object> sumObj = new Dictionary<string, object>();
                sumObj["totalPets"] = allPets != null ? allPets.Count : 0;
                sumObj["registeredPets"] = totalRegistered;
                sumObj["level3Pets"] = totalLevel3;
                sumObj["aroundPetsCount"] = totalAroundPets;
                sumObj["totalNpcsAroundCount"] = totalNpcsAroundCount;
                sumObj["inCurrentMapPetsCount"] = inCurrentMapPetsCount;
                sumObj["inCurrentMapLevel3Count"] = inCurrentMapLevel3Count;
                sumObj["missingPetsInCurrentArea"] = missingPetsInCurrentArea;
                sumObj["missingSoulsCurrentArea"] = totalMissingSoulsCurrentArea;
                sumObj["missingSoulsCurrentMap"] = totalMissingSoulsCurrentMap;
                sumObj["missingSoulsAll"] = totalMissingSoulsAll;

                Dictionary<string, object> statePayload = new Dictionary<string, object>();
                statePayload["timestamp"] = nowMs;
                statePayload["location"] = locObj;
                statePayload["character"] = charObj;
                statePayload["summary"] = sumObj;
                statePayload["miniMap"] = miniMapObj;
                statePayload["worlds"] = _petsDb["worlds"];
                statePayload["recentEvents"] = new ArrayList();
                statePayload["inventory"] = inventory;
                statePayload["craftingSession"] = craftingSession;
                statePayload["pets"] = rows;

                _latestData = statePayload;
                _lastStateWrite = DateTime.UtcNow;

                try
                {
                    string jsonOut = ser.Serialize(statePayload);
                    File.WriteAllText(_stateFilePath, jsonOut, Encoding.UTF8);
                }
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string AngleToArrow(double deg)
        {
            double norm = ((deg % 360) + 360) % 360;
            int idx = (int)Math.Round(norm / 45) % 8;
            string[] arrows = new string[] { "→", "↘", "↓", "↙", "←", "↖", "↑", "↗" };
            return (idx >= 0 && idx < arrows.Length) ? arrows[idx] : "";
        }

        private Dictionary<string, object> FindPetInDb(int petId)
        {
            if (_petsDb == null || !_petsDb.ContainsKey("pets")) return null;
            var list = _petsDb["pets"] as ArrayList;
            if (list == null) return null;
            foreach (object pObj in list)
            {
                var pd = pObj as Dictionary<string, object>;
                if (pd != null && GetInt(pd, "id", 0) == petId) return pd;
            }
            return null;
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

            // 1. Refresh Header Bar
            var loc = _latestData.ContainsKey("location") ? _latestData["location"] as Dictionary<string, object> : null;
            string mapId = GetStr(loc, "mapId", "");
            string mapName = GetStr(loc, "mapName", "");
            string areaName = GetStr(loc, "areaName", "");

            var worlds = _latestData.ContainsKey("worlds") ? _latestData["worlds"] as Dictionary<string, object> : null;
            if (string.IsNullOrEmpty(mapName) && worlds != null && worlds.ContainsKey(mapId))
            {
                var wObj = worlds[mapId] as Dictionary<string, object>;
                if (wObj != null) mapName = GetStr(wObj, "name", "");
            }

            string displayLoc = mapName;
            if (!string.IsNullOrEmpty(areaName)) displayLoc += " · " + areaName;
            _headerTitleText.Text = string.IsNullOrEmpty(displayLoc) ? "Aion 2 Overlay Hub" : displayLoc;

            // 2. Populate Worlds Combo
            if (worlds != null && _mapCombo.Items.Count != worlds.Count)
            {
                _updatingMapCombo = true;
                _mapCombo.Items.Clear();
                foreach (var kvp in worlds)
                {
                    var wDict = kvp.Value as Dictionary<string, object>;
                    string wName = (wDict != null) ? GetStr(wDict, "name", kvp.Key) : kvp.Key;
                    _mapCombo.Items.Add(new KeyValuePair<string, string>(kvp.Key, wName));
                }
                _updatingMapCombo = false;
            }
            if (_mapCombo.Items.Count > 0 && (_mapCombo.SelectedValue == null || _mapCombo.SelectedValue.ToString() != mapId))
            {
                _updatingMapCombo = true;
                _mapCombo.SelectedValue = mapId;
                _updatingMapCombo = false;
            }

            // 3. Refresh Active View
            if (_activeView == "craft")
            {
                RefreshCraftingView();
            }
            else
            {
                RefreshPetsView();
            }
        }

        private void RefreshPetsView()
        {
            var petsList = _latestData.ContainsKey("pets") ? _latestData["pets"] as ArrayList : null;
            if (petsList == null) return;

            string q = (_searchBox.Text ?? "").Trim().ToLowerInvariant();

            List<Dictionary<string, object>> filteredPets = new List<Dictionary<string, object>>();
            int missingPetsHere = 0;
            int missingSoulsHere = 0;

            // Check if any pet has aroundCount > 0
            int totalAroundCount = 0;
            foreach (object itemObj in petsList)
            {
                var p = itemObj as Dictionary<string, object>;
                if (p != null) totalAroundCount += GetInt(p, "aroundCount", 0);
            }

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
                string mobName = GetStr(p, "primaryMonsterName", "");

                // Scope Tab Filtering
                if (_currentTab == "around")
                {
                    if (totalAroundCount > 0)
                    {
                        if (aroundCount <= 0) continue;
                    }
                    else
                    {
                        if (spawnsInArea <= 0 && aroundCount <= 0) continue;
                    }
                }
                if (_currentTab == "area" && spawnsInArea <= 0 && aroundCount <= 0) continue;
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

            filteredPets.Sort(new PetRowComparer());

            _summaryAccentText.Text = Tr("חיות להשלמה: ", "Missing pets: ", "Питомцы: ");
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
            string name = GetStr(p, "name", "");
            string mobName = GetStr(p, "primaryMonsterName", name);
            int level = GetInt(p, "level", 0);
            int souls = GetInt(p, "souls", 0);
            int soulsInStep = GetInt(p, "soulsInStep", 0);
            int nextNeed = GetInt(p, "nextStepNeed", 0);
            int remTo3 = GetInt(p, "remainingToLevel3", 0);
            bool isLv3 = GetBool(p, "isLevel3", false);
            double fillRatio = GetDouble(p, "stepFillRatio", 0.0);
            int aroundCount = GetInt(p, "aroundCount", 0);
            int spawnsInArea = GetInt(p, "spawnsInCurrentArea", 0);
            string arrow = GetStr(p, "directionArrow", "");
            double? distM = p.ContainsKey("nearestDistanceM") && p["nearestDistanceM"] != null ? (double?)Convert.ToDouble(p["nearestDistanceM"]) : null;

            Border card = new Border
            {
                Margin = new Thickness(0, 0, 0, 5),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand
            };

            if (isLv3)
            {
                card.Background = new SolidColorBrush(Color.FromArgb(100, 16, 28, 22));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 52, 211, 153));
                card.BorderThickness = new Thickness(1);
            }
            else if (aroundCount > 0)
            {
                card.Background = new SolidColorBrush(Color.FromArgb(170, 6, 95, 70));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                card.BorderThickness = new Thickness(1.5);
            }
            else if (spawnsInArea > 0)
            {
                card.Background = new SolidColorBrush(Color.FromArgb(160, 20, 30, 48));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 56, 189, 248));
                card.BorderThickness = new Thickness(1);
            }
            else
            {
                card.Background = new SolidColorBrush(Color.FromArgb(110, 15, 23, 42));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                card.BorderThickness = new Thickness(1);
            }

            card.MouseLeftButtonDown += (s, e) =>
            {
                _selectedPetId = petId;
                if (_modZoneMap) RenderMiniMap();
            };

            DockPanel mainDock = new DockPanel { LastChildFill = true };

            // Left: Icon
            if (_optShowIcons)
            {
                string iconUrl = GetStr(p, "icon", "");
                ImageSource iconSrc = GetOrDownloadPetIcon(petId, iconUrl);

                Border iconBorder = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 15, 26)),
                    BorderBrush = isLv3 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (iconSrc != null)
                {
                    Image img = new Image { Source = iconSrc, Stretch = Stretch.UniformToFill };
                    iconBorder.Child = img;
                }
                else
                {
                    TextBlock tbInit = new TextBlock
                    {
                        Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    iconBorder.Child = tbInit;
                }
                DockPanel.SetDock(iconBorder, Dock.Left);
                mainDock.Children.Add(iconBorder);
            }

            // Right: Distance / Arrow & Around Pin badge
            StackPanel rightStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            if (aroundCount > 0 && _optShowAroundPin)
            {
                Border aroundBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 0, 3)
                };
                aroundBadge.Child = new TextBlock
                {
                    Text = "🟢 " + aroundCount + " 📍",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                rightStack.Children.Add(aroundBadge);
            }
            else if (spawnsInArea > 0 && _optShowAroundPin)
            {
                Border areaBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(70, 56, 189, 248)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 0, 0, 3)
                };
                areaBadge.Child = new TextBlock
                {
                    Text = spawnsInArea + " 📍",
                    Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                rightStack.Children.Add(areaBadge);
            }

            if (_optShowDistance && distM.HasValue && !isLv3)
            {
                TextBlock distTb = new TextBlock
                {
                    Text = (string.IsNullOrEmpty(arrow) ? "" : arrow + " ") + Math.Round(distM.Value) + " m",
                    Foreground = (distM.Value < 120)
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : (distM.Value < 350
                            ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                            : new SolidColorBrush(Color.FromRgb(148, 163, 184))),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                rightStack.Children.Add(distTb);
            }

            DockPanel.SetDock(rightStack, Dock.Right);
            mainDock.Children.Add(rightStack);

            // Center: Names + Diamonds + Progress Bar
            StackPanel centerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // Title Row
            DockPanel nameRow = new DockPanel { LastChildFill = true };
            if (_optShowRemaining3)
            {
                TextBlock remTb = new TextBlock
                {
                    Text = isLv3 ? "MAX" : Tr("חסר ", "Need ", "Ост. ") + remTo3,
                    Foreground = isLv3
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : (remTo3 <= 5
                            ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                            : new SolidColorBrush(Color.FromRgb(203, 213, 225))),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                DockPanel.SetDock(remTb, Dock.Right);
                nameRow.Children.Add(remTb);
            }

            string displayName = _optShowPetAndMob && !string.IsNullOrEmpty(mobName) && mobName != name
                ? mobName + " (" + name + ")"
                : name;

            TextBlock nameTbCenter = new TextBlock
            {
                Text = displayName,
                Foreground = isLv3 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nameRow.Children.Add(nameTbCenter);
            centerStack.Children.Add(nameRow);

            // Diamonds & Souls Row
            DockPanel subRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
            if (_optShowDiamonds)
            {
                string diamonds = (level >= 1 ? "◆" : "◇") + (level >= 2 ? "◆" : "◇") + (level >= 3 ? "◆" : "◇");
                TextBlock diaTb = new TextBlock
                {
                    Text = diamonds + " Lv." + level,
                    Foreground = isLv3 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                DockPanel.SetDock(diaTb, Dock.Left);
                subRow.Children.Add(diaTb);
            }

            string stepLabel = isLv3 ? "Lv.3 Complete" : soulsInStep + "/" + nextNeed;
            TextBlock stepTb = new TextBlock
            {
                Text = stepLabel,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 203, 213, 225)),
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            subRow.Children.Add(stepTb);
            centerStack.Children.Add(subRow);

            // Progress Bar
            Border progTrack = new Border
            {
                Height = 4.5,
                Background = new SolidColorBrush(Color.FromArgb(120, 15, 23, 42)),
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true
            };
            Grid progGrid = new Grid();
            Border progFill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(2),
                Background = isLv3
                    ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                    : new SolidColorBrush(Color.FromRgb(56, 189, 248))
            };
            progFill.Width = Math.Max(0, Math.Min(1.0, fillRatio)) * 260.0;
            progGrid.Children.Add(progFill);
            progTrack.Child = progGrid;
            centerStack.Children.Add(progTrack);

            mainDock.Children.Add(centerStack);
            card.Child = mainDock;
            return card;
        }

        private void RefreshCraftingView()
        {
            _craftCardsStack.Children.Clear();

            Dictionary<string, int> inventory = new Dictionary<string, int>();
            if (_latestData != null && _latestData.ContainsKey("inventory"))
            {
                var invObj = _latestData["inventory"] as Dictionary<string, object>;
                if (invObj != null)
                {
                    foreach (var kvp in invObj)
                    {
                        inventory[kvp.Key] = Convert.ToInt32(kvp.Value);
                    }
                }
            }

            Dictionary<string, int> sessionGathers = new Dictionary<string, int>();
            double activeHours = 0.5;
            if (_latestData != null && _latestData.ContainsKey("craftingSession"))
            {
                var sess = _latestData["craftingSession"] as Dictionary<string, object>;
                if (sess != null)
                {
                    long activeMs = GetInt(sess, "activeMs", 0);
                    if (activeMs > 60000) activeHours = activeMs / 3600000.0;
                    if (sess.ContainsKey("items"))
                    {
                        var itmsArr = sess["items"] as ArrayList;
                        if (itmsArr != null)
                        {
                            foreach (object itmObj in itmsArr)
                            {
                                var idict = itmObj as Dictionary<string, object>;
                                if (idict != null)
                                {
                                    string itmId = GetStr(idict, "item", "");
                                    int cnt = GetInt(idict, "count", 0);
                                    if (!string.IsNullOrEmpty(itmId))
                                        sessionGathers[itmId] = (sessionGathers.ContainsKey(itmId) ? sessionGathers[itmId] : 0) + cnt;
                                }
                            }
                        }
                    }
                }
            }

            int readyCount = 0;
            int totalTracked = 0;

            foreach (var wish in _craftWishlist)
            {
                var info = _craftCatalog.Find(c => c.Id == wish.ItemId);
                if (info == null)
                {
                    info = new CraftItem
                    {
                        Id = wish.ItemId,
                        NameHeb = "חפץ #" + wish.ItemId,
                        NameEng = "Item #" + wish.ItemId,
                        Category = "other",
                        Emoji = "📦",
                        BestMap = "World Drop",
                        BestArea = "Monsters"
                    };
                }

                if (_craftCategoryFilter != "all" && info.Category != _craftCategoryFilter)
                    continue;

                totalTracked++;
                int have = inventory.ContainsKey(wish.ItemId) ? inventory[wish.ItemId] : 0;
                bool isReady = (have >= wish.TargetCount);
                if (isReady) readyCount++;

                int sessGathered = sessionGathers.ContainsKey(wish.ItemId) ? sessionGathers[wish.ItemId] : 0;
                double ratePerHour = sessGathered > 0 ? (sessGathered / activeHours) : 0;
                int missing = Math.Max(0, wish.TargetCount - have);
                int etaMinutes = (missing > 0 && ratePerHour > 0) ? (int)Math.Ceiling(missing * 60.0 / ratePerHour) : 0;

                Border card = new Border
                {
                    Background = isReady
                        ? new SolidColorBrush(Color.FromArgb(130, 6, 95, 70))
                        : new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                    BorderBrush = isReady
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : new SolidColorBrush(Color.FromArgb(70, 56, 189, 248)),
                    BorderThickness = new Thickness(isReady ? 1.5 : 1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 7, 8, 7),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                StackPanel cardStack = new StackPanel();

                // Top: Emoji + Name + Ready Badge + Quick Actions
                DockPanel topRow = new DockPanel { LastChildFill = true };

                StackPanel actionBtns = new StackPanel { Orientation = Orientation.Horizontal };
                Button subBtn = CreateSmallCraftBtn("−10", () => { wish.TargetCount = Math.Max(1, wish.TargetCount - 10); SaveWishlist(); RefreshCraftingView(); });
                Button addBtn = CreateSmallCraftBtn("+10", () => { wish.TargetCount += 10; SaveWishlist(); RefreshCraftingView(); });
                Button doubleBtn = CreateSmallCraftBtn("×2", () => { wish.TargetCount *= 2; SaveWishlist(); RefreshCraftingView(); });
                Button delBtn = CreateSmallCraftBtn("🗑", () => { _craftWishlist.Remove(wish); SaveWishlist(); RefreshCraftingView(); });
                delBtn.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));

                actionBtns.Children.Add(subBtn);
                actionBtns.Children.Add(addBtn);
                actionBtns.Children.Add(doubleBtn);
                actionBtns.Children.Add(delBtn);

                DockPanel.SetDock(actionBtns, Dock.Right);
                topRow.Children.Add(actionBtns);

                TextBlock titleTb = new TextBlock
                {
                    Text = info.Emoji + " " + (_lang == "ENG" ? info.NameEng : info.NameHeb),
                    Foreground = isReady ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                topRow.Children.Add(titleTb);
                cardStack.Children.Add(topRow);

                // Progress Bar Row: Have / Goal
                DockPanel progTextRow = new DockPanel { Margin = new Thickness(0, 4, 0, 3) };
                TextBlock statusBadge = new TextBlock
                {
                    Text = isReady
                        ? Tr("✔ מוכן לייצור! 🎉", "✔ Ready to Craft! 🎉", "✔ Готово к крафту! 🎉")
                        : string.Format(Tr("חסר: {0}", "Missing: {0}", "Не хватает: {0}"), missing),
                    Foreground = isReady ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                };
                DockPanel.SetDock(statusBadge, Dock.Right);
                progTextRow.Children.Add(statusBadge);

                double pct = Math.Min(100.0, (have * 100.0) / Math.Max(1, wish.TargetCount));
                TextBlock countTb = new TextBlock
                {
                    Text = string.Format("{0} / {1} ({2:0}%)", have, wish.TargetCount, pct),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5
                };
                progTextRow.Children.Add(countTb);
                cardStack.Children.Add(progTextRow);

                Border barTrack = new Border
                {
                    Height = 6,
                    Background = new SolidColorBrush(Color.FromArgb(120, 10, 15, 26)),
                    CornerRadius = new CornerRadius(3),
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                Border barFill = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(3),
                    Background = isReady
                        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                        : new SolidColorBrush(Color.FromRgb(56, 189, 248))
                };
                barFill.Width = Math.Max(4, Math.Min(1.0, have / (double)wish.TargetCount)) * 380.0;
                barTrack.Child = barFill;
                cardStack.Children.Add(barTrack);

                // Recipe Breakdown pills if recipe
                if (info.IsRecipe && info.RecipeMats.Count > 0)
                {
                    WrapPanel matsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                    foreach (var m in info.RecipeMats)
                    {
                        int reqTotal = m.Count * wish.TargetCount;
                        int currentMatHave = inventory.ContainsKey(m.ItemId) ? inventory[m.ItemId] : 0;
                        bool matReady = (currentMatHave >= reqTotal);

                        Border matPill = new Border
                        {
                            Background = matReady ? new SolidColorBrush(Color.FromArgb(80, 16, 185, 129)) : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(5, 1, 5, 1),
                            Margin = new Thickness(0, 0, 4, 2)
                        };
                        matPill.Child = new TextBlock
                        {
                            Text = string.Format("{0}: {1}/{2} {3}", m.Name, currentMatHave, reqTotal, matReady ? "✔" : ""),
                            Foreground = matReady ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                            FontSize = 9.5,
                            FontWeight = FontWeights.SemiBold
                        };
                        matsWrap.Children.Add(matPill);
                    }
                    cardStack.Children.Add(matsWrap);
                }

                // Rate / ETA & Location Guide Row
                DockPanel infoRow = new DockPanel();
                if (ratePerHour > 0 && !isReady)
                {
                    TextBlock rateTb = new TextBlock
                    {
                        Text = string.Format(Tr("⚡ +{0:0}/שעה · נשאר כ-{1} דק'", "⚡ +{0:0}/hr · ETA: {1}m", "⚡ +{0:0}/ч · Ост: {1}м"), ratePerHour, etaMinutes),
                        Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    };
                    DockPanel.SetDock(rateTb, Dock.Right);
                    infoRow.Children.Add(rateTb);
                }

                TextBlock guideTb = new TextBlock
                {
                    Text = string.Format(Tr("📍 {0} ({1}) · דרישה: רמה {2}", "📍 {0} ({1}) · Req Lv.{2}", "📍 {0} ({1}) · Ур.{2}"), info.BestMap, info.BestArea, info.GatherLevel),
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 9.5
                };
                infoRow.Children.Add(guideTb);
                cardStack.Children.Add(infoRow);

                card.Child = cardStack;
                _craftCardsStack.Children.Add(card);
            }

            _craftSummaryText.Text = string.Format(
                Tr("{0} מתוך {1} יעדים מוכנים לייצור! ✨", "{0} of {1} goals ready to craft! ✨", "{0} из {1} целей готовы к крафту! ✨"),
                readyCount, totalTracked
            );
        }

        private Button CreateSmallCraftBtn(string label, Action onClick)
        {
            Button b = new Button
            {
                Content = label,
                Height = 18,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void RenderMiniMap()
        {
            if (_miniMapCanvas == null) return;
            _miniMapCanvas.Children.Clear();

            var mm = _latestData != null && _latestData.ContainsKey("miniMap") ? _latestData["miniMap"] as Dictionary<string, object> : null;
            if (mm == null) return;

            double w = _miniMapCanvas.ActualWidth;
            double h = _miniMapCanvas.ActualHeight;
            if (w < 10) w = 400;
            if (h < 10) h = 150;

            var playerArr = mm.ContainsKey("player") ? mm["player"] as ArrayList : null;
            bool hasPlayer = playerArr != null && playerArr.Count >= 2;

            double cx = hasPlayer ? Convert.ToDouble(playerArr[0]) : 0;
            double cy = hasPlayer ? Convert.ToDouble(playerArr[1]) : 0;

            var clustersArr = mm.ContainsKey("clusters") ? mm["clusters"] as ArrayList : null;
            var spawnsArr = mm.ContainsKey("spawns") ? mm["spawns"] as ArrayList : null;
            var liveArr = mm.ContainsKey("live") ? mm["live"] as ArrayList : null;

            if (_selectedPetId > 0 && clustersArr != null)
            {
                foreach (object cObj in clustersArr)
                {
                    var cd = cObj as Dictionary<string, object>;
                    if (cd != null && GetInt(cd, "petId", 0) == _selectedPetId)
                    {
                        cx = GetDouble(cd, "x", cx);
                        cy = GetDouble(cd, "y", cy);
                        break;
                    }
                }
            }

            cx += _mapPanX;
            cy += _mapPanY;

            double scale = (1.0 / 16.0) * _mapZoom;
            Func<double, double, Point> toScreen = (gx, gy) =>
            {
                double sx = w / 2 + (gx - cx) * scale;
                double sy = h / 2 - (gy - cy) * scale;
                return new Point(sx, sy);
            };

            // Grid lines
            for (int r = 1; r <= 3; r++)
            {
                double rPix = (r * 15000.0) * scale;
                if (rPix > 5 && rPix < Math.Max(w, h))
                {
                    Ellipse ring = new Ellipse
                    {
                        Width = rPix * 2,
                        Height = rPix * 2,
                        Stroke = new SolidColorBrush(Color.FromArgb(20, 56, 189, 248)),
                        StrokeThickness = 1,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(ring, w / 2 - rPix);
                    Canvas.SetTop(ring, h / 2 - rPix);
                    _miniMapCanvas.Children.Add(ring);
                }
            }

            // Spawns Dots
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

            // Labeled Clusters
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
                    if (cp.X < -30 || cp.X > w + 30 || cp.Y < -15 || cp.Y > h + 15) continue;

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

            // Live Nearby Monsters
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

            // Player Marker
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
