using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Aion2PetsOverlay
{
    public class CraftRecipeMat
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public int Count { get; set; }
        public string DropZone { get; set; }
    }

    public class CraftItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Profession { get; set; }
        public int Level { get; set; }
        public string Rarity { get; set; }
        public string Zone { get; set; }
        public List<CraftRecipeMat> Materials { get; set; }

        public CraftItem()
        {
            Materials = new List<CraftRecipeMat>();
        }
    }

    public class CraftWishlistItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Profession { get; set; }
        public int TargetCount { get; set; }
        public int HaveCount { get; set; }
        public List<CraftRecipeMat> Materials { get; set; }

        public CraftWishlistItem()
        {
            Materials = new List<CraftRecipeMat>();
            TargetCount = 1;
        }
    }

    public class PetRowComparer : IComparer<Dictionary<string, object>>, IComparer
    {
        public int Compare(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            double da = a.ContainsKey("nearestDistanceM") && a["nearestDistanceM"] != null ? Convert.ToDouble(a["nearestDistanceM"]) : 99999.0;
            double db = b.ContainsKey("nearestDistanceM") && b["nearestDistanceM"] != null ? Convert.ToDouble(b["nearestDistanceM"]) : 99999.0;
            int c = da.CompareTo(db);
            if (c != 0) return c;

            int ra = a.ContainsKey("remainingToLevel3") ? Convert.ToInt32(a["remainingToLevel3"]) : 0;
            int rb = b.ContainsKey("remainingToLevel3") ? Convert.ToInt32(b["remainingToLevel3"]) : 0;
            return ra.CompareTo(rb);
        }

        public int Compare(object x, object y)
        {
            return Compare(x as Dictionary<string, object>, y as Dictionary<string, object>);
        }
    }

    public class PartyMemberState
    {
        public long EntityId { get; set; }
        public string Name { get; set; }
        public string ClassName { get; set; }
        public int Level { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public bool IsDead { get; set; }
        public bool IsSelf { get; set; }
        public List<long> Debuffs { get; set; }
        public DateTime LastUpdated { get; set; }

        public PartyMemberState()
        {
            Debuffs = new List<long>();
            LastUpdated = DateTime.Now;
            Hp = 12760;
            MaxHp = 12760;
        }

        public double HpPercent
        {
            get
            {
                if (MaxHp <= 0) return 100.0;
                double pct = ((double)Hp / (double)MaxHp) * 100.0;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                return pct;
            }
        }
    }

    public enum RecommendationType
    {
        None,
        EmergencyHeal,
        NormalHeal,
        Regeneration,
        Dispel,
        Resurrection,
        SelfShield
    }

    public class SkillComboStep
    {
        public string SkillName { get; set; }
        public string IconFileName { get; set; }
        public string Keybind { get; set; }
        public string Hint { get; set; }

        public SkillComboStep(string name, string icon, string key, string hint)
        {
            SkillName = name;
            IconFileName = icon;
            Keybind = key;
            Hint = hint;
        }
    }

    public class ClericSkillRecommendation
    {
        public RecommendationType Type { get; set; }
        public int Priority { get; set; }
        public string TargetName { get; set; }
        public double TargetHpPercent { get; set; }
        public int TargetHp { get; set; }
        public int TargetMaxHp { get; set; }
        public string SkillNameHeb { get; set; }
        public string SkillNameEng { get; set; }
        public string SkillNameRus { get; set; }
        public string DescriptionHeb { get; set; }
        public string DescriptionEng { get; set; }
        public string DescriptionRus { get; set; }
        public string IconFileName { get; set; }
        public string FallbackEmoji { get; set; }
        public string Keybind { get; set; }
        public string GlowColorHex { get; set; }
        public List<SkillComboStep> ComboSequence { get; set; }

        public ClericSkillRecommendation()
        {
            Type = RecommendationType.None;
            ComboSequence = new List<SkillComboStep>();
            GlowColorHex = "#38bdf8";
            FallbackEmoji = "⚡";
            TargetHpPercent = 100.0;
            TargetHp = 12760;
            TargetMaxHp = 12760;
        }

        public string GetName(string lang)
        {
            if (lang == "ENG") return SkillNameEng ?? SkillNameHeb;
            if (lang == "RUS") return SkillNameRus ?? SkillNameHeb;
            return SkillNameHeb ?? SkillNameEng;
        }

        public string GetDescription(string lang)
        {
            if (lang == "ENG") return DescriptionEng ?? DescriptionHeb;
            if (lang == "RUS") return DescriptionRus ?? DescriptionHeb;
            return DescriptionHeb ?? DescriptionEng;
        }
    }

    public class CombatLogMonitor : IDisposable
    {
        public delegate void PartyUpdatedHandler(List<PartyMemberState> members);
        public event PartyUpdatedHandler OnPartyUpdate;

        private string _logDir;
        private string _activeLogFile;
        private FileStream _fileStream;
        private StreamReader _reader;
        private DispatcherTimer _pollTimer;
        private JavaScriptSerializer _json = new JavaScriptSerializer();

        public string ActiveSelfName { get; private set; }
        public long SelfEntityId { get; private set; }
        public int SelfLevel { get; private set; }
        public string SelfClass { get; private set; }

        private readonly Dictionary<long, PartyMemberState> _partyMap = new Dictionary<long, PartyMemberState>();
        private readonly HashSet<long> _dispelableDebuffIds = new HashSet<long>();
        private readonly Dictionary<long, DateTime> _debuffTimestamps = new Dictionary<long, DateTime>();
        private readonly object _lock = new object();

        public CombatLogMonitor()
        {
            _logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            ActiveSelfName = "ManatDam";
            SelfEntityId = 10694;
            SelfLevel = 45;
            SelfClass = "Cleric";

            LoadDebuffDatabase();
            DetectActiveCharacterFromState();

            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(250);
            _pollTimer.Tick += delegate(object s, EventArgs e) { Update(); };
            _pollTimer.Start();
        }

        private void DetectActiveCharacterFromState()
        {
            try
            {
                string statePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "overlay-state.json");
                if (File.Exists(statePath))
                {
                    string txt = File.ReadAllText(statePath);
                    Dictionary<string, object> root = _json.Deserialize<Dictionary<string, object>>(txt);
                    if (root != null)
                    {
                        if (root.ContainsKey("character"))
                        {
                            Dictionary<string, object> ch = root["character"] as Dictionary<string, object>;
                            if (ch != null)
                            {
                                if (ch.ContainsKey("name") && ch["name"] != null) ActiveSelfName = ch["name"].ToString();
                                if (ch.ContainsKey("level") && ch["level"] != null)
                                {
                                    int lvl;
                                    if (int.TryParse(ch["level"].ToString(), out lvl) && lvl > 0) SelfLevel = lvl;
                                }
                                if (ch.ContainsKey("className") && ch["className"] != null) SelfClass = ch["className"].ToString();
                                else if (ch.ContainsKey("class") && ch["class"] != null) SelfClass = ch["class"].ToString();
                            }
                        }
                        if (root.ContainsKey("cleric"))
                        {
                            Dictionary<string, object> cl = root["cleric"] as Dictionary<string, object>;
                            if (cl != null && cl.ContainsKey("self"))
                            {
                                Dictionary<string, object> sf = cl["self"] as Dictionary<string, object>;
                                if (sf != null)
                                {
                                    if (sf.ContainsKey("entity") && sf["entity"] != null)
                                    {
                                        long eid;
                                        if (long.TryParse(sf["entity"].ToString(), out eid) && eid > 0) SelfEntityId = eid;
                                    }
                                    if (sf.ContainsKey("name") && sf["name"] != null) ActiveSelfName = sf["name"].ToString();
                                    if (sf.ContainsKey("level") && sf["level"] != null)
                                    {
                                        int slvl;
                                        if (int.TryParse(sf["level"].ToString(), out slvl) && slvl > 0) SelfLevel = slvl;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void LoadDebuffDatabase()
        {
            long[] knownDebuffs = new long[] {
                10101, 10102, 10103, 10201, 10202, 10203,
                1101, 1102, 1103, 1104, 1105, 1201, 1202,
                2001, 2002, 2003, 3001, 3002, 4001, 4002
            };
            foreach (long id in knownDebuffs) _dispelableDebuffIds.Add(id);
        }

        private void FindLatestLogFile()
        {
            try
            {
                List<string> candidateDirs = new List<string> {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Questlog", "aion-2", "combat-logs"),
                    _logDir,
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "logs"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Questlog", "logs"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Questlog", "logs")
                };

                string bestFile = null;
                DateTime bestTime = DateTime.MinValue;

                foreach (string dir in candidateDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        string[] files = Directory.GetFiles(dir, "*.ndjson");
                        foreach (string f in files)
                        {
                            if (f.EndsWith("index.json", StringComparison.OrdinalIgnoreCase)) continue;
                            DateTime wt = File.GetLastWriteTime(f);
                            if (wt > bestTime)
                            {
                                bestTime = wt;
                                bestFile = f;
                            }
                        }
                    }
                }

                if (bestFile != null && bestFile != _activeLogFile)
                {
                    CloseCurrentLog();
                    _activeLogFile = bestFile;
                    _fileStream = new FileStream(_activeLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (_fileStream.Length > 100000)
                    {
                        // Seek to the tail of the log file so we only stream fresh live combat events,
                        // avoiding replay of historical HP events from lines recorded hours ago!
                        _fileStream.Seek(_fileStream.Length - 100000, SeekOrigin.Begin);
                        _reader = new StreamReader(_fileStream, Encoding.UTF8);
                        _reader.ReadLine(); // discard partial line
                    }
                    else
                    {
                        _reader = new StreamReader(_fileStream, Encoding.UTF8);
                        ReadLogHeader();
                    }
                }
            }
            catch { }
        }

        private void ReadLogHeader()
        {
            try
            {
                if (_reader == null) return;
                for (int i = 0; i < 20; i++)
                {
                    string line = _reader.ReadLine();
                    if (line == null) break;
                    if (line.Contains("\"selfName\""))
                    {
                        Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
                        if (ev != null && ev.ContainsKey("selfName") && ev["selfName"] != null)
                        {
                            string sname = ev["selfName"].ToString();
                            if (!string.IsNullOrEmpty(sname)) ActiveSelfName = sname;
                        }
                    }
                    if (line.Contains("\"type\":\"player\""))
                    {
                        ParsePlayerLine(line);
                    }
                }
            }
            catch { }
        }

        private void CloseCurrentLog()
        {
            try
            {
                if (_reader != null) { _reader.Dispose(); _reader = null; }
                if (_fileStream != null) { _fileStream.Dispose(); _fileStream = null; }
            }
            catch { }
        }

        public void Update()
        {
            try
            {
                bool hasNewData = false;

                // 1. Direct real-time sync with overlay-state.json (sniffer packet state)
                try
                {
                    string statePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "overlay-state.json");
                    if (File.Exists(statePath))
                    {
                        string txt = File.ReadAllText(statePath);
                        Dictionary<string, object> root = _json.Deserialize<Dictionary<string, object>>(txt);
                        if (root != null)
                        {
                            if (root.ContainsKey("character"))
                            {
                                Dictionary<string, object> ch = root["character"] as Dictionary<string, object>;
                                if (ch != null)
                                {
                                    if (ch.ContainsKey("name") && ch["name"] != null) ActiveSelfName = ch["name"].ToString();
                                    if (ch.ContainsKey("level") && ch["level"] != null)
                                    {
                                        int lvl;
                                        if (int.TryParse(ch["level"].ToString(), out lvl) && lvl > 0) SelfLevel = lvl;
                                    }
                                }
                            }

                            if (root.ContainsKey("cleric"))
                            {
                                Dictionary<string, object> cl = root["cleric"] as Dictionary<string, object>;
                                if (cl != null)
                                {
                                    if (cl.ContainsKey("self"))
                                    {
                                        Dictionary<string, object> sf = cl["self"] as Dictionary<string, object>;
                                        if (sf != null)
                                        {
                                            int shp = sf.ContainsKey("hp") ? Convert.ToInt32(sf["hp"]) : -1;
                                            int smax = sf.ContainsKey("maxHp") ? Convert.ToInt32(sf["maxHp"]) : -1;
                                            string sname = sf.ContainsKey("name") && sf["name"] != null ? sf["name"].ToString() : ActiveSelfName;
                                            int slvl = sf.ContainsKey("level") ? Convert.ToInt32(sf["level"]) : SelfLevel;
                                            long seid = sf.ContainsKey("entity") ? Convert.ToInt64(sf["entity"]) : SelfEntityId;
                                            if (seid > 0) SelfEntityId = seid;
                                            if (!string.IsNullOrEmpty(sname)) ActiveSelfName = sname;

                                            lock (_lock)
                                            {
                                                if (!_partyMap.ContainsKey(SelfEntityId))
                                                {
                                                    _partyMap[SelfEntityId] = new PartyMemberState
                                                    {
                                                        EntityId = SelfEntityId,
                                                        Name = ActiveSelfName,
                                                        ClassName = "Cleric",
                                                        Level = slvl,
                                                        Hp = shp > 0 ? shp : 12760,
                                                        MaxHp = smax > 0 ? smax : 12760,
                                                        IsSelf = true,
                                                        IsDead = (shp == 0),
                                                        LastUpdated = DateTime.Now
                                                    };
                                                }
                                                else
                                                {
                                                    if (shp >= 0) _partyMap[SelfEntityId].Hp = shp;
                                                    if (smax > 0) _partyMap[SelfEntityId].MaxHp = smax;
                                                    _partyMap[SelfEntityId].IsDead = (shp == 0);
                                                    _partyMap[SelfEntityId].Name = ActiveSelfName;
                                                    _partyMap[SelfEntityId].Level = slvl;
                                                    _partyMap[SelfEntityId].LastUpdated = DateTime.Now;
                                                }

                                                // Sync debuffs from sniffer state
                                                if (sf.ContainsKey("debuffs") && sf["debuffs"] is ArrayList)
                                                {
                                                    ArrayList dList = sf["debuffs"] as ArrayList;
                                                    _partyMap[SelfEntityId].Debuffs.Clear();
                                                    foreach (object dObj in dList)
                                                    {
                                                        long dId;
                                                        if (long.TryParse(dObj.ToString(), out dId))
                                                            _partyMap[SelfEntityId].Debuffs.Add(dId);
                                                    }
                                                }
                                                else
                                                {
                                                    _partyMap[SelfEntityId].Debuffs.Clear();
                                                }

                                                hasNewData = true;
                                            }
                                        }
                                    }

                                    if (cl.ContainsKey("party") && cl["party"] is ArrayList)
                                    {
                                        ArrayList pArr = cl["party"] as ArrayList;
                                        lock (_lock)
                                        {
                                            foreach (object po in pArr)
                                            {
                                                Dictionary<string, object> pd = po as Dictionary<string, object>;
                                                if (pd == null) continue;
                                                long pent = pd.ContainsKey("entity") ? Convert.ToInt64(pd["entity"]) : 0;
                                                if (pent == 0 || pent == SelfEntityId) continue;
                                                int php = pd.ContainsKey("hp") ? Convert.ToInt32(pd["hp"]) : 10000;
                                                int pmax = pd.ContainsKey("maxHp") ? Convert.ToInt32(pd["maxHp"]) : 10000;
                                                string pname = pd.ContainsKey("name") && pd["name"] != null ? pd["name"].ToString() : ("Party_" + pent);
                                                bool pdead = pd.ContainsKey("isDead") ? Convert.ToBoolean(pd["isDead"]) : (php == 0);

                                                if (!_partyMap.ContainsKey(pent))
                                                {
                                                    _partyMap[pent] = new PartyMemberState
                                                    {
                                                        EntityId = pent,
                                                        Name = pname,
                                                        ClassName = "Member",
                                                        Level = 45,
                                                        Hp = php,
                                                        MaxHp = pmax,
                                                        IsSelf = false,
                                                        IsDead = pdead,
                                                        LastUpdated = DateTime.Now
                                                    };
                                                }
                                                else
                                                {
                                                    if (php >= 0) _partyMap[pent].Hp = php;
                                                    if (pmax > 0) _partyMap[pent].MaxHp = pmax;
                                                    _partyMap[pent].IsDead = pdead;
                                                    _partyMap[pent].LastUpdated = DateTime.Now;
                                                }

                                                if (pd.ContainsKey("debuffs") && pd["debuffs"] is ArrayList)
                                                {
                                                    ArrayList dList = pd["debuffs"] as ArrayList;
                                                    _partyMap[pent].Debuffs.Clear();
                                                    foreach (object dObj in dList)
                                                    {
                                                        long dId;
                                                        if (long.TryParse(dObj.ToString(), out dId))
                                                            _partyMap[pent].Debuffs.Add(dId);
                                                    }
                                                }
                                                else
                                                {
                                                    _partyMap[pent].Debuffs.Clear();
                                                }

                                                hasNewData = true;
                                            }

                                            // Prune members not in cl.party and not Self
                                            HashSet<long> partyEnts = new HashSet<long>();
                                            foreach (object po in pArr)
                                            {
                                                Dictionary<string, object> pd = po as Dictionary<string, object>;
                                                if (pd != null && pd.ContainsKey("entity"))
                                                {
                                                    long pe;
                                                    if (long.TryParse(pd["entity"].ToString(), out pe) && pe > 0) partyEnts.Add(pe);
                                                }
                                            }
                                            List<long> toRemove = new List<long>();
                                            foreach (var kvp in _partyMap)
                                            {
                                                if (!kvp.Value.IsSelf && kvp.Key != SelfEntityId && !partyEnts.Contains(kvp.Key))
                                                    toRemove.Add(kvp.Key);
                                            }
                                            foreach (long deadEnt in toRemove)
                                            {
                                                _partyMap.Remove(deadEnt);
                                                hasNewData = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2. Stream Questlog combat log ndjson if active
                if (_fileStream == null || _reader == null)
                {
                    FindLatestLogFile();
                }

                if (_reader != null)
                {
                    string line;
                    int linesRead = 0;
                    while ((line = _reader.ReadLine()) != null && linesRead < 1000)
                    {
                        linesRead++;
                        if (string.IsNullOrEmpty(line)) continue;
                        if (ProcessLogLine(line)) hasNewData = true;
                    }
                }

                // 3. Clear debuffs that haven't been refreshed for > 12 seconds
                lock (_lock)
                {
                    DateTime now = DateTime.Now;
                    foreach (PartyMemberState p in _partyMap.Values)
                    {
                        if (p.Debuffs != null && p.Debuffs.Count > 0)
                        {
                            List<long> toRemove = new List<long>();
                            foreach (long d in p.Debuffs)
                            {
                                if (_debuffTimestamps.ContainsKey(d) && (now - _debuffTimestamps[d]).TotalSeconds > 12.0)
                                    toRemove.Add(d);
                            }
                            foreach (long r in toRemove)
                            {
                                p.Debuffs.Remove(r);
                                hasNewData = true;
                            }
                        }
                    }
                }

                if (_partyMap.Count == 0)
                {
                    CheckFallbackSoloPlayer();
                }

                if (hasNewData && OnPartyUpdate != null)
                {
                    List<PartyMemberState> members;
                    lock (_lock)
                    {
                        members = new List<PartyMemberState>(_partyMap.Values);
                    }
                    if (OnPartyUpdate != null) OnPartyUpdate(members);
                }
            }
            catch { }
        }

        private void CheckFallbackSoloPlayer()
        {
            lock (_lock)
            {
                if (!_partyMap.ContainsKey(SelfEntityId))
                {
                    _partyMap[SelfEntityId] = new PartyMemberState
                    {
                        EntityId = SelfEntityId,
                        Name = ActiveSelfName,
                        ClassName = SelfClass,
                        Level = SelfLevel,
                        Hp = 12760,
                        MaxHp = 12760,
                        IsSelf = true,
                        IsDead = false,
                        LastUpdated = DateTime.Now
                    };
                    if (OnPartyUpdate != null) OnPartyUpdate(new List<PartyMemberState>(_partyMap.Values));
                }
            }
        }

        private bool ProcessLogLine(string line)
        {
            try
            {
                if (line.Contains("\"type\":\"player\"")) return ParsePlayerLine(line);
                if (line.Contains("\"type\":\"hp\"")) return ParseHpLine(line);
                if (line.Contains("\"type\":\"stats\"")) return ParseStatsLine(line);
                if (line.Contains("\"type\":\"buff-end\"")) return ParseBuffEndLine(line);
                if (line.Contains("\"type\":\"buff\"")) return ParseBuffLine(line);
                if (line.Contains("\"type\":\"death\"")) return ParseDeathLine(line);
            }
            catch { }
            return false;
        }

        private bool ParsePlayerLine(string line)
        {
            Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
            if (ev == null) return false;

            long ent = 0;
            if (ev.ContainsKey("entity") && ev["entity"] != null) long.TryParse(ev["entity"].ToString(), out ent);
            if (ent == 0) return false;

            string name = ev.ContainsKey("name") && ev["name"] != null ? ev["name"].ToString() : ("Player_" + ent);
            string cls = ev.ContainsKey("class") && ev["class"] != null ? ev["class"].ToString() : "Cleric";
            int lvl = 45;
            if (ev.ContainsKey("level") && ev["level"] != null) int.TryParse(ev["level"].ToString(), out lvl);
            bool isSelf = ev.ContainsKey("self") && Convert.ToBoolean(ev["self"]);

            if (isSelf || (!string.IsNullOrEmpty(ActiveSelfName) && string.Equals(name, ActiveSelfName, StringComparison.OrdinalIgnoreCase)))
            {
                isSelf = true;
                SelfEntityId = ent;
                ActiveSelfName = name;
                SelfLevel = lvl;
                SelfClass = cls;
            }
            else
            {
                // Do not register random zone bystanders from combat logs into party!
                return false;
            }

            lock (_lock)
            {
                if (!_partyMap.ContainsKey(ent))
                {
                    _partyMap[ent] = new PartyMemberState
                    {
                        EntityId = ent,
                        Name = name,
                        ClassName = cls,
                        Level = lvl,
                        IsSelf = isSelf,
                        Hp = 12760,
                        MaxHp = 12760,
                        LastUpdated = DateTime.Now
                    };
                }
                else
                {
                    _partyMap[ent].Name = name;
                    _partyMap[ent].ClassName = cls;
                    _partyMap[ent].Level = lvl;
                    _partyMap[ent].IsSelf = isSelf;
                }
            }
            return true;
        }

        private bool ParseHpLine(string line)
        {
            Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
            if (ev == null) return false;

            long ent = 0;
            if (ev.ContainsKey("entity") && ev["entity"] != null) long.TryParse(ev["entity"].ToString(), out ent);
            if (ent == 0) return false;

            int hp = -1;
            int maxHp = -1;
            if (ev.ContainsKey("hp") && ev["hp"] != null) int.TryParse(ev["hp"].ToString(), out hp);
            if (ev.ContainsKey("maxHp") && ev["maxHp"] != null) int.TryParse(ev["maxHp"].ToString(), out maxHp);
            else if (ev.ContainsKey("max") && ev["max"] != null) int.TryParse(ev["max"].ToString(), out maxHp);

            lock (_lock)
            {
                if (_partyMap.ContainsKey(ent))
                {
                    if (hp >= 0) _partyMap[ent].Hp = hp;
                    if (maxHp > 0) _partyMap[ent].MaxHp = maxHp;
                    _partyMap[ent].IsDead = (hp == 0);
                    _partyMap[ent].LastUpdated = DateTime.Now;
                    return true;
                }
                else if (ent == SelfEntityId || (SelfEntityId == 0 && _partyMap.Count == 0))
                {
                    _partyMap[ent] = new PartyMemberState
                    {
                        EntityId = ent,
                        Name = ActiveSelfName,
                        ClassName = SelfClass,
                        Level = SelfLevel,
                        Hp = hp >= 0 ? hp : 12760,
                        MaxHp = maxHp > 0 ? maxHp : 12760,
                        IsSelf = true,
                        IsDead = (hp == 0),
                        LastUpdated = DateTime.Now
                    };
                    return true;
                }
            }
            return false;
        }

        private bool ParseStatsLine(string line)
        {
            Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
            if (ev == null) return false;

            long ent = 0;
            if (ev.ContainsKey("entity") && ev["entity"] != null) long.TryParse(ev["entity"].ToString(), out ent);
            if (ent == 0) return false;

            int maxHp = -1;
            if (ev.ContainsKey("maxHp") && ev["maxHp"] != null) int.TryParse(ev["maxHp"].ToString(), out maxHp);
            else if (ev.ContainsKey("max") && ev["max"] != null) int.TryParse(ev["max"].ToString(), out maxHp);

            if (maxHp > 0)
            {
                lock (_lock)
                {
                    if (_partyMap.ContainsKey(ent))
                    {
                        _partyMap[ent].MaxHp = maxHp;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool ParseBuffLine(string line)
        {
            Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
            if (ev == null) return false;

            long target = 0;
            if (ev.ContainsKey("target") && ev["target"] != null) long.TryParse(ev["target"].ToString(), out target);
            if (target == 0) return false;

            long buffId = 0;
            if (ev.ContainsKey("buff") && ev["buff"] != null) long.TryParse(ev["buff"].ToString(), out buffId);
            if (buffId == 0) return false;

            if (_dispelableDebuffIds.Contains(buffId))
            {
                lock (_lock)
                {
                    if (_partyMap.ContainsKey(target))
                    {
                        if (!_partyMap[target].Debuffs.Contains(buffId))
                            _partyMap[target].Debuffs.Add(buffId);
                        _debuffTimestamps[buffId] = DateTime.Now;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool ParseBuffEndLine(string line)
        {
            Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
            if (ev == null) return false;

            long target = 0;
            if (ev.ContainsKey("target") && ev["target"] != null) long.TryParse(ev["target"].ToString(), out target);
            if (target == 0) return false;

            long buffId = 0;
            if (ev.ContainsKey("buff") && ev["buff"] != null) long.TryParse(ev["buff"].ToString(), out buffId);

            lock (_lock)
            {
                if (_partyMap.ContainsKey(target))
                {
                    if (buffId != 0) _partyMap[target].Debuffs.Remove(buffId);
                    else _partyMap[target].Debuffs.Clear();
                    return true;
                }
            }
            return false;
        }

        private bool ParseDeathLine(string line)
        {
            Dictionary<string, object> ev = _json.Deserialize<Dictionary<string, object>>(line);
            if (ev == null) return false;

            long ent = 0;
            if (ev.ContainsKey("target") && ev["target"] != null) long.TryParse(ev["target"].ToString(), out ent);
            else if (ev.ContainsKey("entity") && ev["entity"] != null) long.TryParse(ev["entity"].ToString(), out ent);
            if (ent == 0) return false;

            lock (_lock)
            {
                if (_partyMap.ContainsKey(ent))
                {
                    _partyMap[ent].IsDead = true;
                    _partyMap[ent].Hp = 0;
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer = null; }
            CloseCurrentLog();
        }
    }

    public class SkillSlotControl : Border
    {
        private Image _skillIcon;
        private TextBlock _fallbackEmoji;
        private Border _keybindPill;
        private TextBlock _keybindText;
        private TextBlock _skillNameText;
        private DropShadowEffect _glowEffect;
        private bool _isPrimary;
        private double _slotSize;

        public SkillSlotControl(double size, bool isPrimary)
        {
            _slotSize = size;
            _isPrimary = isPrimary;

            Width = size;
            Height = size + 16;
            Margin = new Thickness(isPrimary ? 4 : 2, 0, isPrimary ? 4 : 2, 0);
            CornerRadius = new CornerRadius(8);
            BorderThickness = new Thickness(isPrimary ? 2 : 1.2);
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            Background = new SolidColorBrush(Color.FromArgb(200, 15, 23, 42));

            _glowEffect = new DropShadowEffect
            {
                Color = Color.FromRgb(56, 189, 248),
                BlurRadius = isPrimary ? 16 : 8,
                ShadowDepth = 0,
                Opacity = isPrimary ? 0.85 : 0.45
            };
            Effect = _glowEffect;

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });

            // Slot Container
            Border iconBox = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(10, 14, 23))
            };

            Grid innerGrid = new Grid();

            _fallbackEmoji = new TextBlock
            {
                FontSize = isPrimary ? 24 : 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            innerGrid.Children.Add(_fallbackEmoji);

            _skillIcon = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            innerGrid.Children.Add(_skillIcon);

            // Keybind Pill (Top-left)
            _keybindPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 2, 6, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(isPrimary ? 4 : 2, 0, isPrimary ? 4 : 2, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 3, 0, 0)
            };
            _keybindText = new TextBlock
            {
                FontSize = isPrimary ? 10.5 : 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21))
            };
            _keybindPill.Child = _keybindText;
            innerGrid.Children.Add(_keybindPill);

            iconBox.Child = innerGrid;
            Grid.SetRow(iconBox, 0);
            g.Children.Add(iconBox);

            // Skill Name Label
            _skillNameText = new TextBlock
            {
                FontSize = isPrimary ? 9.5 : 8.0,
                FontWeight = isPrimary ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = size + 10
            };
            Grid.SetRow(_skillNameText, 1);
            g.Children.Add(_skillNameText);

            Child = g;
        }

        public void SetSkill(string name, string iconFileName, string keybind, string glowColorHex, string fallbackEmoji)
        {
            _skillNameText.Text = name ?? "";
            _keybindText.Text = string.IsNullOrEmpty(keybind) ? "" : ("[" + keybind + "]");
            _fallbackEmoji.Text = fallbackEmoji ?? "⚡";

            Color glow = Color.FromRgb(56, 189, 248);
            try
            {
                if (!string.IsNullOrEmpty(glowColorHex))
                    glow = (Color)ColorConverter.ConvertFromString(glowColorHex);
            }
            catch { }

            BorderBrush = new SolidColorBrush(glow);
            _glowEffect.Color = glow;

            // Load icon from data/skills/cleric/ or data/skills/
            bool loaded = false;
            if (!string.IsNullOrEmpty(iconFileName))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] tryPaths = new string[] {
                    System.IO.Path.Combine(baseDir, "data", "skills", "cleric", iconFileName),
                    System.IO.Path.Combine(baseDir, "data", "skills", iconFileName),
                    System.IO.Path.Combine(baseDir, "data", "icons", iconFileName)
                };
                foreach (string p in tryPaths)
                {
                    if (File.Exists(p))
                    {
                        try
                        {
                            BitmapImage bi = new BitmapImage();
                            bi.BeginInit();
                            bi.UriSource = new Uri(p, UriKind.Absolute);
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.EndInit();
                            bi.Freeze();
                            _skillIcon.Source = bi;
                            _fallbackEmoji.Visibility = Visibility.Collapsed;
                            _skillIcon.Visibility = Visibility.Visible;
                            loaded = true;
                            break;
                        }
                        catch { }
                    }
                }
            }

            if (!loaded)
            {
                _skillIcon.Source = null;
                _skillIcon.Visibility = Visibility.Collapsed;
                _fallbackEmoji.Visibility = Visibility.Visible;
            }
        }

        public void UpdatePulseGlow(double opacity)
        {
            if (_isPrimary && _glowEffect != null)
            {
                _glowEffect.Opacity = opacity;
            }
        }
    }

    public class ClericAssistantWindow : Window
    {
        private OverlayWindow _parent;
        private string _lang = "HEB";

        // Simulation / Testing
        private bool _isSimulating = false;
        private int _simStep = 0;
        private DispatcherTimer _simTimer;
        private DispatcherTimer _glowPulseTimer;
        private double _pulseOpacity = 0.85;
        private bool _pulseIncreasing = false;

        // Settings
        public bool SoloMode = true;
        public double EmergencyThreshold = 40.0;
        public double NormalThreshold = 80.0;
        public double HotThreshold = 90.0;
        public bool DispelEnabled = true;
        public bool ResEnabled = true;
        public bool SoundEnabled = true;
        private DateTime _lastSoundTime = DateTime.MinValue;
        private RecommendationType _lastAlertRecType = RecommendationType.None;
        private string _lastAlertTarget = "";

        // UI Components
        private Border _rootBorder;
        private TextBlock _titleText;
        private TextBlock _statusBadge;
        private Button _btnMode;
        private Button _btnTest;
        private Button _btnClose;

        // Action Bar Combo Slots
        private Border _targetPill;
        private TextBlock _targetPillText;
        private StackPanel _comboBarPanel;
        private SkillSlotControl _slotMain;
        private TextBlock _arrow1;
        private SkillSlotControl _slotFollowup1;
        private TextBlock _arrow2;
        private SkillSlotControl _slotFollowup2;

        // Slim Unit Frames
        private ScrollViewer _partyScrollViewer;
        private StackPanel _partyListPanel;

        private List<PartyMemberState> _lastPartyData = new List<PartyMemberState>();

        public ClericAssistantWindow(OverlayWindow parent)
        {
            _parent = parent;

            Title = "Aion 2 · Cleric Action Assistant";
            Width = 340;
            Height = 145;
            MinWidth = 280;
            MinHeight = 70;
            Left = 820;
            Top = 48;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            };
            WindowChrome.SetWindowChrome(this, chrome);

            BuildUI();

            _glowPulseTimer = new DispatcherTimer();
            _glowPulseTimer.Interval = TimeSpan.FromMilliseconds(75);
            _glowPulseTimer.Tick += GlowPulseTimer_Tick;
            _glowPulseTimer.Start();

            _simTimer = new DispatcherTimer();
            _simTimer.Interval = TimeSpan.FromSeconds(3.5);
            _simTimer.Tick += delegate(object s, EventArgs e)
            {
                if (!_isSimulating) return;
                _simStep = (_simStep + 1) % 5;
                RenderCurrentState();
            };

            LocationChanged += delegate(object s, EventArgs e)
            {
                if (_parent != null) _parent.OnOverlayMoved("cleric", Left, Top, Width, Height);
            };
            SizeChanged += delegate(object s, SizeChangedEventArgs e)
            {
                if (_parent != null) _parent.OnOverlayMoved("cleric", Left, Top, Width, Height);
            };
        }

        private void GlowPulseTimer_Tick(object sender, EventArgs e)
        {
            if (_pulseIncreasing)
            {
                _pulseOpacity += 0.05;
                if (_pulseOpacity >= 0.95) _pulseIncreasing = false;
            }
            else
            {
                _pulseOpacity -= 0.05;
                if (_pulseOpacity <= 0.40) _pulseIncreasing = true;
            }
            if (_slotMain != null) _slotMain.UpdatePulseGlow(_pulseOpacity);
        }

        private void BuildUI()
        {
            _rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 11, 15, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 56, 189, 248)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 6, 8, 6),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(56, 189, 248),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.45
                }
            };
            _rootBorder.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Target pill
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Action Bar Combo Slots
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Slim Unit Frames

            // --- 0. Header Bar ---
            Border headerBar = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(2, 0, 2, 4),
                Cursor = Cursors.SizeAll
            };

            DockPanel headerDock = new DockPanel { LastChildFill = true };

            StackPanel leftHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock iconBadge = new TextBlock
            {
                Text = "⚚",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(iconBadge);

            _titleText = new TextBlock
            {
                Text = "סדרת ריפוי | Action Assist",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 180, 254)),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(_titleText);

            _statusBadge = new TextBlock
            {
                Text = "● מוכן",
                FontSize = 9.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(_statusBadge);

            headerDock.Children.Add(leftHeader);
            DockPanel.SetDock(leftHeader, Dock.Left);

            StackPanel headerControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _btnMode = CreateIconButton("👤", "מצב סולו / קבוצה", delegate(object s, RoutedEventArgs e)
            {
                SoloMode = !SoloMode;
                UpdateModeButton();
                RenderCurrentState();
            });
            UpdateModeButton();
            headerControls.Children.Add(_btnMode);

            _btnTest = CreateIconButton("🧪", "בדוק המלצות בלייב (סימולציה)", delegate(object s, RoutedEventArgs e)
            {
                _isSimulating = !_isSimulating;
                if (_isSimulating)
                {
                    _btnTest.Background = new SolidColorBrush(Color.FromArgb(120, 234, 179, 8));
                    _simTimer.Start();
                    RenderCurrentState();
                }
                else
                {
                    _btnTest.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                    _simTimer.Stop();
                    RenderCurrentState();
                }
            });
            headerControls.Children.Add(_btnTest);

            _btnClose = CreateIconButton("✕", "מזער", delegate(object s, RoutedEventArgs e)
            {
                Hide();
                if (_parent != null) _parent.OnOverlayVisibilityChanged("cleric", false);
            });
            headerControls.Children.Add(_btnClose);

            headerDock.Children.Add(headerControls);
            DockPanel.SetDock(headerControls, Dock.Right);

            headerBar.Child = headerDock;
            Grid.SetRow(headerBar, 0);
            mainGrid.Children.Add(headerBar);

            // --- 1. Target Pill (Whom to heal & why) ---
            _targetPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 2, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _targetPillText = new TextBlock
            {
                Text = "STANDBY · בריא ויציב",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _targetPill.Child = _targetPillText;
            Grid.SetRow(_targetPill, 1);
            mainGrid.Children.Add(_targetPill);

            // --- 2. Action Bar Combo Slots ---
            _comboBarPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4)
            };

            // Main Primary Hero Slot (56x56)
            _slotMain = new SkillSlotControl(56, true);
            _comboBarPanel.Children.Add(_slotMain);

            // Arrow 1
            _arrow1 = new TextBlock
            {
                Text = "→",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };
            _comboBarPanel.Children.Add(_arrow1);

            // Followup Slot 1 (44x44)
            _slotFollowup1 = new SkillSlotControl(44, false);
            _comboBarPanel.Children.Add(_slotFollowup1);

            // Arrow 2
            _arrow2 = new TextBlock
            {
                Text = "→",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 1, 0)
            };
            _comboBarPanel.Children.Add(_arrow2);

            // Followup Slot 2 (38x38)
            _slotFollowup2 = new SkillSlotControl(38, false);
            _comboBarPanel.Children.Add(_slotFollowup2);

            Grid.SetRow(_comboBarPanel, 2);
            mainGrid.Children.Add(_comboBarPanel);

            // --- 3. Slim Unit Frames ---
            _partyScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 2, 0, 0),
                MaxHeight = 120
            };
            _partyListPanel = new StackPanel { Orientation = Orientation.Vertical };
            _partyScrollViewer.Content = _partyListPanel;
            Grid.SetRow(_partyScrollViewer, 3);
            mainGrid.Children.Add(_partyScrollViewer);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;

            RenderStandbyState();
        }

        private Button CreateIconButton(string text, string tooltip, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                Width = 22,
                Height = 20,
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            btn.Click += onClick;
            return btn;
        }

        public void UpdateModeButton()
        {
            if (_btnMode == null) return;
            if (SoloMode)
            {
                _btnMode.Content = "👤";
                _btnMode.ToolTip = Tr("מצב נוכחי: סולו (עוקב ומרפא רק אותך)\nלחץ כדי לעבור למצב קבוצה", "Current mode: Solo (only track & heal YOU)\nClick to switch to Party mode", "Режим: Соло (только вы)\nНажмите для группы");
                _btnMode.Background = new SolidColorBrush(Color.FromArgb(90, 56, 189, 248));
            }
            else
            {
                _btnMode.Content = "👥";
                _btnMode.ToolTip = Tr("מצב נוכחי: קבוצה (עוקב אחרי חברי הקבוצה)\nלחץ כדי לעבור למצב סולו", "Current mode: Party (track party members)\nClick to switch to Solo mode", "Режим: Группа\nНажмите для соло");
                _btnMode.Background = new SolidColorBrush(Color.FromArgb(90, 168, 85, 247));
            }
        }

        public void SetLanguage(string lang)
        {
            _lang = lang;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            if (_titleText != null)
                _titleText.Text = Tr("סדרת ריפוי | Action Assist", "Cleric Action Assist", "Серия исцелений | Ассистент");
            UpdateModeButton();
            RenderCurrentState();
        }

        public void UpdatePartyState(List<PartyMemberState> members)
        {
            if (_isSimulating) return;
            _lastPartyData = members;
            RenderCurrentState();
        }

        public void RenderCurrentState()
        {
            List<PartyMemberState> members = _lastPartyData;

            if (_isSimulating)
            {
                members = BuildSimulationData(_simStep);
            }

            if (members == null || members.Count == 0)
            {
                RenderStandbyState();
                return;
            }

            ClericSkillRecommendation rec = EvaluateRecommendation(members);
            RenderRecommendation(rec);
            RenderPartyFrames(members);
        }

        private List<PartyMemberState> BuildSimulationData(int step)
        {
            List<PartyMemberState> list = new List<PartyMemberState>();
            string meName = _parent != null && !string.IsNullOrEmpty(_parent.ActiveCharacterName) ? _parent.ActiveCharacterName : "ManatDam";

            if (step == 0)
            {
                // Solo wounded at 75%
                list.Add(new PartyMemberState { EntityId = 10694, Name = meName, ClassName = "Cleric", Level = 45, Hp = 9570, MaxHp = 12760, IsSelf = true });
            }
            else if (step == 1)
            {
                // Solo emergency at 30%
                list.Add(new PartyMemberState { EntityId = 10694, Name = meName, ClassName = "Cleric", Level = 45, Hp = 3828, MaxHp = 12760, IsSelf = true });
            }
            else if (step == 2)
            {
                // Debuff Dispel
                PartyMemberState p = new PartyMemberState { EntityId = 10694, Name = meName, ClassName = "Cleric", Level = 45, Hp = 11000, MaxHp = 12760, IsSelf = true };
                p.Debuffs.Add(10101);
                list.Add(p);
            }
            else if (step == 3)
            {
                // Party ally dead
                list.Add(new PartyMemberState { EntityId = 10694, Name = meName, ClassName = "Cleric", Level = 45, Hp = 12760, MaxHp = 12760, IsSelf = true });
                list.Add(new PartyMemberState { EntityId = 2002, Name = "TemplarTank", ClassName = "Templar", Level = 45, Hp = 0, MaxHp = 14500, IsDead = true });
            }
            else
            {
                // 100% healthy
                list.Add(new PartyMemberState { EntityId = 10694, Name = meName, ClassName = "Cleric", Level = 45, Hp = 12760, MaxHp = 12760, IsSelf = true });
            }
            return list;
        }

        private ClericSkillRecommendation EvaluateRecommendation(List<PartyMemberState> members)
        {
            if (SoloMode)
            {
                members = members.FindAll(m => m.IsSelf);
                if (members.Count == 0)
                {
                    string meName = _parent != null && !string.IsNullOrEmpty(_parent.ActiveCharacterName) ? _parent.ActiveCharacterName : "ManatDam";
                    members.Add(new PartyMemberState { EntityId = 10694, Name = meName, ClassName = "Cleric", Level = 45, Hp = 12760, MaxHp = 12760, IsSelf = true });
                }
            }

            ClericSkillRecommendation rec = new ClericSkillRecommendation();

            // 1. Dead Ally (Resurrection) - Group only
            if (ResEnabled && !SoloMode)
            {
                foreach (PartyMemberState p in members)
                {
                    if (p.IsDead && !p.IsSelf)
                    {
                        rec.Type = RecommendationType.Resurrection;
                        rec.Priority = 100;
                        rec.TargetName = p.Name;
                        rec.TargetHpPercent = 0;
                        rec.TargetHp = 0;
                        rec.TargetMaxHp = p.MaxHp > 0 ? p.MaxHp : 10000;
                        rec.SkillNameHeb = "תחיית נשמה (Summon Resurrection)";
                        rec.SkillNameEng = "Summon Resurrection";
                        rec.SkillNameRus = "Призыв воскрешения";
                        rec.DescriptionHeb = "החזר חבר קבוצה שנפל לחיים מיידית!";
                        rec.DescriptionEng = "Revive fallen party member instantly!";
                        rec.DescriptionRus = "Воскресить павшего союзника!";
                        rec.IconFileName = "Icon_cl_skill_039.png";
                        rec.FallbackEmoji = "✝️";
                        rec.Keybind = "3";
                        rec.GlowColorHex = "#10b981";
                        rec.ComboSequence.Add(new SkillComboStep("Summon Resurrection", "Icon_cl_skill_039.png", "3", "Revive"));
                        rec.ComboSequence.Add(new SkillComboStep("Healing Light", "Icon_cl_skill_010.png", "2", "Top off"));
                        return rec;
                    }
                }
            }

            // Find lowest HP party member
            PartyMemberState lowest = null;
            double lowestPct = 999.0;
            foreach (PartyMemberState p in members)
            {
                if (!p.IsDead && p.HpPercent < lowestPct)
                {
                    lowestPct = p.HpPercent;
                    lowest = p;
                }
            }

            // 2. Critical Emergency (HP <= EmergencyThreshold, e.g. 40%)
            if (lowest != null && lowestPct <= EmergencyThreshold)
            {
                rec.Type = RecommendationType.EmergencyHeal;
                rec.Priority = 90;
                rec.TargetName = lowest.IsSelf ? Tr("עליך", "YOU", "НА ВАС") : lowest.Name;
                rec.TargetHpPercent = lowestPct;
                rec.TargetHp = lowest.Hp;
                rec.TargetMaxHp = lowest.MaxHp;
                rec.SkillNameHeb = "מחילת חטאים (Absolution)";
                rec.SkillNameEng = "Absolution";
                rec.SkillNameRus = "Отпущение грехов";
                rec.DescriptionHeb = "ריפוי חירום מתפרץ לקבוצה + הסרת קללות!";
                rec.DescriptionEng = "Emergency burst group heal + cleanses debuffs!";
                rec.DescriptionRus = "Экстренное массовое исцеление + рассеивание!";
                rec.IconFileName = "Icon_cl_skill_029.png";
                rec.FallbackEmoji = "⚠️";
                rec.Keybind = "1";
                rec.GlowColorHex = "#ef4444";
                rec.ComboSequence.Add(new SkillComboStep("Absolution", "Icon_cl_skill_029.png", "1", "Burst Heal"));
                rec.ComboSequence.Add(new SkillComboStep("Healing Light", "Icon_cl_skill_010.png", "2", "Followup Heal"));
                rec.ComboSequence.Add(new SkillComboStep("Light of Protection", "Icon_cl_skill_041.png", "R", "Shield"));
                return rec;
            }

            // 3. Debuff Dispel
            if (DispelEnabled)
            {
                foreach (PartyMemberState p in members)
                {
                    if (!p.IsDead && p.Debuffs != null && p.Debuffs.Count > 0)
                    {
                        rec.Type = RecommendationType.Dispel;
                        rec.Priority = 80;
                        rec.TargetName = p.IsSelf ? Tr("עליך", "YOU", "НА ВАС") : p.Name;
                        rec.TargetHpPercent = p.HpPercent;
                        rec.TargetHp = p.Hp;
                        rec.TargetMaxHp = p.MaxHp;
                        rec.SkillNameHeb = "התאוששות קורנת (Radiant Recovery)";
                        rec.SkillNameEng = "Radiant Recovery";
                        rec.SkillNameRus = "Сияющее восстановление";
                        rec.DescriptionHeb = "הסר קללות, רעלים ודיבאפים + שחזר חיים!";
                        rec.DescriptionEng = "Cleanses debuffs, curses & restores health!";
                        rec.DescriptionRus = "Снять проклятия, яды и восстановить HP!";
                        rec.IconFileName = "Icon_cl_skill_012.png";
                        rec.FallbackEmoji = "🌀";
                        rec.Keybind = "R";
                        rec.GlowColorHex = "#a855f7";
                        rec.ComboSequence.Add(new SkillComboStep("Radiant Recovery", "Icon_cl_skill_012.png", "R", "Dispel & Heal"));
                        rec.ComboSequence.Add(new SkillComboStep("Light of Regeneration", "Icon_cl_skill_009.png", "Q", "HoT"));
                        return rec;
                    }
                }
            }

            // 4. Normal Heal (HP <= NormalThreshold, e.g. 80%)
            if (lowest != null && lowestPct <= NormalThreshold)
            {
                rec.Type = RecommendationType.NormalHeal;
                rec.Priority = 60;
                rec.TargetName = lowest.IsSelf ? Tr("עליך", "YOU", "НА ВАС") : lowest.Name;
                rec.TargetHpPercent = lowestPct;
                rec.TargetHp = lowest.Hp;
                rec.TargetMaxHp = lowest.MaxHp;
                rec.SkillNameHeb = "אור מרפא (Healing Light)";
                rec.SkillNameEng = "Healing Light";
                rec.SkillNameRus = "Исцеляющий свет";
                rec.DescriptionHeb = "מרפא את היעד ומשחזר חיים לקלריק!";
                rec.DescriptionEng = "Restores HP to target and lowest ally!";
                rec.DescriptionRus = "Восстанавливает HP цели и союзнику!";
                rec.IconFileName = "Icon_cl_skill_010.png";
                rec.FallbackEmoji = "⚡";
                rec.Keybind = "2";
                rec.GlowColorHex = "#eab308";
                rec.ComboSequence.Add(new SkillComboStep("Healing Light", "Icon_cl_skill_010.png", "2", "Direct Heal"));
                rec.ComboSequence.Add(new SkillComboStep("Light of Regeneration", "Icon_cl_skill_009.png", "Q", "HoT"));
                return rec;
            }

            // 5. Standby when all healthy (HP > NormalThreshold)
            rec.Type = RecommendationType.None;
            if (lowest != null)
            {
                rec.TargetHp = lowest.Hp;
                rec.TargetMaxHp = lowest.MaxHp;
                rec.TargetHpPercent = lowestPct;
                rec.TargetName = lowest.IsSelf ? Tr("עליך", "YOU", "НА ВАС") : lowest.Name;
            }
            return rec;
        }

        private void RenderRecommendation(ClericSkillRecommendation rec)
        {
            if (rec == null || rec.Type == RecommendationType.None)
            {
                RenderStandbyState();
                return;
            }

            // Reveal action combo skill buttons ONLY when healing or dispel is needed!
            _comboBarPanel.Visibility = Visibility.Visible;
            _targetPill.Visibility = Visibility.Visible;

            string prefix = "";
            if (rec.Type == RecommendationType.EmergencyHeal) prefix = "⚠️ " + Tr("חירום: ", "Emergency: ", "Тревога: ");
            else if (rec.Type == RecommendationType.Dispel) prefix = "🌀 " + Tr("קללה: ", "Debuff: ", "Дебафф: ");
            else if (rec.Type == RecommendationType.Resurrection) prefix = "✝️ " + Tr("החייאה: ", "Resurrect: ", "Воскрешение: ");
            else prefix = "⚡ " + Tr("ריפוי: ", "Heal: ", "Лечение: ");

            string hpDetails = "";
            if (rec.TargetMaxHp > 0 && rec.TargetHp > 0)
                hpDetails = " (" + Math.Round(rec.TargetHpPercent) + "% · " + rec.TargetHp.ToString("N0") + " / " + rec.TargetMaxHp.ToString("N0") + ")";
            else
                hpDetails = " (" + Math.Round(rec.TargetHpPercent) + "% HP)";

            _targetPillText.Text = prefix + rec.TargetName + hpDetails;
            _targetPillText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(rec.GlowColorHex));

            _slotMain.SetSkill(rec.GetName(_lang), rec.IconFileName, rec.Keybind, rec.GlowColorHex, rec.FallbackEmoji);

            if (rec.ComboSequence != null && rec.ComboSequence.Count > 1)
            {
                _arrow1.Visibility = Visibility.Visible;
                _slotFollowup1.Visibility = Visibility.Visible;
                SkillComboStep s1 = rec.ComboSequence[1];
                _slotFollowup1.SetSkill(s1.SkillName, s1.IconFileName, s1.Keybind, "#38bdf8", "✨");
            }
            else
            {
                _arrow1.Visibility = Visibility.Collapsed;
                _slotFollowup1.Visibility = Visibility.Collapsed;
            }

            if (rec.ComboSequence != null && rec.ComboSequence.Count > 2)
            {
                _arrow2.Visibility = Visibility.Visible;
                _slotFollowup2.Visibility = Visibility.Visible;
                SkillComboStep s2 = rec.ComboSequence[2];
                _slotFollowup2.SetSkill(s2.SkillName, s2.IconFileName, s2.Keybind, "#94a3b8", "🛡️");
            }
            else
            {
                _arrow2.Visibility = Visibility.Collapsed;
                _slotFollowup2.Visibility = Visibility.Collapsed;
            }

            _statusBadge.Text = "● " + Tr("פעולה נדרשת", "Action Needed", "Требуется действие");
            _statusBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(rec.GlowColorHex));

            // Sound chime triggers ONCE on new alert transition
            bool isNewAlert = (rec.Type != _lastAlertRecType || !string.Equals(rec.TargetName, _lastAlertTarget, StringComparison.OrdinalIgnoreCase));
            if (isNewAlert)
            {
                _lastAlertRecType = rec.Type;
                _lastAlertTarget = rec.TargetName;
                if (SoundEnabled && (DateTime.Now - _lastSoundTime).TotalSeconds > 2.0)
                {
                    _lastSoundTime = DateTime.Now;
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
            }
        }

        private void RenderStandbyState()
        {
            _lastAlertRecType = RecommendationType.None;
            _lastAlertTarget = "";

            // Hide the skill combo buttons completely when player is healthy!
            _comboBarPanel.Visibility = Visibility.Collapsed;
            _targetPill.Visibility = Visibility.Visible;

            string extra = "";
            if (_lastPartyData != null && _lastPartyData.Count > 0)
            {
                PartyMemberState self = _lastPartyData.Find(p => p.IsSelf) ?? _lastPartyData[0];
                if (self != null && self.MaxHp > 0)
                    extra = " (" + self.Hp.ToString("N0") + " / " + self.MaxHp.ToString("N0") + ")";
            }

            _targetPillText.Text = Tr("● בריא ויציב · מוכן לפעולה" + extra, "● Healthy & Stable · Ready" + extra, "● Все здоровы · Готов к бою" + extra);
            _targetPillText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));

            _statusBadge.Text = "● " + Tr("מוכן", "Ready", "Готов");
            _statusBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));

            if (SoloMode || _lastPartyData == null || _lastPartyData.Count <= 1)
            {
                _partyScrollViewer.Visibility = Visibility.Collapsed;
                Height = 76;
            }
        }

        private void RenderPartyFrames(List<PartyMemberState> members)
        {
            _partyListPanel.Children.Clear();
            if (SoloMode || members == null || members.Count <= 1)
            {
                // Solo mode: Auto collapse party unit frames to keep HUD compact!
                _partyScrollViewer.Visibility = Visibility.Collapsed;
                Height = (_comboBarPanel.Visibility == Visibility.Visible) ? 145 : 76;
                return;
            }

            _partyScrollViewer.Visibility = Visibility.Visible;
            Height = Math.Min(260, (_comboBarPanel.Visibility == Visibility.Visible ? 145 : 76) + (members.Count * 26));

            foreach (PartyMemberState p in members)
            {
                Border row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(5, 2, 5, 2)
                };

                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); // Name
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // HP Bar
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) }); // Pct

                string prefix = p.IsSelf ? "[YOU] " : "";
                TextBlock nameTxt = new TextBlock
                {
                    Text = prefix + p.Name,
                    FontSize = 9.5,
                    FontWeight = p.IsSelf ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = p.IsSelf ? new SolidColorBrush(Color.FromRgb(250, 204, 21)) : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameTxt, 0);
                g.Children.Add(nameTxt);

                // Progress Bar
                Border barContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    CornerRadius = new CornerRadius(3),
                    Height = 10,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid barGrid = new Grid();
                Border hpFill = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(0, (p.HpPercent / 100.0) * 120)
                };

                Color barCol = Color.FromRgb(34, 197, 94); // Green
                if (p.HpPercent <= EmergencyThreshold) barCol = Color.FromRgb(239, 68, 68); // Red
                else if (p.HpPercent <= NormalThreshold) barCol = Color.FromRgb(234, 179, 8); // Yellow
                hpFill.Background = new SolidColorBrush(barCol);

                barGrid.Children.Add(hpFill);
                barContainer.Child = barGrid;
                Grid.SetColumn(barContainer, 1);
                g.Children.Add(barContainer);

                // Percentage text
                TextBlock pctTxt = new TextBlock
                {
                    Text = p.IsDead ? "DEAD" : (Math.Round(p.HpPercent) + "%"),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = p.IsDead ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(pctTxt, 2);
                g.Children.Add(pctTxt);

                row.Child = g;
                _partyListPanel.Children.Add(row);
            }
        }

        private string Tr(string heb, string eng, string rus)
        {
            if (_lang == "ENG") return eng;
            if (_lang == "RUS") return rus;
            return heb;
        }
    }

    public class PetsOverlayWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        private OverlayWindow _parent;
        private string _lang = "HEB";
        private bool _isClickThrough = false;

        private string _activeView = "pets"; // "pets" or "craft"
        private string _scopeFilter = "area"; // "area", "around", "all"
        private string _petFilter = "missing"; // "missing", "recommended", "all"
        public string PlayerClass = "Cleric";

        private Border _rootBorder;
        private TextBlock _titleText;
        private Button _lockBtn;
        private Button _tabPetsBtn;
        private Button _tabCraftBtn;

        // Scope chips
        private Button _chipAreaBtn;
        private Button _chipAroundBtn;
        private Button _chipAllScopeBtn;

        // Status chips
        private Button _chipMissingBtn;
        private Button _chipRecBtn;
        private Button _chipAllPetsBtn;

        private StackPanel _scopeChipsPanel;
        private StackPanel _statusChipsPanel;
        private Border _summaryBanner;
        private TextBlock _summaryText;
        private ScrollViewer _contentScroll;
        private StackPanel _itemsPanel;

        private Dictionary<string, object> _latestData;
        private Dictionary<string, object> _petsDb;
        private List<CraftWishlistItem> _craftWishlist = new List<CraftWishlistItem>();
        private readonly Dictionary<int, ImageSource> _iconCache = new Dictionary<int, ImageSource>();
        private readonly HashSet<int> _iconDownloading = new HashSet<int>();

        public PetsOverlayWindow(OverlayWindow parent)
        {
            _parent = parent;

            Title = "Aion 2 · Pets & Crafting HUD";
            Width = 350;
            Height = 460;
            MinWidth = 280;
            MinHeight = 200;
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
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            };
            WindowChrome.SetWindowChrome(this, chrome);

            BuildUI();

            LocationChanged += delegate(object s, EventArgs e)
            {
                if (_parent != null) _parent.OnOverlayMoved("pets", Left, Top, Width, Height);
            };
            SizeChanged += delegate(object s, SizeChangedEventArgs e)
            {
                if (_parent != null) _parent.OnOverlayMoved("pets", Left, Top, Width, Height);
            };
        }

        public void SetClickThrough(bool enable)
        {
            _isClickThrough = enable;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (enable)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                    if (_lockBtn != null) _lockBtn.Content = "🔒";
                }
                else
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    if (_lockBtn != null) _lockBtn.Content = "🔓";
                }
            }
        }

        private void BuildUI()
        {
            _rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 11, 15, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 56, 189, 248)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 6, 8, 6),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(56, 189, 248),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.45
                }
            };
            _rootBorder.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Tabs (Pets vs Craft)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Scope Chips Row
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Status Filter Chips Row
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Summary Banner
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 5: Content List

            // --- 0. Header Bar ---
            Border headerBar = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(2, 0, 2, 4),
                Cursor = Cursors.SizeAll
            };

            DockPanel headerDock = new DockPanel { LastChildFill = true };

            StackPanel leftHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock iconBadge = new TextBlock
            {
                Text = "🐾",
                FontSize = 13,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(iconBadge);

            _titleText = new TextBlock
            {
                Text = "מעקב חיות וקראפט",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(_titleText);

            headerDock.Children.Add(leftHeader);
            DockPanel.SetDock(leftHeader, Dock.Left);

            StackPanel headerControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _lockBtn = CreateHeaderSmallBtn("🔓", "נעילת עכבר / מעבר חופשי (F10)", delegate(object s, RoutedEventArgs e)
            {
                SetClickThrough(!_isClickThrough);
            });
            headerControls.Children.Add(_lockBtn);

            Button closeBtn = CreateHeaderSmallBtn("✕", "מזער", delegate(object s, RoutedEventArgs e)
            {
                Hide();
                if (_parent != null) _parent.OnOverlayVisibilityChanged("pets", false);
            });
            headerControls.Children.Add(closeBtn);

            headerDock.Children.Add(headerControls);
            DockPanel.SetDock(headerControls, Dock.Right);

            headerBar.Child = headerDock;
            Grid.SetRow(headerBar, 0);
            mainGrid.Children.Add(headerBar);

            // --- 1. Mode Tabs (Pets vs Craft) ---
            UniformGrid tabsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };

            _tabPetsBtn = CreateViewTabBtn("🐾 חיות מחמד", true, delegate(object s, RoutedEventArgs e)
            {
                _activeView = "pets";
                UpdateTabVisuals();
                RefreshList();
            });
            tabsGrid.Children.Add(_tabPetsBtn);

            _tabCraftBtn = CreateViewTabBtn("⚒️ קראפטינג", false, delegate(object s, RoutedEventArgs e)
            {
                _activeView = "craft";
                UpdateTabVisuals();
                RefreshList();
            });
            tabsGrid.Children.Add(_tabCraftBtn);

            Grid.SetRow(tabsGrid, 1);
            mainGrid.Children.Add(tabsGrid);

            // --- 2. Scope Chips (Area / Around / All) ---
            _scopeChipsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 2) };

            _chipAreaBtn = CreateFilterChipBtn("📍 באזור", true, delegate(object s, RoutedEventArgs e)
            {
                _scopeFilter = "area";
                UpdateChipVisuals();
                RefreshList();
            });
            _scopeChipsPanel.Children.Add(_chipAreaBtn);

            _chipAroundBtn = CreateFilterChipBtn("🎯 סביבי", false, delegate(object s, RoutedEventArgs e)
            {
                _scopeFilter = "around";
                UpdateChipVisuals();
                RefreshList();
            });
            _scopeChipsPanel.Children.Add(_chipAroundBtn);

            _chipAllScopeBtn = CreateFilterChipBtn("🗺️ כל המפה", false, delegate(object s, RoutedEventArgs e)
            {
                _scopeFilter = "all";
                UpdateChipVisuals();
                RefreshList();
            });
            _scopeChipsPanel.Children.Add(_chipAllScopeBtn);

            Grid.SetRow(_scopeChipsPanel, 2);
            mainGrid.Children.Add(_scopeChipsPanel);

            // --- 3. Status Filter Chips (Missing / Recommended / All) ---
            _statusChipsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 4) };

            _chipMissingBtn = CreateFilterChipBtn("⏳ רק חסר", true, delegate(object s, RoutedEventArgs e)
            {
                _petFilter = "missing";
                UpdateChipVisuals();
                RefreshList();
            });
            _statusChipsPanel.Children.Add(_chipMissingBtn);

            _chipRecBtn = CreateFilterChipBtn("⭐ מומלץ לקלאס", false, delegate(object s, RoutedEventArgs e)
            {
                _petFilter = "recommended";
                UpdateChipVisuals();
                RefreshList();
            });
            _statusChipsPanel.Children.Add(_chipRecBtn);

            _chipAllPetsBtn = CreateFilterChipBtn("📋 הכל", false, delegate(object s, RoutedEventArgs e)
            {
                _petFilter = "all";
                UpdateChipVisuals();
                RefreshList();
            });
            _statusChipsPanel.Children.Add(_chipAllPetsBtn);

            Grid.SetRow(_statusChipsPanel, 3);
            mainGrid.Children.Add(_statusChipsPanel);

            // --- 4. Summary Banner (כמה מתוך כמה חסר באזור) ---
            _summaryBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 4)
            };
            _summaryText = new TextBlock
            {
                Text = "...",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _summaryBanner.Child = _summaryText;
            Grid.SetRow(_summaryBanner, 4);
            mainGrid.Children.Add(_summaryBanner);

            // --- 5. Items List ---
            _contentScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 2, 0, 0)
            };
            _itemsPanel = new StackPanel { Orientation = Orientation.Vertical };
            _contentScroll.Content = _itemsPanel;

            Grid.SetRow(_contentScroll, 5);
            mainGrid.Children.Add(_contentScroll);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;

            UpdateChipVisuals();
        }

        private Button CreateHeaderSmallBtn(string text, string tooltip, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                Width = 22,
                Height = 20,
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            btn.Click += onClick;
            return btn;
        }

        private Button CreateViewTabBtn(string text, bool isActive, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                Height = 24,
                Margin = new Thickness(1),
                Background = isActive ? new SolidColorBrush(Color.FromArgb(140, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private Button CreateFilterChipBtn(string text, bool isActive, RoutedEventHandler onClick)
        {
            Button btn = new Button
            {
                Content = text,
                Height = 20,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(0, 0, 4, 0),
                Background = isActive ? new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                Foreground = isActive ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : Brushes.Transparent,
                BorderThickness = new Thickness(isActive ? 1 : 0),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private void UpdateTabVisuals()
        {
            bool isPets = (_activeView == "pets");
            _tabPetsBtn.Background = isPets ? new SolidColorBrush(Color.FromArgb(140, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            _tabPetsBtn.Foreground = isPets ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            _tabCraftBtn.Background = !isPets ? new SolidColorBrush(Color.FromArgb(140, 234, 179, 8)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            _tabCraftBtn.Foreground = !isPets ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            _scopeChipsPanel.Visibility = isPets ? Visibility.Visible : Visibility.Collapsed;
            _statusChipsPanel.Visibility = isPets ? Visibility.Visible : Visibility.Collapsed;
            if (_summaryBanner != null) _summaryBanner.Visibility = isPets ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateChipVisuals()
        {
            SetChipActive(_chipAreaBtn, _scopeFilter == "area", Color.FromRgb(56, 189, 248));
            SetChipActive(_chipAroundBtn, _scopeFilter == "around", Color.FromRgb(56, 189, 248));
            SetChipActive(_chipAllScopeBtn, _scopeFilter == "all", Color.FromRgb(56, 189, 248));

            SetChipActive(_chipMissingBtn, _petFilter == "missing", Color.FromRgb(52, 211, 153));
            SetChipActive(_chipRecBtn, _petFilter == "recommended", Color.FromRgb(168, 85, 247));
            SetChipActive(_chipAllPetsBtn, _petFilter == "all", Color.FromRgb(148, 163, 184));
        }

        private void SetChipActive(Button btn, bool isActive, Color activeCol)
        {
            if (btn == null) return;
            btn.Background = isActive ? new SolidColorBrush(Color.FromArgb(90, activeCol.R, activeCol.G, activeCol.B)) : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            btn.Foreground = isActive ? new SolidColorBrush(activeCol) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            btn.BorderBrush = isActive ? new SolidColorBrush(activeCol) : Brushes.Transparent;
            btn.BorderThickness = new Thickness(isActive ? 1 : 0);
        }

        public void SetFilters(string scope, string petFilter)
        {
            if (!string.IsNullOrEmpty(scope)) _scopeFilter = scope;
            if (!string.IsNullOrEmpty(petFilter)) _petFilter = petFilter;
            UpdateChipVisuals();
            RefreshList();
        }

        public string GetScopeFilter() { return _scopeFilter; }
        public string GetPetFilter() { return _petFilter; }

        public void SetLanguage(string lang)
        {
            _lang = lang;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            if (_titleText != null)
                _titleText.Text = Tr("מעקב חיות וקראפט", "Pets & Crafting HUD", "Оверлей питомцев и крафта");

            _tabPetsBtn.Content = Tr("🐾 חיות מחמד", "🐾 Pets", "🐾 Питомцы");
            _tabCraftBtn.Content = Tr("⚒️ קראפטינג", "⚒️ Crafting", "⚒️ Крафт");

            _chipAreaBtn.Content = Tr("📍 באזור", "📍 In Zone", "📍 В зоне");
            _chipAroundBtn.Content = Tr("🎯 סביבי", "🎯 Nearby", "🎯 Рядом");
            _chipAllScopeBtn.Content = Tr("🗺️ כל המפה", "🗺️ All Map", "🗺️ Вся карта");

            _chipMissingBtn.Content = Tr("⏳ רק חסר", "⏳ Missing", "⏳ Только нужные");
            _chipRecBtn.Content = Tr("⭐ מומלץ לקלאס", "⭐ Class Rec", "⭐ Для класса");
            _chipAllPetsBtn.Content = Tr("📋 הכל", "📋 All", "📋 Все");

            RefreshList();
        }

        public void UpdateState(Dictionary<string, object> data, Dictionary<string, object> petsDb, List<CraftWishlistItem> wishlist)
        {
            _latestData = data;
            _petsDb = petsDb;
            _craftWishlist = wishlist;
            RefreshList();
        }

        private void RefreshList()
        {
            _itemsPanel.Children.Clear();
            if (_activeView == "craft")
            {
                RenderCraftingWishlist();
            }
            else
            {
                RenderPetsList();
            }
        }

        public static bool IsRecommendedGenusForClass(string genus, string cls)
        {
            if (string.IsNullOrEmpty(genus)) return false;
            string g = genus.ToLowerInvariant().Trim();
            string c = (cls ?? "cleric").ToLowerInvariant().Trim();

            if (c.Contains("cleric") || c.Contains("priest"))
            {
                return g.Contains("intellect") || g.Contains("trans");
            }
            if (c.Contains("sorc") || c.Contains("mage"))
            {
                return g.Contains("intellect");
            }
            if (c.Contains("spirit") || c.Contains("sm"))
            {
                return g.Contains("intellect") || g.Contains("nature");
            }
            if (c.Contains("glad") || c.Contains("warrior") || c.Contains("templar"))
            {
                return g.Contains("feral") || g.Contains("beast") || g.Contains("trans");
            }
            if (c.Contains("rang") || c.Contains("scout") || c.Contains("sin") || c.Contains("assassin"))
            {
                return g.Contains("feral") || g.Contains("beast") || g.Contains("intellect");
            }
            if (c.Contains("chant"))
            {
                return g.Contains("feral") || g.Contains("trans") || g.Contains("intellect");
            }
            return g.Contains("intellect") || g.Contains("trans");
        }

        private void RenderPetsList()
        {
            if (_latestData == null) return;

            ArrayList rawList = null;
            if (_scopeFilter == "around" && _latestData.ContainsKey("around") && _latestData["around"] is ArrayList)
                rawList = _latestData["around"] as ArrayList;
            else if (_scopeFilter == "area" && _latestData.ContainsKey("area") && _latestData["area"] is ArrayList)
                rawList = _latestData["area"] as ArrayList;
            else if (_latestData.ContainsKey("pets") && _latestData["pets"] is ArrayList)
                rawList = _latestData["pets"] as ArrayList;
            else if (_latestData.ContainsKey("all") && _latestData["all"] is ArrayList)
                rawList = _latestData["all"] as ArrayList;

            if (rawList == null) return;

            List<Dictionary<string, object>> candidates = new List<Dictionary<string, object>>();
            foreach (object o in rawList)
            {
                Dictionary<string, object> pet = o as Dictionary<string, object>;
                if (pet == null) continue;

                // Scope filtering
                if (_scopeFilter == "around")
                {
                    int aroundCount = pet.ContainsKey("aroundCount") ? Convert.ToInt32(pet["aroundCount"]) : 0;
                    double dist = pet.ContainsKey("nearestDistanceM") && pet["nearestDistanceM"] != null ? Convert.ToDouble(pet["nearestDistanceM"]) : (pet.ContainsKey("distMeters") && pet["distMeters"] != null ? Convert.ToDouble(pet["distMeters"]) : 99999.0);
                    if (aroundCount == 0 && dist > 150.0) continue;
                }
                else if (_scopeFilter == "area")
                {
                    int spawns = pet.ContainsKey("spawnsInCurrentArea") ? Convert.ToInt32(pet["spawnsInCurrentArea"]) : 0;
                    int around = pet.ContainsKey("aroundCount") ? Convert.ToInt32(pet["aroundCount"]) : 0;
                    if (spawns == 0 && around == 0) continue;
                }

                // Status filtering
                bool isLv3 = pet.ContainsKey("isLevel3") && Convert.ToBoolean(pet["isLevel3"]);
                int remTo3 = pet.ContainsKey("remainingToLevel3") ? Convert.ToInt32(pet["remainingToLevel3"]) : 
                             (pet.ContainsKey("remainingToLv3") ? Convert.ToInt32(pet["remainingToLv3"]) : 0);
                int curLvl = pet.ContainsKey("level") ? Convert.ToInt32(pet["level"]) : 0;
                if (curLvl >= 3) isLv3 = true;

                if (_petFilter == "missing")
                {
                    // Only pets that still need souls towards Lv.3! Hide MAX pets completely!
                    if (isLv3 || remTo3 <= 0) continue;
                }
                else if (_petFilter == "recommended")
                {
                    // Only pets matching class genus recommendation and not yet Lv.3!
                    string genus = pet.ContainsKey("genus") && pet["genus"] != null ? pet["genus"].ToString() : "";
                    if (!IsRecommendedGenusForClass(genus, PlayerClass)) continue;
                    if (isLv3 || remTo3 <= 0) continue;
                }
                // "all" includes everything

                candidates.Add(pet);
            }

            // Sort by distance first, then by remaining souls
            candidates.Sort(delegate(Dictionary<string, object> a, Dictionary<string, object> b) {
                double da = a.ContainsKey("nearestDistanceM") && a["nearestDistanceM"] != null ? Convert.ToDouble(a["nearestDistanceM"]) : 99999.0;
                double db = b.ContainsKey("nearestDistanceM") && b["nearestDistanceM"] != null ? Convert.ToDouble(b["nearestDistanceM"]) : 99999.0;
                int cmp = da.CompareTo(db);
                if (cmp != 0) return cmp;
                int ra = a.ContainsKey("remainingToLevel3") ? Convert.ToInt32(a["remainingToLevel3"]) : 0;
                int rb = b.ContainsKey("remainingToLevel3") ? Convert.ToInt32(b["remainingToLevel3"]) : 0;
                return ra.CompareTo(rb);
            });

            // Calculate and display scope summary (כמה מתוך כמה חסר)
            int areaTotalCount = 0;
            int areaMissingCount = 0;
            int areaMissingSouls = 0;

            ArrayList areaList = (_latestData.ContainsKey("area") && _latestData["area"] is ArrayList) ? _latestData["area"] as ArrayList : null;
            if (areaList != null && areaList.Count > 0)
            {
                areaTotalCount = areaList.Count;
                foreach (object o in areaList)
                {
                    Dictionary<string, object> p = o as Dictionary<string, object>;
                    if (p == null) continue;
                    bool isL3 = p.ContainsKey("isLevel3") && Convert.ToBoolean(p["isLevel3"]);
                    int rem = p.ContainsKey("remainingToLevel3") ? Convert.ToInt32(p["remainingToLevel3"]) : 0;
                    int curL = p.ContainsKey("level") ? Convert.ToInt32(p["level"]) : 0;
                    if (!isL3 && curL < 3 && rem > 0)
                    {
                        areaMissingCount++;
                        areaMissingSouls += rem;
                    }
                }
            }
            else if (_latestData.ContainsKey("summary") && _latestData["summary"] is Dictionary<string, object>)
            {
                var sum = _latestData["summary"] as Dictionary<string, object>;
                areaMissingCount = sum.ContainsKey("missingPetsInCurrentArea") ? Convert.ToInt32(sum["missingPetsInCurrentArea"]) : 0;
                areaMissingSouls = sum.ContainsKey("missingSoulsCurrentArea") ? Convert.ToInt32(sum["missingSoulsCurrentArea"]) : 0;
                areaTotalCount = sum.ContainsKey("inCurrentAreaPetsCount") ? Convert.ToInt32(sum["inCurrentAreaPetsCount"]) : areaMissingCount;
            }

            if (_summaryText != null)
            {
                if (_scopeFilter == "area")
                {
                    _summaryText.Text = Tr(
                        string.Format("📍 חסר באזור: {0} מתוך {1} חיות · {2} נשמות", areaMissingCount, areaTotalCount, areaMissingSouls),
                        string.Format("📍 In Zone: {0} / {1} pets missing · {2} souls", areaMissingCount, areaTotalCount, areaMissingSouls),
                        string.Format("📍 В зоне: нужно {0} из {1} питомцев · {2} душ", areaMissingCount, areaTotalCount, areaMissingSouls)
                    );
                }
                else if (_scopeFilter == "around")
                {
                    int aroundLiveCount = 0;
                    ArrayList aroundList = (_latestData.ContainsKey("around") && _latestData["around"] is ArrayList) ? _latestData["around"] as ArrayList : null;
                    if (aroundList != null)
                    {
                        foreach (object o in aroundList)
                        {
                            Dictionary<string, object> p = o as Dictionary<string, object>;
                            if (p == null) continue;
                            int ac = p.ContainsKey("aroundCount") ? Convert.ToInt32(p["aroundCount"]) : 0;
                            if (ac > 0) aroundLiveCount++;
                        }
                    }
                    _summaryText.Text = Tr(
                        string.Format("🎯 סביבך: {0} חיות מחמד בטווח קרוב", aroundLiveCount),
                        string.Format("🎯 Nearby: {0} pets in close range", aroundLiveCount),
                        string.Format("🎯 Рядом: {0} питомцев поблизости", aroundLiveCount)
                    );
                }
                else // "all"
                {
                    int mapTotal = 0;
                    int mapMissing = 0;
                    int mapMissingSouls = 0;
                    ArrayList allList = (_latestData.ContainsKey("pets") && _latestData["pets"] is ArrayList) ? _latestData["pets"] as ArrayList : null;
                    if (allList != null)
                    {
                        mapTotal = allList.Count;
                        foreach (object o in allList)
                        {
                            Dictionary<string, object> p = o as Dictionary<string, object>;
                            if (p == null) continue;
                            bool isL3 = p.ContainsKey("isLevel3") && Convert.ToBoolean(p["isLevel3"]);
                            int rem = p.ContainsKey("remainingToLevel3") ? Convert.ToInt32(p["remainingToLevel3"]) : 0;
                            int curL = p.ContainsKey("level") ? Convert.ToInt32(p["level"]) : 0;
                            if (!isL3 && curL < 3 && rem > 0)
                            {
                                mapMissing++;
                                mapMissingSouls += rem;
                            }
                        }
                    }
                    _summaryText.Text = Tr(
                        string.Format("🗺️ בכל המפה: {0} מתוך {1} חיות חסרות · {2} נשמות", mapMissing, mapTotal, mapMissingSouls),
                        string.Format("🗺️ Map Total: {0} / {1} pets missing · {2} souls", mapMissing, mapTotal, mapMissingSouls),
                        string.Format("🗺️ Вся карта: нужно {0} из {1} · {2} душ", mapMissing, mapTotal, mapMissingSouls)
                    );
                }
            }

            if (candidates.Count == 0)
            {
                string emptyMsg = (_petFilter == "missing") 
                    ? Tr("כל החיות באזור זה כבר בדרגה 3 (MAX)!", "All pets in this zone are Lv.3 MAX!", "Все питомцы в зоне достигли 3 ур. (MAX)!")
                    : Tr("אין חיות התואמות לסינון הנוכחי", "No pets match the current filter", "Нет питомцев под этот фильтр");
                TextBlock emptyTxt = new TextBlock
                {
                    Text = emptyMsg,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(12, 30, 12, 0)
                };
                _itemsPanel.Children.Add(emptyTxt);
                return;
            }

            int count = 0;
            foreach (Dictionary<string, object> pet in candidates)
            {
                if (count++ >= 35) break;
                Border row = CreatePetRow(pet);
                _itemsPanel.Children.Add(row);
            }
        }

        public static Color GetGenusColor(string genus)
        {
            if (string.IsNullOrEmpty(genus)) return Color.FromRgb(56, 189, 248);
            string g = genus.ToLowerInvariant().Trim();
            if (g.Contains("feral") || g.Contains("beast")) return Color.FromRgb(249, 115, 22);     // Amber / Orange (#F97316)
            if (g.Contains("intellect")) return Color.FromRgb(96, 165, 250); // Sky Blue (#60A5FA)
            if (g.Contains("nature") || g.Contains("plant")) return Color.FromRgb(74, 222, 128);    // Emerald Green (#4ADE80)
            if (g.Contains("trans")) return Color.FromRgb(192, 132, 252);    // Purple / Violet (#C084FC)
            if (g.Contains("special")) return Color.FromRgb(250, 204, 21);   // Gold / Yellow (#FACC15)
            return Color.FromRgb(56, 189, 248);
        }

        private ImageSource LoadPetIcon(int petId, string iconUrl)
        {
            if (petId <= 0) return null;
            if (_iconCache.ContainsKey(petId)) return _iconCache[petId];

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string webpPath = System.IO.Path.Combine(baseDir, "data", "icons", petId + ".webp");
            string pngPath = System.IO.Path.Combine(baseDir, "data", "icons", petId + ".png");

            string fileToLoad = null;
            if (File.Exists(webpPath)) fileToLoad = webpPath;
            else if (File.Exists(pngPath)) fileToLoad = pngPath;

            if (fileToLoad != null)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(fileToLoad);
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
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        using (WebClient wc = new WebClient())
                        {
                            byte[] dlBytes = wc.DownloadData(iconUrl);
                            try
                            {
                                string iconsDir = System.IO.Path.Combine(baseDir, "data", "icons");
                                if (!Directory.Exists(iconsDir)) Directory.CreateDirectory(iconsDir);
                                File.WriteAllBytes(webpPath, dlBytes);
                            }
                            catch { }

                            Dispatcher.Invoke(delegate
                            {
                                try
                                {
                                    using (MemoryStream ms = new MemoryStream(dlBytes))
                                    {
                                        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                        if (dec.Frames.Count > 0)
                                        {
                                            BitmapSource frame = dec.Frames[0];
                                            frame.Freeze();
                                            _iconCache[petId] = frame;
                                            RefreshList();
                                        }
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    catch { }
                });
            }

            return null;
        }

        private Border CreatePetRow(Dictionary<string, object> pet)
        {
            int petId = pet.ContainsKey("id") ? Convert.ToInt32(pet["id"]) : (pet.ContainsKey("petId") ? Convert.ToInt32(pet["petId"]) : 0);
            string name = pet.ContainsKey("name") && pet["name"] != null ? pet["name"].ToString() : ("Pet " + petId);
            string primaryMonster = pet.ContainsKey("primaryMonsterName") && pet["primaryMonsterName"] != null ? pet["primaryMonsterName"].ToString() : "";
            string iconUrl = pet.ContainsKey("icon") && pet["icon"] != null ? pet["icon"].ToString() : "";
            string genus = pet.ContainsKey("genus") && pet["genus"] != null ? pet["genus"].ToString() : "special";
            int level = pet.ContainsKey("level") ? Convert.ToInt32(pet["level"]) : 0;
            int remTo3 = pet.ContainsKey("remainingToLevel3") ? Convert.ToInt32(pet["remainingToLevel3"]) : 0;
            bool isLv3 = pet.ContainsKey("isLevel3") && Convert.ToBoolean(pet["isLevel3"]);
            if (level >= 3) isLv3 = true;

            int soulsInStep = pet.ContainsKey("soulsInStep") ? Convert.ToInt32(pet["soulsInStep"]) : 0;
            int nextStepNeed = pet.ContainsKey("nextStepNeed") ? Convert.ToInt32(pet["nextStepNeed"]) : 25;
            int totalHaveToward3 = pet.ContainsKey("totalHaveToward3") ? Convert.ToInt32(pet["totalHaveToward3"]) : Math.Max(0, 105 - remTo3);
            int totalRequiredFor3 = pet.ContainsKey("totalRequiredFor3") ? Convert.ToInt32(pet["totalRequiredFor3"]) : 105;
            if (isLv3) { totalHaveToward3 = 105; remTo3 = 0; }
            double stepFill = pet.ContainsKey("stepFillRatio") ? Convert.ToDouble(pet["stepFillRatio"]) : 0.0;
            int killsCount = pet.ContainsKey("killsCount") ? Convert.ToInt32(pet["killsCount"]) : 0;

            double dist = pet.ContainsKey("nearestDistanceM") && pet["nearestDistanceM"] != null ? Convert.ToDouble(pet["nearestDistanceM"]) : (pet.ContainsKey("distMeters") && pet["distMeters"] != null ? Convert.ToDouble(pet["distMeters"]) : 99999.0);
            string arrow = pet.ContainsKey("directionArrow") && pet["directionArrow"] != null ? pet["directionArrow"].ToString() : "→";
            int aroundCount = pet.ContainsKey("aroundCount") ? Convert.ToInt32(pet["aroundCount"]) : 0;

            Color genusCol = GetGenusColor(genus);

            Border row = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, genusCol.R, genusCol.G, genusCol.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(5, 4, 5, 4)
            };

            DockPanel dp = new DockPanel { LastChildFill = true };

            // Left: Pet Icon
            Border iconBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, genusCol.R, genusCol.G, genusCol.B)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(10, 14, 23)),
                Margin = new Thickness(0, 0, 7, 0)
            };
            Image img = new Image { Stretch = Stretch.UniformToFill };
            ImageSource src = LoadPetIcon(petId, iconUrl);
            if (src != null) img.Source = src;
            iconBox.Child = img;
            dp.Children.Add(iconBox);
            DockPanel.SetDock(iconBox, Dock.Left);

            // Right: Need Souls & Distance & Progress Fractions
            StackPanel rightCol = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };

            // Top Row: [ 5 🔮 ]  Need 99 (or MAX)
            StackPanel topBadgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            if (aroundCount > 0)
            {
                Border aroundPill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(90, 16, 185, 129)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 0, 3, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                aroundPill.Child = new TextBlock { Text = aroundCount + " 🔮", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)) };
                topBadgeRow.Children.Add(aroundPill);
            }

            if (isLv3)
            {
                topBadgeRow.Children.Add(new TextBlock
                {
                    Text = "MAX",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                topBadgeRow.Children.Add(new TextBlock
                {
                    Text = "Need " + remTo3,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            rightCol.Children.Add(topBadgeRow);

            // Sub Row: 1/25 (6/105)   27m ←
            StackPanel subRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };

            if (isLv3)
            {
                subRow.Children.Add(new TextBlock
                {
                    Text = "105/105",
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                subRow.Children.Add(new TextBlock
                {
                    Text = soulsInStep + "/" + nextStepNeed,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    VerticalAlignment = VerticalAlignment.Center
                });

                subRow.Children.Add(new TextBlock
                {
                    Text = " (" + totalHaveToward3 + "/105)",
                    FontSize = 8.0,
                    FontWeight = FontWeights.Normal,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (dist < 9999.0)
            {
                subRow.Children.Add(new TextBlock
                {
                    Text = "  " + Math.Round(dist) + "m " + arrow,
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (dist <= 60.0) ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            rightCol.Children.Add(subRow);

            dp.Children.Add(rightCol);
            DockPanel.SetDock(rightCol, Dock.Right);

            // Center: Name, Diamonds, Genus Tag, Progress Bar
            StackPanel centerCol = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };

            TextBlock titleTb = new TextBlock
            {
                Text = string.IsNullOrEmpty(primaryMonster) ? name : (name + " (" + primaryMonster + ")"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180
            };
            centerCol.Children.Add(titleTb);

            // Level Diamonds + Genus Soul Stone Badge
            StackPanel metaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 2) };

            string diamonds = "";
            for (int i = 0; i < 3; i++) diamonds += (i < level) ? "◆" : "◇";
            metaRow.Children.Add(new TextBlock
            {
                Text = diamonds + " Lv." + level,
                FontSize = 9.0,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21)),
                Margin = new Thickness(0, 0, 5, 0)
            });

            Border genusBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, genusCol.R, genusCol.G, genusCol.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, genusCol.R, genusCol.G, genusCol.B)),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 0, 3, 0)
            };
            genusBadge.Child = new TextBlock
            {
                Text = genus.ToUpperInvariant(),
                FontSize = 7.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(genusCol)
            };
            metaRow.Children.Add(genusBadge);

            // Step fraction badge right in meta row!
            string stepFraction = isLv3 ? "MAX" : (soulsInStep + "/" + nextStepNeed);
            metaRow.Children.Add(new TextBlock
            {
                Text = "  " + stepFraction,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = isLv3 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                VerticalAlignment = VerticalAlignment.Center
            });

            centerCol.Children.Add(metaRow);

            // Progress Bar to Next Level
            Border progBar = new Border
            {
                Height = 3.5,
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 1, 0, 0)
            };
            Border progFill = new Border
            {
                Height = 3.5,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(0, Math.Min(1, stepFill) * 140),
                Background = new SolidColorBrush(genusCol),
                CornerRadius = new CornerRadius(2)
            };
            progBar.Child = progFill;
            centerCol.Children.Add(progBar);

            dp.Children.Add(centerCol);
            row.Child = dp;

            row.ToolTip = string.Format(
                "{0} ({1})\nLv.{2} · {3}/{4} souls in level · Total: {5}/105 toward Lv.3 · Need {6} souls\nDistance: {7}",
                name, primaryMonster, level, soulsInStep, nextStepNeed, totalHaveToward3, remTo3,
                (dist < 9999.0 ? (Math.Round(dist) + "m " + arrow) : "-")
            );
            return row;
        }

        private void RenderCraftingWishlist()
        {
            if (_craftWishlist == null || _craftWishlist.Count == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = Tr("רשימת המשאלות ריקה.\nהוסף פריטים ב-Aion 2 Studio (Alt+E)", "Wishlist is empty.\nAdd items in Aion 2 Studio (Alt+E)", "Список пуст.\nДобавьте в Студии (Alt+E)"),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 30, 0, 0)
                };
                _itemsPanel.Children.Add(empty);
                return;
            }

            foreach (CraftWishlistItem item in _craftWishlist)
            {
                Border row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(100, 234, 179, 8)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 3),
                    Padding = new Thickness(6, 4, 6, 4)
                };

                DockPanel dp = new DockPanel { LastChildFill = true };

                TextBlock counts = new TextBlock
                {
                    Text = item.HaveCount + " / " + item.TargetCount,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (item.HaveCount >= item.TargetCount) ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(250, 204, 21)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                dp.Children.Add(counts);
                DockPanel.SetDock(counts, Dock.Right);

                StackPanel info = new StackPanel { Orientation = Orientation.Vertical };
                info.Children.Add(new TextBlock { Text = item.Name, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)) });
                info.Children.Add(new TextBlock { Text = item.Profession, FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)) });
                dp.Children.Add(info);

                row.Child = dp;
                _itemsPanel.Children.Add(row);
            }
        }

        private string Tr(string heb, string eng, string rus)
        {
            if (_lang == "ENG") return eng;
            if (_lang == "RUS") return rus;
            return heb;
        }
    }

    public class OverlayWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_CLICKTHROUGH = 9001;
        private const int HOTKEY_TOGGLEVIS = 9002;
        private const int HOTKEY_TOGGLEHUB = 9003;
        private const int VK_F9 = 0x78;
        private const int VK_F10 = 0x79;
        private const int MOD_ALT = 0x0001;
        private const int VK_E = 0x45;

        private string _lang = "HEB"; // "HEB", "ENG", "RUS"
        private bool _isClickThroughLocked = false;
        private bool _overlaysVisible = true;
        private double _globalOpacity = 1.0;
        private int _updateRateMs = 250;

        public string ActiveCharacterName = "ManatDam";
        public int ActiveCharacterLevel = 45;
        public string ActiveCharacterClass = "Cleric";

        // Separate Floating Overlay Windows
        private PetsOverlayWindow _petsOverlay;
        private ClericAssistantWindow _clericWindow;
        private CombatLogMonitor _combatLogMonitor;

        private Process _snifferProc;
        private DispatcherTimer _pollTimer;
        private JavaScriptSerializer _json = new JavaScriptSerializer();
        private Dictionary<string, object> _latestData;
        private Dictionary<string, object> _petsDb;
        private readonly List<CraftItem> _craftCatalog = new List<CraftItem>();
        private readonly List<CraftWishlistItem> _craftWishlist = new List<CraftWishlistItem>();

        // Navigation & Views
        private string _activeNavTab = "dashboard";
        private Border _rootBorder;
        private StackPanel _navPanel;
        private ContentControl _centerViewContainer;
        private TextBlock _statusPillText;

        // UI Controls for Cleric Settings
        private Slider _sliderEmergency;
        private Slider _sliderNormal;
                private TextBlock _valEmergency;
        private TextBlock _valNormal;
                private CheckBox _cbDispel;
        private CheckBox _cbRes;
        private CheckBox _cbSound;

        // UI Controls for Crafting
        private StackPanel _craftCatalogContainer;
        private StackPanel _craftWishlistContainer;

        // UI Controls for Pets Database
        private StackPanel _petsBrowserContainer;
        private TextBox _petSearchBox;
        private ComboBox _zoneCombo;
        private string _petGenusFilter = "all";

        public OverlayWindow()
        {
            Title = "Aion 2 Studio · Control Hub & Overlay Manager";
            Width = 980;
            Height = 640;
            MinWidth = 850;
            MinHeight = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = false;

            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10)
            };
            WindowChrome.SetWindowChrome(this, chrome);

            LoadDatabaseFiles();
            LoadSettings();

            BuildUI();

            Loaded += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                RegisterHotKey(hwnd, HOTKEY_CLICKTHROUGH, 0, VK_F10);
                RegisterHotKey(hwnd, HOTKEY_TOGGLEVIS, 0, VK_F9);
                RegisterHotKey(hwnd, HOTKEY_TOGGLEHUB, MOD_ALT, VK_E);

                HwndSource source = HwndSource.FromHwnd(hwnd);
                if (source != null) source.AddHook(HwndHook);
            };

            Closed += OnClosed;

            // Launch Floating Overlays
            _petsOverlay = new PetsOverlayWindow(this);
            _petsOverlay.SetLanguage(_lang);
            _petsOverlay.Show();

            _clericWindow = new ClericAssistantWindow(this);
            _clericWindow.SetLanguage(_lang);
            _clericWindow.Show();

            _combatLogMonitor = new CombatLogMonitor();
            _combatLogMonitor.OnPartyUpdate += delegate(List<PartyMemberState> members)
            {
                Dispatcher.Invoke(delegate
                {
                    if (_combatLogMonitor != null)
                    {
                        ActiveCharacterName = _combatLogMonitor.ActiveSelfName;
                        ActiveCharacterLevel = _combatLogMonitor.SelfLevel;
                        ActiveCharacterClass = _combatLogMonitor.SelfClass;
                        UpdateStatusPill();
                    }
                    if (_clericWindow != null) _clericWindow.UpdatePartyState(members);
                });
            };

            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(_updateRateMs);
            _pollTimer.Tick += delegate(object s, EventArgs ev) { PollLiveState(); };
            _pollTimer.Start();

            StartSniffer();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_CLICKTHROUGH);
            UnregisterHotKey(hwnd, HOTKEY_TOGGLEVIS);
            UnregisterHotKey(hwnd, HOTKEY_TOGGLEHUB);

            if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer = null; }
            if (_combatLogMonitor != null) { _combatLogMonitor.Dispose(); _combatLogMonitor = null; }
            if (_petsOverlay != null) _petsOverlay.Close();
            if (_clericWindow != null) _clericWindow.Close();

            SaveSettings();
            StopSniffer();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_CLICKTHROUGH)
                {
                    ToggleClickThroughLock();
                    handled = true;
                }
                else if (id == HOTKEY_TOGGLEVIS)
                {
                    ToggleAllOverlaysVisibility();
                    handled = true;
                }
                else if (id == HOTKEY_TOGGLEHUB)
                {
                    ToggleHubVisibility();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void ToggleHubVisibility()
        {
            if (Visibility == Visibility.Visible) Hide();
            else
            {
                Show();
                Activate();
            }
        }

        public void ToggleAllOverlaysVisibility()
        {
            _overlaysVisible = !_overlaysVisible;
            if (_petsOverlay != null)
            {
                if (_overlaysVisible) _petsOverlay.Show();
                else _petsOverlay.Hide();
            }
            if (_clericWindow != null)
            {
                if (_overlaysVisible) _clericWindow.Show();
                else _clericWindow.Hide();
            }
            if (_activeNavTab == "dashboard") SwitchNavTab("dashboard");
        }

        public void ToggleClickThroughLock()
        {
            _isClickThroughLocked = !_isClickThroughLocked;
            if (_petsOverlay != null) _petsOverlay.SetClickThrough(_isClickThroughLocked);
            if (_activeNavTab == "dashboard") SwitchNavTab("dashboard");
        }

        public void OnOverlayMoved(string overlayId, double left, double top, double w, double h)
        {
            SaveSettings();
        }

        public void OnOverlayVisibilityChanged(string overlayId, bool visible)
        {
            SaveSettings();
            if (_activeNavTab == "dashboard") SwitchNavTab("dashboard");
        }

        private void StartSniffer()
        {
            try
            {
                string snifferPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sniffer.cjs");
                if (File.Exists(snifferPath))
                {
                    string runner = "node";
                    bool isElectron = false;
                    string antigravityExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "antigravity", "Antigravity.exe");
                    if (File.Exists(antigravityExe))
                    {
                        runner = antigravityExe;
                        isElectron = true;
                    }

                    ProcessStartInfo psi = new ProcessStartInfo(runner, "\"" + snifferPath + "\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    };
                    if (isElectron)
                    {
                        psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
                    }
                    _snifferProc = Process.Start(psi);
                }
            }
            catch { }
        }

        private void StopSniffer()
        {
            try
            {
                if (_snifferProc != null && !_snifferProc.HasExited)
                {
                    _snifferProc.Kill();
                    _snifferProc.Dispose();
                    _snifferProc = null;
                }
            }
            catch { }
        }

        private void PollLiveState()
        {
            try
            {
                string statePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "overlay-state.json");
                if (File.Exists(statePath))
                {
                    string txt = File.ReadAllText(statePath);
                    _latestData = _json.Deserialize<Dictionary<string, object>>(txt);

                    if (_petsOverlay != null) _petsOverlay.UpdateState(_latestData, _petsDb, _craftWishlist);

                    // Sync character info
                    if (_latestData.ContainsKey("character"))
                    {
                        Dictionary<string, object> ch = _latestData["character"] as Dictionary<string, object>;
                        if (ch != null)
                        {
                            if (ch.ContainsKey("name") && ch["name"] != null) ActiveCharacterName = ch["name"].ToString();
                            if (ch.ContainsKey("level") && ch["level"] != null)
                            {
                                int lvl;
                                if (int.TryParse(ch["level"].ToString(), out lvl) && lvl > 0) ActiveCharacterLevel = lvl;
                            }
                            if (ch.ContainsKey("class") && ch["class"] != null) ActiveCharacterClass = ch["class"].ToString();
                            UpdateStatusPill();
                        }
                    }

                    // Feed Cleric HUD directly with fresh telemetry in every poll tick!
                    if (_latestData.ContainsKey("cleric") && _clericWindow != null)
                    {
                        Dictionary<string, object> cl = _latestData["cleric"] as Dictionary<string, object>;
                        if (cl != null)
                        {
                            List<PartyMemberState> list = new List<PartyMemberState>();
                            if (cl.ContainsKey("self"))
                            {
                                Dictionary<string, object> sf = cl["self"] as Dictionary<string, object>;
                                if (sf != null)
                                {
                                    int shp = sf.ContainsKey("hp") ? Convert.ToInt32(sf["hp"]) : 12760;
                                    int smax = sf.ContainsKey("maxHp") ? Convert.ToInt32(sf["maxHp"]) : 12760;
                                    string sname = sf.ContainsKey("name") && sf["name"] != null ? sf["name"].ToString() : ActiveCharacterName;
                                    int slvl = sf.ContainsKey("level") ? Convert.ToInt32(sf["level"]) : ActiveCharacterLevel;
                                    long seid = sf.ContainsKey("entity") ? Convert.ToInt64(sf["entity"]) : 10694;

                                    PartyMemberState selfState = new PartyMemberState
                                    {
                                        EntityId = seid,
                                        Name = sname,
                                        ClassName = ActiveCharacterClass,
                                        Level = slvl,
                                        Hp = shp,
                                        MaxHp = smax,
                                        IsSelf = true,
                                        IsDead = (shp <= 0),
                                        LastUpdated = DateTime.Now
                                    };

                                    if (sf.ContainsKey("debuffs") && sf["debuffs"] is ArrayList)
                                    {
                                        foreach (object d in sf["debuffs"] as ArrayList)
                                        {
                                            long did;
                                            if (long.TryParse(d.ToString(), out did)) selfState.Debuffs.Add(did);
                                        }
                                    }
                                    list.Add(selfState);
                                }
                            }

                            if (cl.ContainsKey("party") && cl["party"] is ArrayList)
                            {
                                foreach (object po in cl["party"] as ArrayList)
                                {
                                    Dictionary<string, object> pd = po as Dictionary<string, object>;
                                    if (pd == null) continue;
                                    long pe = pd.ContainsKey("entity") ? Convert.ToInt64(pd["entity"]) : 0;
                                    int php = pd.ContainsKey("hp") ? Convert.ToInt32(pd["hp"]) : 10000;
                                    int pmax = pd.ContainsKey("maxHp") ? Convert.ToInt32(pd["maxHp"]) : 10000;
                                    string pname = pd.ContainsKey("name") && pd["name"] != null ? pd["name"].ToString() : ("Party_" + pe);
                                    bool pdead = pd.ContainsKey("isDead") ? Convert.ToBoolean(pd["isDead"]) : (php <= 0);

                                    PartyMemberState pm = new PartyMemberState
                                    {
                                        EntityId = pe,
                                        Name = pname,
                                        ClassName = "Member",
                                        Level = 45,
                                        Hp = php,
                                        MaxHp = pmax,
                                        IsSelf = false,
                                        IsDead = pdead,
                                        LastUpdated = DateTime.Now
                                    };
                                    if (pd.ContainsKey("debuffs") && pd["debuffs"] is ArrayList)
                                    {
                                        foreach (object d in pd["debuffs"] as ArrayList)
                                        {
                                            long did;
                                            if (long.TryParse(d.ToString(), out did)) pm.Debuffs.Add(did);
                                        }
                                    }
                                    list.Add(pm);
                                }
                            }

                            if (list.Count > 0)
                            {
                                _clericWindow.UpdatePartyState(list);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void BuildUI()
        {
            _rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 13, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 0,
                    Opacity = 0.8
                }
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); // 0: Top Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Body

            // 0. Top Header
            Border topHeader = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 0, 14, 0),
                Cursor = Cursors.SizeAll
            };
            topHeader.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            DockPanel headerDock = new DockPanel { LastChildFill = true };

            StackPanel brandStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock brandIcon = new TextBlock
            {
                Text = "⚡",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            brandStack.Children.Add(brandIcon);

            TextBlock brandTitle = new TextBlock
            {
                Text = "Aion 2 Studio",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            brandStack.Children.Add(brandTitle);

            headerDock.Children.Add(brandStack);
            DockPanel.SetDock(brandStack, Dock.Left);

            // Right Controls: Status Pill, Alt+E, Langs, Window Controls
            StackPanel rightControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border statusPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 16, 185, 129)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusPillText = new TextBlock
            {
                Text = "🟢 Aion 2 is running (" + ActiveCharacterName + " · Lv." + ActiveCharacterLevel + " " + ActiveCharacterClass + ")",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153))
            };
            statusPill.Child = _statusPillText;
            rightControls.Children.Add(statusPill);

            Button minToGameBtn = new Button
            {
                Content = "Alt+E מזער למשחק",
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromArgb(60, 56, 189, 248)),
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            minToGameBtn.Click += delegate(object s, RoutedEventArgs e) { ToggleHubVisibility(); };
            rightControls.Children.Add(minToGameBtn);

            rightControls.Children.Add(CreateLangBtn("עב", "HEB"));
            rightControls.Children.Add(CreateLangBtn("EN", "ENG"));
            rightControls.Children.Add(CreateLangBtn("RU", "RUS"));

            rightControls.Children.Add(CreateWindowCtrlBtn("—", delegate(object s, RoutedEventArgs e) { WindowState = WindowState.Minimized; }));
            rightControls.Children.Add(CreateWindowCtrlBtn("▢", delegate(object s, RoutedEventArgs e) { WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized; }));
            rightControls.Children.Add(CreateWindowCtrlBtn("✕", delegate(object s, RoutedEventArgs e) { ToggleHubVisibility(); }));

            headerDock.Children.Add(rightControls);
            DockPanel.SetDock(rightControls, Dock.Right);

            topHeader.Child = headerDock;
            Grid.SetRow(topHeader, 0);
            mainGrid.Children.Add(topHeader);

            // 1. Studio Body (Left Nav + Center View Container)
            Grid bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); // Left Nav
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Center Content

            // Left Navigation
            Border navBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(12, 16, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(10, 12, 10, 12)
            };

            DockPanel navDock = new DockPanel { LastChildFill = true };

            TextBlock verTxt = new TextBlock
            {
                Text = "v2.9.0-dev · Modern Studio",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(6, 0, 0, 4)
            };
            DockPanel.SetDock(verTxt, Dock.Bottom);
            navDock.Children.Add(verTxt);

            _navPanel = new StackPanel { Orientation = Orientation.Vertical };

            _navPanel.Children.Add(CreateNavButton("🎛️", Tr("מרכז התוספים", "Overlays Dashboard", "Центр оверлеев"), "dashboard"));

            _navPanel.Children.Add(CreateNavCategoryHeader(Tr("הגדרות תוספים", "OVERLAY SETTINGS", "НАСТРОЙКИ ОВЕРЛЕЕВ")));
            _navPanel.Children.Add(CreateNavButton("🐾", Tr("הגדרות חיות וקראפט", "Pets HUD Settings", "Настройки питомцев"), "pets_settings"));
            _navPanel.Children.Add(CreateNavButton("⚚", Tr("הגדרות עוזר קלריק", "Cleric Assistant Settings", "Настройки целителя"), "cleric_settings"));
            _navPanel.Children.Add(CreateNavButton("🗺️", Tr("הגדרות מפה וקוביות", "Map & Radar Settings", "Настройки карты"), "map_settings"));

            _navPanel.Children.Add(CreateNavCategoryHeader(Tr("מאגר מידע במשחק", "GAME DATABASE", "БАЗА ДАННЫХ")));
            _navPanel.Children.Add(CreateNavButton("📚", Tr("מאגר חיות מחמד", "Pets Database", "База питомцев"), "pets_db"));
            _navPanel.Children.Add(CreateNavButton("⚒️", Tr("רשימת קראפט", "Crafting Wishlist", "Список крафта"), "craft_list"));

            _navPanel.Children.Add(CreateNavCategoryHeader(Tr("מערכת", "SYSTEM", "СИСТЕМА")));
            _navPanel.Children.Add(CreateNavButton("⚙️", Tr("קיצורי מקשים ומערכת", "Hotkeys & Settings", "Горячие клавиши"), "settings"));

            navDock.Children.Add(_navPanel);
            navBorder.Child = navDock;
            Grid.SetColumn(navBorder, 0);
            bodyGrid.Children.Add(navBorder);

            // Center View Container
            _centerViewContainer = new ContentControl
            {
                Margin = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(10, 13, 20))
            };
            Grid.SetColumn(_centerViewContainer, 1);
            bodyGrid.Children.Add(_centerViewContainer);

            Grid.SetRow(bodyGrid, 1);
            mainGrid.Children.Add(bodyGrid);

            _rootBorder.Child = mainGrid;
            Content = _rootBorder;

            SwitchNavTab("dashboard");
        }

        private TextBlock CreateNavCategoryHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 9.0,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(8, 12, 0, 4)
            };
        }

        private Button CreateNavButton(string icon, string label, string tabId)
        {
            Button btn = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 0, 8, 0),
                Cursor = Cursors.Hand,
                Tag = tabId
            };

            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock ico = new TextBlock
            {
                Text = icon,
                FontSize = 13,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(ico);

            TextBlock lbl = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(lbl);

            btn.Content = sp;
            btn.Click += delegate(object s, RoutedEventArgs e) { SwitchNavTab(tabId); };
            return btn;
        }

        public void SwitchNavTab(string tabId)
        {
            _activeNavTab = tabId;

            foreach (object child in _navPanel.Children)
            {
                Button b = child as Button;
                if (b != null)
                {
                    bool isActive = (b.Tag != null && b.Tag.ToString() == tabId);
                    b.Background = isActive ? new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)) : Brushes.Transparent;
                    StackPanel sp = b.Content as StackPanel;
                    if (sp != null && sp.Children.Count > 1)
                    {
                        TextBlock txt = sp.Children[1] as TextBlock;
                        if (txt != null)
                            txt.Foreground = isActive ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(203, 213, 225));
                    }
                }
            }

            if (tabId == "dashboard") _centerViewContainer.Content = BuildDashboardView();
            else if (tabId == "pets_settings") _centerViewContainer.Content = BuildPetsSettingsView();
            else if (tabId == "cleric_settings") _centerViewContainer.Content = BuildClericSettingsView();
            else if (tabId == "map_settings") _centerViewContainer.Content = BuildMapSettingsView();
            else if (tabId == "pets_db") _centerViewContainer.Content = BuildPetsBrowserView();
            else if (tabId == "craft_list") _centerViewContainer.Content = BuildCraftingWishlistView();
            else if (tabId == "settings") _centerViewContainer.Content = BuildSettingsView();
        }

        private void UpdateStatusPill()
        {
            if (_statusPillText != null)
            {
                _statusPillText.Text = "🟢 Aion 2 is running (" + ActiveCharacterName + " · Lv." + ActiveCharacterLevel + " " + ActiveCharacterClass + ")";
            }
        }

        // =====================================================================
        // VIEW 1: OVERLAYS DASHBOARD (MODERN HUBCENTER WITH DEDICATED CARDS)
        // =====================================================================
        private FrameworkElement BuildDashboardView()
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical };

            // Header
            StackPanel headerStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 16) };
            headerStack.Children.Add(new TextBlock
            {
                Text = Tr("מרכז התוספים והשליטה (Overlays Hub)", "Overlays Hub & Dashboard", "Центр управления оверлеями"),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = Tr("שליטה מלאה, הגדרות כלליות והתאמה אישית של כל תוסף בנפרד מעל מסך המשחק.", "Manage, customize and configure every overlay individually for Aion 2.", "Управление и индивидуальная настройка каждого оверлея над экраном игры."),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 3, 0, 0)
            });
            sp.Children.Add(headerStack);

            // Global Settings Bar (Glass Card)
            Border globalBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 18)
            };

            Grid gGrid = new Grid();
            gGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Left controls
            gGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Right quick buttons

            StackPanel gLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Global Opacity Slider
            gLeft.Children.Add(new TextBlock
            {
                Text = Tr("שקיפות כללית:", "Global Opacity:", "Общая прозрачность:"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            Slider opSlider = new Slider
            {
                Minimum = 0.20,
                Maximum = 1.0,
                Value = _globalOpacity,
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock opVal = new TextBlock
            {
                Text = Math.Round(_globalOpacity * 100) + "%",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 18, 0)
            };
            opSlider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
            {
                _globalOpacity = e.NewValue;
                opVal.Text = Math.Round(e.NewValue * 100) + "%";
                if (_petsOverlay != null) _petsOverlay.Opacity = _globalOpacity;
                if (_clericWindow != null) _clericWindow.Opacity = _globalOpacity;
            };
            gLeft.Children.Add(opSlider);
            gLeft.Children.Add(opVal);

            // Update Rate selector
            gLeft.Children.Add(new TextBlock
            {
                Text = Tr("קצב רענון:", "Update Rate:", "Частота:"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            int[] rates = new int[] { 100, 250, 500, 1000 };
            foreach (int rate in rates)
            {
                Button rBtn = new Button
                {
                    Content = rate + "ms",
                    Height = 22,
                    Padding = new Thickness(6, 0, 6, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = (_updateRateMs == rate) ? new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    Foreground = (_updateRateMs == rate) ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    BorderThickness = new Thickness(0),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                int rCopy = rate;
                rBtn.Click += delegate
                {
                    _updateRateMs = rCopy;
                    if (_pollTimer != null) _pollTimer.Interval = TimeSpan.FromMilliseconds(rCopy);
                    SwitchNavTab("dashboard");
                };
                gLeft.Children.Add(rBtn);
            }

            gGrid.Children.Add(gLeft);
            Grid.SetColumn(gLeft, 0);

            // Right Global Actions
            StackPanel gRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Button btnToggleAll = CreateActionPillBtn("👁️ " + Tr("הסתר / הצג הכל (F9)", "Toggle All (F9)", "Все оверлеи (F9)"), delegate {
                ToggleAllOverlaysVisibility();
            });
            gRight.Children.Add(btnToggleAll);

            Button btnLockAll = CreateActionPillBtn((_isClickThroughLocked ? "🔒 " : "🔓 ") + Tr("נעילת עכבר (F10)", "Click Lock (F10)", "Блок мыши (F10)"), delegate {
                ToggleClickThroughLock();
            });
            gRight.Children.Add(btnLockAll);

            Button btnResetAll = CreateActionPillBtn("🎯 " + Tr("אפס את כל המיקומים", "Reset All Positions", "Сбросить все позиции"), delegate {
                ResetAllOverlays();
            });
            gRight.Children.Add(btnResetAll);

            gGrid.Children.Add(gRight);
            Grid.SetColumn(gRight, 1);

            globalBar.Child = gGrid;
            sp.Children.Add(globalBar);

            // Overlays Grid (4 Dedicated Overlay Cards)
            UniformGrid cardGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0) };

            // 1. Pets Overlay Card
            string petsSummary = Tr("טווח: " + (_petsOverlay != null ? _petsOverlay.GetScopeFilter() : "באזור") + " · סינון: " + (_petsOverlay != null ? _petsOverlay.GetPetFilter() : "רק חסר"), "Scope: Area · Filter: Missing", "В зоне · Только нужные");
            cardGrid.Children.Add(CreateOverlayModuleCard(
                "🐾",
                Tr("מעקב חיות וקראפט (Pets & Craft HUD)", "Pets & Crafting HUD", "Оверлей питомцев и крафта"),
                Tr("עוקב אחרי חיות סביבך ובאזור, סופר נשמות חסרות לדרגה 3 ומסמן מרחקים וכיוונים.", "Tracks nearby pets, counts missing souls to Lv.3, distance and directions.", "Отслеживание питомцев в зоне, подсчет душ до 3 уровня."),
                petsSummary,
                _petsOverlay != null && _petsOverlay.IsVisible,
                Color.FromRgb(56, 189, 248),
                delegate {
                    if (_petsOverlay != null)
                    {
                        if (_petsOverlay.IsVisible) _petsOverlay.Hide();
                        else _petsOverlay.Show();
                        SwitchNavTab("dashboard");
                    }
                },
                delegate { SwitchNavTab("pets_settings"); },
                delegate {
                    if (_petsOverlay != null) { _petsOverlay.Left = 24; _petsOverlay.Top = 48; _petsOverlay.Show(); }
                }
            ));

            // 2. Cleric Assistant Card
            string clericSummary = Tr("מצב: " + (_clericWindow != null && _clericWindow.SoloMode ? "סולו (רק אותך)" : "קבוצה") + " · סף ריפוי: " + (_clericWindow != null ? _clericWindow.NormalThreshold : 80) + "%", "Mode: Solo · Threshold: 80%", "Режим: Соло · Порог: 80%");
            cardGrid.Children.Add(CreateOverlayModuleCard(
                "⚚",
                Tr("עוזר קלריק וריפוי חכם (Cleric Action Assist)", "Cleric Action Assistant", "Ассистент исцеления целителя"),
                Tr("עוקב אחרי HP ודיבאפים ומציע את סדרת הסקילים המומלצת ללחיצה בזמן אמת.", "Monitors HP and debuffs, recommending the optimal healing skill combo live.", "Мониторинг здоровья и подсказка комбинаций скиллов в реальном времени."),
                clericSummary,
                _clericWindow != null && _clericWindow.IsVisible,
                Color.FromRgb(168, 85, 247),
                delegate {
                    if (_clericWindow != null)
                    {
                        if (_clericWindow.IsVisible) _clericWindow.Hide();
                        else _clericWindow.Show();
                        SwitchNavTab("dashboard");
                    }
                },
                delegate { SwitchNavTab("cleric_settings"); },
                delegate {
                    if (_clericWindow != null) { _clericWindow.Left = 820; _clericWindow.Top = 48; _clericWindow.Show(); }
                }
            ));

            // 3. Mini-Map & Radar Card
            cardGrid.Children.Add(CreateOverlayModuleCard(
                "🗺️",
                Tr("מיני-מפה ורדאר סביבתי (Mini-Map & Radar)", "World Map & Radar", "Карта и радар мира"),
                Tr("מפה שקופה עם קואורדינטות מיקום שחקן, גבולות אזור ורמות מפלצות.", "Transparent map with live player heading, area polygons, and creature levels.", "Прозрачная карта с направлением игрока и уровнями существ."),
                Tr("זום: 1.0x · סימון קוביות: פעיל", "Zoom: 1.0x · Cubes: Active", "Масштаб: 1.0x · Кубы: Вкл"),
                false,
                Color.FromRgb(52, 211, 153),
                delegate { SwitchNavTab("map_settings"); },
                delegate { SwitchNavTab("map_settings"); },
                delegate { }
            ));

            // 4. Hidden Cubes Card
            cardGrid.Children.Add(CreateOverlayModuleCard(
                "📦",
                Tr("מאתר קוביות נסתרות (Hidden Cubes Tracker)", "Hidden Cubes Radar", "Поиск скрытых кубов"),
                Tr("התרעה והכוונה לקוביות הרחבת תיק מוסתרות לפי קואורדינטות מפה מדויקות.", "Alerts and navigational guidance to hidden inventory expansion cubes.", "Поиск и оповещение о скрытых кубах инвентаря на локациях."),
                Tr("סורק 45 קוביות בעולם המשחק", "Scanning 45 cubes worldwide", "Сканирование 45 кубов в мире"),
                true,
                Color.FromRgb(250, 204, 21),
                delegate { SwitchNavTab("map_settings"); },
                delegate { SwitchNavTab("map_settings"); },
                delegate { }
            ));

            sp.Children.Add(cardGrid);
            sv.Content = sp;
            return sv;
        }

        private Border CreateOverlayModuleCard(string icon, string title, string description, string summaryPillText, bool isActive, Color accentColor, Action onToggle, Action onEditSettings, Action onResetPos)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(14, 19, 29)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(isActive ? (byte)180 : (byte)60, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(6),
                Effect = new DropShadowEffect
                {
                    Color = accentColor,
                    BlurRadius = isActive ? 12 : 4,
                    ShadowDepth = 0,
                    Opacity = isActive ? 0.35 : 0.10
                }
            };

            Grid cg = new Grid();
            cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Description
            cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Summary pill
            cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions bar

            // 0. Card Header
            DockPanel ch = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };

            StackPanel leftHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock icoTb = new TextBlock { Text = icon, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            leftHeader.Children.Add(icoTb);

            TextBlock titleTb = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftHeader.Children.Add(titleTb);

            ch.Children.Add(leftHeader);
            DockPanel.SetDock(leftHeader, Dock.Left);

            // Active Badge
            Border badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(isActive ? (byte)60 : (byte)30, isActive ? (byte)52 : (byte)148, isActive ? (byte)211 : (byte)163, isActive ? (byte)153 : (byte)184)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(isActive ? (byte)140 : (byte)60, isActive ? (byte)52 : (byte)148, isActive ? (byte)211 : (byte)163, isActive ? (byte)153 : (byte)184)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = isActive ? Tr("🟢 פעיל", "🟢 Active", "🟢 Активен") : Tr("⚪ כבוי", "⚪ Off", "⚪ Выкл"),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = isActive ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
            ch.Children.Add(badge);
            DockPanel.SetDock(badge, Dock.Right);

            Grid.SetRow(ch, 0);
            cg.Children.Add(ch);

            // 1. Description
            TextBlock descTb = new TextBlock
            {
                Text = description,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                LineHeight = 15
            };
            Grid.SetRow(descTb, 1);
            cg.Children.Add(descTb);

            // 2. Summary Pill
            Border sumPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(120, 10, 14, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 10)
            };
            sumPill.Child = new TextBlock
            {
                Text = summaryPillText,
                FontSize = 10.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225))
            };
            Grid.SetRow(sumPill, 2);
            cg.Children.Add(sumPill);

            // 3. Actions Bar
            DockPanel actDock = new DockPanel { LastChildFill = true };

            StackPanel leftBtns = new StackPanel { Orientation = Orientation.Horizontal };

            Button btnToggle = new Button
            {
                Content = isActive ? Tr("👁️ הסתר תוסף", "👁️ Hide", "👁️ Скрыть") : Tr("👁️ הצג על המסך", "👁️ Show", "👁️ Показать"),
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = isActive ? new SolidColorBrush(Color.FromArgb(60, 239, 68, 68)) : new SolidColorBrush(Color.FromArgb(80, 52, 211, 153)),
                Foreground = isActive ? new SolidColorBrush(Color.FromRgb(252, 165, 165)) : new SolidColorBrush(Color.FromRgb(167, 243, 208)),
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnToggle.Click += delegate { onToggle(); };
            leftBtns.Children.Add(btnToggle);

            Button btnReset = new Button
            {
                Content = "🔄 " + Tr("אפס מיקום", "Reset Pos", "Сброс"),
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0),
                FontSize = 10.0,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btnReset.Click += delegate { onResetPos(); };
            leftBtns.Children.Add(btnReset);

            actDock.Children.Add(leftBtns);
            DockPanel.SetDock(leftBtns, Dock.Left);

            Button btnSettings = new Button
            {
                Content = "⚙️ " + Tr("ערוך הגדרות תוסף", "Edit Settings", "Настройки"),
                Height = 26,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromArgb(100, 56, 189, 248)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnSettings.Click += delegate { onEditSettings(); };
            actDock.Children.Add(btnSettings);
            DockPanel.SetDock(btnSettings, Dock.Right);

            Grid.SetRow(actDock, 3);
            cg.Children.Add(actDock);

            card.Child = cg;
            return card;
        }

        private Button CreateActionPillBtn(string text, Action onClick)
        {
            Button b = new Button
            {
                Content = text,
                Height = 26,
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            b.Click += delegate { onClick(); };
            return b;
        }

        private void ResetAllOverlays()
        {
            if (_petsOverlay != null)
            {
                _petsOverlay.Left = 24;
                _petsOverlay.Top = 48;
                _petsOverlay.Show();
            }
            if (_clericWindow != null)
            {
                _clericWindow.Left = 820;
                _clericWindow.Top = 48;
                _clericWindow.Show();
            }
            SaveSettings();
            if (_activeNavTab == "dashboard") SwitchNavTab("dashboard");
        }

        // =====================================================================
        // VIEW 2: DEDICATED PETS & CRAFTING SETTINGS
        // =====================================================================
        private FrameworkElement BuildPetsSettingsView()
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24) };
            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 750, HorizontalAlignment = HorizontalAlignment.Left };

            // Title
            sp.Children.Add(new TextBlock { Text = Tr("הגדרות מעקב חיות וקראפט (Pets & Craft HUD)", "Pets & Crafting HUD Settings", "Настройки оверлея питомцев"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) });
            sp.Children.Add(new TextBlock { Text = Tr("קבע מה יופיע בתוסף מעל המשחק: סנן לפי חיות שחסרות לדרגה 3, מומלצות לקלאס או הכל.", "Configure in-game pet filters: show missing souls to Lv.3, class recommended, or all.", "Фильтрация питомцев: только нужные до 3 уровня, для вашего класса или все."), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), Margin = new Thickness(0, 3, 0, 18) });

            // 1. Default Pet Status Filter
            Border filterBox = CreateSettingsSectionBox(Tr("1. ברירת מחדל לסינון חיות (Pet Status Filter)", "1. Pet Filter", "1. Фильтр питомцев"));
            StackPanel fStack = new StackPanel { Orientation = Orientation.Vertical };

            WrapPanel fChips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            string curFilter = _petsOverlay != null ? _petsOverlay.GetPetFilter() : "missing";

            fChips.Children.Add(CreateSettingsChoiceChip("⏳ " + Tr("רק חסר לדרגה 3 (הסתר MAX)", "Missing towards Lv.3 (Hide MAX)", "Только нужные (скрыть MAX)"), curFilter == "missing", delegate {
                if (_petsOverlay != null) _petsOverlay.SetFilters(null, "missing");
                SwitchNavTab("pets_settings");
            }));
            fChips.Children.Add(CreateSettingsChoiceChip("⭐ " + Tr("מומלץ לקלאס בלבד", "Recommended for Class", "Рекомендовано классу"), curFilter == "recommended", delegate {
                if (_petsOverlay != null) _petsOverlay.SetFilters(null, "recommended");
                SwitchNavTab("pets_settings");
            }));
            fChips.Children.Add(CreateSettingsChoiceChip("📋 " + Tr("הכל (כולל MAX דרגה 3)", "All Pets (Including Lv.3 MAX)", "Все питомцы (включая MAX)"), curFilter == "all", delegate {
                if (_petsOverlay != null) _petsOverlay.SetFilters(null, "all");
                SwitchNavTab("pets_settings");
            }));
            fStack.Children.Add(fChips);

            fStack.Children.Add(new TextBlock
            {
                Text = Tr("💡 מצב 'רק חסר' פועל בדיוק כמו בגרסה 2.5.0: מסתיר לחלוטין חיות שהגיעו לדרגה 3 (MAX) ומציג רק מפלצות שמהן עדיין צריך נשמות.", "Tip: 'Missing' filter hides all Lv.3 MAX pets so you only see monsters you still need souls from.", "Совет: режим 'Только нужные' скрывает питомцев 3 уровня."),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                TextWrapping = TextWrapping.Wrap
            });

            filterBox.Child = fStack;
            sp.Children.Add(filterBox);

            // 2. Default Scope
            Border scopeBox = CreateSettingsSectionBox(Tr("2. טווח תצוגה ברירת מחדל (Default Scope)", "2. Scope", "2. Область показа"));
            StackPanel sStack = new StackPanel { Orientation = Orientation.Vertical };
            WrapPanel sChips = new WrapPanel { Orientation = Orientation.Horizontal };
            string curScope = _petsOverlay != null ? _petsOverlay.GetScopeFilter() : "area";

            sChips.Children.Add(CreateSettingsChoiceChip("📍 " + Tr("באזור הנוכחי (Zone)", "Current Zone", "В текущей зоне"), curScope == "area", delegate {
                if (_petsOverlay != null) _petsOverlay.SetFilters("area", null);
                SwitchNavTab("pets_settings");
            }));
            sChips.Children.Add(CreateSettingsChoiceChip("🎯 " + Tr("סביבי (עד 150 מטר)", "Nearby (<= 150m)", "Рядом (до 150м)"), curScope == "around", delegate {
                if (_petsOverlay != null) _petsOverlay.SetFilters("around", null);
                SwitchNavTab("pets_settings");
            }));
            sChips.Children.Add(CreateSettingsChoiceChip("🗺️ " + Tr("כל המפה (World Map)", "Entire Map", "Вся карта"), curScope == "all", delegate {
                if (_petsOverlay != null) _petsOverlay.SetFilters("all", null);
                SwitchNavTab("pets_settings");
            }));
            sStack.Children.Add(sChips);
            scopeBox.Child = sStack;
            sp.Children.Add(scopeBox);

            // 3. Recommended Class
            Border classBox = CreateSettingsSectionBox(Tr("3. קלאס השחקן להמלצות נשמות (Class Soul Stones)", "3. Class Recommendation", "3. Класс для рекомендаций"));
            StackPanel cStack = new StackPanel { Orientation = Orientation.Vertical };
            WrapPanel cChips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            string[] classes = new string[] { "Cleric", "Chanter", "Sorcerer", "Spiritmaster", "Gladiator", "Templar", "Ranger", "Assassin" };
            string curClass = _petsOverlay != null ? _petsOverlay.PlayerClass : "Cleric";
            foreach (string cls in classes)
            {
                string cCopy = cls;
                cChips.Children.Add(CreateSettingsChoiceChip(cls, curClass == cls, delegate {
                    if (_petsOverlay != null) _petsOverlay.PlayerClass = cCopy;
                    SwitchNavTab("pets_settings");
                }));
            }
            cStack.Children.Add(cChips);

            cStack.Children.Add(new TextBlock
            {
                Text = Tr("עבור קלריק מומלצות נשמות מסוג Intellect (עוצמת קסם/ריפוי) ו-Transcendence (בריאות/הגנה).", "For Clerics: Intellect & Transcendence soul stones are prioritized.", "Для целителя рекомендуются камни Intellect и Transcendence."),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(168, 85, 247))
            });
            classBox.Child = cStack;
            sp.Children.Add(classBox);

            // 4. Window Position & Opacity
            Border winBox = CreateSettingsSectionBox(Tr("4. מיקום ושקיפות חלון זה", "4. Window Controls", "4. Окно оверлея"));
            StackPanel wStack = new StackPanel { Orientation = Orientation.Horizontal };
            Button resetPetsPos = CreateActionPillBtn("🔄 " + Tr("אפס מיקום לתוסף זה (24, 48)", "Reset Pets Position", "Сбросить позицию"), delegate {
                if (_petsOverlay != null) { _petsOverlay.Left = 24; _petsOverlay.Top = 48; _petsOverlay.Show(); }
            });
            wStack.Children.Add(resetPetsPos);
            winBox.Child = wStack;
            sp.Children.Add(winBox);

            sv.Content = sp;
            return sv;
        }

        private Border CreateSettingsSectionBox(string title)
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 14, 19, 29)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            return b;
        }

        private Button CreateSettingsChoiceChip(string label, bool isSelected, Action onClick)
        {
            Button btn = new Button
            {
                Content = label,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : Brushes.Transparent,
                BorderThickness = new Thickness(isSelected ? 1.5 : 0),
                FontSize = 10.5,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btn.Click += delegate { onClick(); };
            return btn;
        }

        // =====================================================================
        // VIEW 3: DEDICATED CLERIC SETTINGS & SIMULATION LAB
        // =====================================================================
        private FrameworkElement BuildClericSettingsView()
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24) };
            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 750, HorizontalAlignment = HorizontalAlignment.Left };

            // Title
            sp.Children.Add(new TextBlock { Text = Tr("הגדרות עוזר קלריק וריפוי חכם (Cleric Assistant)", "Cleric Assistant Settings & Thresholds", "Настройки ассистента целителя"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(216, 180, 254)) });
            sp.Children.Add(new TextBlock { Text = Tr("הגדר מצב סולו/קבוצה, ספי אחוז חיים להמלצות ריפוי ובדוק תרחישים בלייב.", "Configure solo/party tracking, HP thresholds, and test live combat scenarios.", "Настройка режима соло/группы, порогов здоровья и проверка скиллов."), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), Margin = new Thickness(0, 3, 0, 18) });

            // 1. Solo vs Party Mode
            Border modeBox = CreateSettingsSectionBox(Tr("1. מצב מעקב (Tracking Mode)", "1. Tracking Mode", "1. Режим отслеживания"));
            StackPanel mStack = new StackPanel { Orientation = Orientation.Vertical };
            WrapPanel mChips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            bool isSolo = _clericWindow != null ? _clericWindow.SoloMode : true;

            mChips.Children.Add(CreateSettingsChoiceChip("👤 " + Tr("מצב סולו (עוקב ומרפא רק אותך)", "Solo Mode (Track only YOU)", "Соло (исцелять только себя)"), isSolo, delegate {
                if (_clericWindow != null) { _clericWindow.SoloMode = true; _clericWindow.UpdateModeButton(); _clericWindow.RenderCurrentState(); }
                SwitchNavTab("cleric_settings");
            }));
            mChips.Children.Add(CreateSettingsChoiceChip("👥 " + Tr("מצב קבוצה (חברי הקבוצה בלבד)", "Party Mode (Party members only)", "Группа (только сопартийцы)"), !isSolo, delegate {
                if (_clericWindow != null) { _clericWindow.SoloMode = false; _clericWindow.UpdateModeButton(); _clericWindow.RenderCurrentState(); }
                SwitchNavTab("cleric_settings");
            }));
            mStack.Children.Add(mChips);

            mStack.Children.Add(new TextBlock
            {
                Text = Tr("💡 במצב סולו, ה-HUD מגיב אך ורק לבריאות שלך ומסתיר לחלוטין שחקנים זרים שנמצאים מסביבך במפה.", "Tip: Solo mode responds ONLY to your HP and completely ignores random bystanders in the zone.", "Совет: в режиме соло оверлей следит только за вами и игнорирует случайных игроков."),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                TextWrapping = TextWrapping.Wrap
            });
            modeBox.Child = mStack;
            sp.Children.Add(modeBox);

            // 2. Healing Thresholds
            Border threshBox = CreateSettingsSectionBox(Tr("2. ספי ריפוי באחוזים (Healing Thresholds)", "2. Healing Thresholds", "2. Пороги исцеления"));
            StackPanel tStack = new StackPanel { Orientation = Orientation.Vertical };

            // Normal Heal Threshold
            DockPanel nDock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            nDock.Children.Add(new TextBlock { Text = Tr("סף ריפוי רגיל (אור מרפא [2] + רגנרציה [Q]):", "Normal Heal Threshold (Healing Light [2] + HoT [Q]):", "Порог обычного исцеления [2]:"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(234, 179, 8)) });
            _valNormal = new TextBlock { Text = (_clericWindow != null ? _clericWindow.NormalThreshold : 80) + "%", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(234, 179, 8)) };
            DockPanel.SetDock(_valNormal, Dock.Right);
            nDock.Children.Add(_valNormal);
            tStack.Children.Add(nDock);

            _sliderNormal = new Slider { Minimum = 50, Maximum = 90, Value = (_clericWindow != null ? _clericWindow.NormalThreshold : 80), Margin = new Thickness(0, 0, 0, 14) };
            _sliderNormal.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
            {
                if (_clericWindow != null) { _clericWindow.NormalThreshold = e.NewValue; _clericWindow.RenderCurrentState(); }
                if (_valNormal != null) _valNormal.Text = Math.Round(e.NewValue) + "%";
            };
            tStack.Children.Add(_sliderNormal);

            // Emergency Heal Threshold
            DockPanel eDock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            eDock.Children.Add(new TextBlock { Text = Tr("סף ריפוי חירום (מחילת חטאים [1] מתפרץ):", "Emergency Heal Threshold (Absolution [1]):", "Порог экстренного исцеления [1]:"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) });
            _valEmergency = new TextBlock { Text = (_clericWindow != null ? _clericWindow.EmergencyThreshold : 40) + "%", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) };
            DockPanel.SetDock(_valEmergency, Dock.Right);
            eDock.Children.Add(_valEmergency);
            tStack.Children.Add(eDock);

            _sliderEmergency = new Slider { Minimum = 20, Maximum = 60, Value = (_clericWindow != null ? _clericWindow.EmergencyThreshold : 40), Margin = new Thickness(0, 0, 0, 10) };
            _sliderEmergency.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
            {
                if (_clericWindow != null) { _clericWindow.EmergencyThreshold = e.NewValue; _clericWindow.RenderCurrentState(); }
                if (_valEmergency != null) _valEmergency.Text = Math.Round(e.NewValue) + "%";
            };
            tStack.Children.Add(_sliderEmergency);

            tStack.Children.Add(new TextBlock
            {
                Text = Tr("💡 כפתורי הסקילים מוסתרים לחלוטין כשהחיים תקינים (מעל הסף), ומופיעים מיידית כשצריך ריפוי!", "Tip: Skill buttons are completely hidden when HP is healthy and pop up only when healing is needed!", "Совет: кнопки скиллов скрыты когда здоровье в норме и появляются только при необходимости."),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153))
            });

            threshBox.Child = tStack;
            sp.Children.Add(threshBox);

            // 3. Special Alerts (Dispel / Res / Audio)
            Border alertBox = CreateSettingsSectionBox(Tr("3. התראות מיוחדות וצליל", "3. Alerts & Sound", "3. Оповещения и звук"));
            WrapPanel alWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            _cbDispel = new CheckBox
            {
                Content = Tr("הסרת קללות ודיבאפים [R]", "Dispel Debuffs [R]", "Рассеивание дебаффов [R]"),
                IsChecked = _clericWindow != null ? _clericWindow.DispelEnabled : true,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 18, 6)
            };
            _cbDispel.Checked += delegate { if (_clericWindow != null) _clericWindow.DispelEnabled = true; };
            _cbDispel.Unchecked += delegate { if (_clericWindow != null) _clericWindow.DispelEnabled = false; };
            alWrap.Children.Add(_cbDispel);

            _cbRes = new CheckBox
            {
                Content = Tr("החייאת חברי קבוצה [3]", "Resurrection Alerts [3]", "Воскрешение [3]"),
                IsChecked = _clericWindow != null ? _clericWindow.ResEnabled : true,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 18, 6)
            };
            _cbRes.Checked += delegate { if (_clericWindow != null) _clericWindow.ResEnabled = true; };
            _cbRes.Unchecked += delegate { if (_clericWindow != null) _clericWindow.ResEnabled = false; };
            alWrap.Children.Add(_cbRes);

            _cbSound = new CheckBox
            {
                Content = Tr("צליל התראה קולי", "Audio Chime", "Звуковой сигнал"),
                IsChecked = _clericWindow != null ? _clericWindow.SoundEnabled : true,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 18, 6)
            };
            _cbSound.Checked += delegate { if (_clericWindow != null) _clericWindow.SoundEnabled = true; };
            _cbSound.Unchecked += delegate { if (_clericWindow != null) _clericWindow.SoundEnabled = false; };
            alWrap.Children.Add(_cbSound);

            alertBox.Child = alWrap;
            sp.Children.Add(alertBox);

            // 4. Live Simulation Test Buttons
            Border simBox = CreateSettingsSectionBox(Tr("4. בדיקת תרחישים בלייב על המסך (סימולציה)", "4. Live Simulation Tests", "4. Тестирование сценариев"));
            StackPanel simStack = new StackPanel { Orientation = Orientation.Vertical };
            simStack.Children.Add(new TextBlock { Text = Tr("לחיצה על כפתור מדמה מיידית את המצב ב-HUD מעל המשחק:", "Click to test scenarios on the floating HUD:", "Нажмите для проверки оверлея:"), FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), Margin = new Thickness(0, 0, 0, 8) });

            WrapPanel simBtns = new WrapPanel { Orientation = Orientation.Horizontal };
            simBtns.Children.Add(CreateSimTestBtn("🧪 סולו 75% HP (פצוע)", delegate {
                if (_clericWindow != null) _clericWindow.UpdatePartyState(new List<PartyMemberState> {
                    new PartyMemberState { EntityId = 10694, Name = ActiveCharacterName, ClassName = "Cleric", Level = 45, Hp = 9570, MaxHp = 12760, IsSelf = true }
                });
            }));
            simBtns.Children.Add(CreateSimTestBtn("⚠️ סולו 30% HP (חירום מתפרץ)", delegate {
                if (_clericWindow != null) _clericWindow.UpdatePartyState(new List<PartyMemberState> {
                    new PartyMemberState { EntityId = 10694, Name = ActiveCharacterName, ClassName = "Cleric", Level = 45, Hp = 3828, MaxHp = 12760, IsSelf = true }
                });
            }));
            simBtns.Children.Add(CreateSimTestBtn("🌀 הסרת קללה (Dispel)", delegate {
                PartyMemberState p = new PartyMemberState { EntityId = 10694, Name = ActiveCharacterName, ClassName = "Cleric", Level = 45, Hp = 11500, MaxHp = 12760, IsSelf = true };
                p.Debuffs.Add(10101);
                if (_clericWindow != null) _clericWindow.UpdatePartyState(new List<PartyMemberState> { p });
            }));
            simBtns.Children.Add(CreateSimTestBtn("✝️ החייאת חבר שנפל", delegate {
                if (_clericWindow != null)
                {
                    _clericWindow.SoloMode = false;
                    _clericWindow.UpdateModeButton();
                    _clericWindow.UpdatePartyState(new List<PartyMemberState> {
                        new PartyMemberState { EntityId = 10694, Name = ActiveCharacterName, ClassName = "Cleric", Level = 45, Hp = 12760, MaxHp = 12760, IsSelf = true },
                        new PartyMemberState { EntityId = 2002, Name = "TemplarTank", ClassName = "Templar", Level = 45, Hp = 0, MaxHp = 14500, IsDead = true }
                    });
                }
            }));
            simBtns.Children.Add(CreateSimTestBtn("● בריא ויציב (100% HP - סקילים מוסתרים)", delegate {
                if (_clericWindow != null) _clericWindow.UpdatePartyState(new List<PartyMemberState> {
                    new PartyMemberState { EntityId = 10694, Name = ActiveCharacterName, ClassName = "Cleric", Level = 45, Hp = 12760, MaxHp = 12760, IsSelf = true }
                });
            }));
            simStack.Children.Add(simBtns);
            simBox.Child = simStack;
            sp.Children.Add(simBox);

            // 5. Window Reset
            Border winBox = CreateSettingsSectionBox(Tr("5. מיקום חלון הקלריק", "5. Position", "5. Позиция"));
            Button resetClericPos = CreateActionPillBtn("🔄 " + Tr("אפס מיקום לתוסף זה (820, 48)", "Reset Cleric Position", "Сбросить позицию целителя"), delegate {
                if (_clericWindow != null) { _clericWindow.Left = 820; _clericWindow.Top = 48; _clericWindow.Show(); }
            });
            winBox.Child = resetClericPos;
            sp.Children.Add(winBox);

            sv.Content = sp;
            return sv;
        }

        private Button CreateSimTestBtn(string text, Action onClick)
        {
            Button b = new Button
            {
                Content = text,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)),
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += delegate { onClick(); };
            return b;
        }

        // =====================================================================
        // VIEW 4: MAP & RADAR SETTINGS
        // =====================================================================
        private FrameworkElement BuildMapSettingsView()
        {
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24) };
            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 750, HorizontalAlignment = HorizontalAlignment.Left };

            sp.Children.Add(new TextBlock { Text = Tr("הגדרות מפת עולם וקוביות נסתרות (Map & Cubes)", "World Map & Hidden Cubes Radar", "Интерактивная карта и радар кубов"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)) });
            sp.Children.Add(new TextBlock { Text = Tr("התאמת זום רדאר, סימון קוביות תיק מוסתרות ורמות מפלצות באזורים.", "Adjust radar zoom, hidden inventory cubes, and zone monster levels.", "Настройка масштаба радара, скрытых кубов и уровней монстров."), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), Margin = new Thickness(0, 3, 0, 18) });

            Border mBox = CreateSettingsSectionBox(Tr("הגדרות רדאר", "Radar Settings", "Настройки радара"));
            StackPanel ms = new StackPanel { Orientation = Orientation.Vertical };

            CheckBox cbCubes = new CheckBox { Content = Tr("הצג קוביות הרחבת תיק נסתרות על המפה", "Show hidden inventory cubes", "Показывать скрытые кубы"), IsChecked = true, Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), Margin = new Thickness(0, 0, 0, 8) };
            ms.Children.Add(cbCubes);

            CheckBox cbLevels = new CheckBox { Content = Tr("הצג רמות מפלצות באזורים (Zone Creature Levels)", "Show zone creature levels", "Показывать уровни монстров"), IsChecked = true, Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), Margin = new Thickness(0, 0, 0, 8) };
            ms.Children.Add(cbLevels);

            mBox.Child = ms;
            sp.Children.Add(mBox);

            sv.Content = sp;
            return sv;
        }

        // =====================================================================
        // VIEW 5: PETS DATABASE BROWSER
        // =====================================================================
        private FrameworkElement BuildPetsBrowserView()
        {
            Grid g = new Grid { Margin = new Thickness(16) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            DockPanel top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };

            _petSearchBox = new TextBox
            {
                Height = 26,
                Width = 180,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _petSearchBox.TextChanged += delegate { RenderPetsBrowser(); };
            top.Children.Add(_petSearchBox);
            DockPanel.SetDock(_petSearchBox, Dock.Left);

            _zoneCombo = new ComboBox
            {
                Height = 26,
                Width = 150,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 0, 8, 0)
            };
            _zoneCombo.Items.Add(Tr("כל האזורים", "All Areas", "Все зоны"));
            _zoneCombo.SelectedIndex = 0;
            _zoneCombo.SelectionChanged += delegate { RenderPetsBrowser(); };
            top.Children.Add(_zoneCombo);
            DockPanel.SetDock(_zoneCombo, Dock.Left);

            StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal };
            chips.Children.Add(CreatePetFilterChip(Tr("הכל", "All", "Все"), "all"));
            chips.Children.Add(CreatePetFilterChip(Tr("חסר לדרגה 3", "Need Lv.3", "Нужен 3 ур"), "missing"));
            chips.Children.Add(CreatePetFilterChip(Tr("אינטלקט", "Intellect", "Интеллект"), "intellect"));
            chips.Children.Add(CreatePetFilterChip(Tr("טרנסנדנס", "Transcendence", "Трансценд"), "trans"));
            top.Children.Add(chips);

            Grid.SetRow(top, 0);
            g.Children.Add(top);

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _petsBrowserContainer = new StackPanel { Orientation = Orientation.Vertical };
            sv.Content = _petsBrowserContainer;
            Grid.SetRow(sv, 1);
            g.Children.Add(sv);

            RenderPetsBrowser();
            return g;
        }

        private Button CreatePetFilterChip(string text, string genusKey)
        {
            Button b = new Button
            {
                Content = text,
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(2, 0, 2, 0),
                Background = (_petGenusFilter == genusKey) ? new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Foreground = (_petGenusFilter == genusKey) ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += delegate
            {
                _petGenusFilter = genusKey;
                RenderPetsBrowser();
            };
            return b;
        }

        private void RenderPetsBrowser()
        {
            if (_petsBrowserContainer == null || _latestData == null) return;
            _petsBrowserContainer.Children.Clear();

            ArrayList all = null;
            if (_latestData.ContainsKey("pets") && _latestData["pets"] is ArrayList)
                all = _latestData["pets"] as ArrayList;

            if (all == null) return;

            string q = (_petSearchBox != null) ? _petSearchBox.Text.Trim().ToLowerInvariant() : "";

            int count = 0;
            foreach (object item in all)
            {
                Dictionary<string, object> pet = item as Dictionary<string, object>;
                if (pet == null) continue;

                string name = pet.ContainsKey("name") && pet["name"] != null ? pet["name"].ToString().ToLowerInvariant() : "";
                string pMob = pet.ContainsKey("primaryMonsterName") && pet["primaryMonsterName"] != null ? pet["primaryMonsterName"].ToString().ToLowerInvariant() : "";
                string genus = pet.ContainsKey("genus") && pet["genus"] != null ? pet["genus"].ToString().ToLowerInvariant() : "";
                bool isLv3 = pet.ContainsKey("isLevel3") && Convert.ToBoolean(pet["isLevel3"]);

                if (!string.IsNullOrEmpty(q) && !name.Contains(q) && !pMob.Contains(q)) continue;

                if (_petGenusFilter == "missing" && isLv3) continue;
                if (_petGenusFilter == "intellect" && !genus.Contains("intellect")) continue;
                if (_petGenusFilter == "trans" && !genus.Contains("trans")) continue;

                if (count++ >= 50) break;

                Border row = CreatePetBrowserRow(pet);
                _petsBrowserContainer.Children.Add(row);
            }
        }

        private Border CreatePetBrowserRow(Dictionary<string, object> pet)
        {
            string name = pet.ContainsKey("name") && pet["name"] != null ? pet["name"].ToString() : "";
            string pMob = pet.ContainsKey("primaryMonsterName") && pet["primaryMonsterName"] != null ? pet["primaryMonsterName"].ToString() : "";
            string genus = pet.ContainsKey("genus") && pet["genus"] != null ? pet["genus"].ToString() : "special";
            int level = pet.ContainsKey("level") ? Convert.ToInt32(pet["level"]) : 0;
            int remTo3 = pet.ContainsKey("remainingToLevel3") ? Convert.ToInt32(pet["remainingToLevel3"]) : 0;
            bool isLv3 = pet.ContainsKey("isLevel3") && Convert.ToBoolean(pet["isLevel3"]);

            Color genusCol = PetsOverlayWindow.GetGenusColor(genus);

            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, genusCol.R, genusCol.G, genusCol.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 3)
            };

            DockPanel dp = new DockPanel { LastChildFill = true };

            TextBlock needTb = new TextBlock
            {
                Text = isLv3 ? "MAX" : ("Need " + remTo3),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = isLv3 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                VerticalAlignment = VerticalAlignment.Center
            };
            dp.Children.Add(needTb);
            DockPanel.SetDock(needTb, Dock.Right);

            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = name + (string.IsNullOrEmpty(pMob) ? "" : (" (" + pMob + ")")), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)), Margin = new Thickness(0, 0, 10, 0) });
            sp.Children.Add(new TextBlock { Text = "Lv." + level + " · " + genus.ToUpperInvariant(), FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(genusCol), VerticalAlignment = VerticalAlignment.Center });
            dp.Children.Add(sp);

            b.Child = dp;
            return b;
        }

        // =====================================================================
        // VIEW 6: CRAFTING WISHLIST
        // =====================================================================
        private FrameworkElement BuildCraftingWishlistView()
        {
            Grid g = new Grid { Margin = new Thickness(16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: Catalog
            Border leftBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(12, 16, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 8, 0)
            };
            StackPanel leftStack = new StackPanel { Orientation = Orientation.Vertical };
            leftStack.Children.Add(new TextBlock { Text = Tr("קטלוג פריטים לקראפטינג", "Crafting Catalog", "Каталог крафта"), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)), Margin = new Thickness(0, 0, 0, 8) });

            ScrollViewer svCatalog = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 560 };
            _craftCatalogContainer = new StackPanel { Orientation = Orientation.Vertical };

            foreach (CraftItem item in _craftCatalog)
            {
                Border row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(120, 20, 26, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(40, 53, 76)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(8, 6, 8, 6)
                };

                DockPanel dp = new DockPanel { LastChildFill = true };

                Button addBtn = new Button
                {
                    Content = "+ " + Tr("הוסף", "Add", "Добавить"),
                    Height = 22,
                    Padding = new Thickness(8, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                CraftItem iCopy = item;
                addBtn.Click += delegate
                {
                    CraftWishlistItem wish = _craftWishlist.Find(w => w.Id == iCopy.Id);
                    if (wish == null)
                    {
                        _craftWishlist.Add(new CraftWishlistItem { Id = iCopy.Id, Name = iCopy.Name, Profession = iCopy.Profession, TargetCount = 1, HaveCount = 0, Materials = iCopy.Materials });
                    }
                    else
                    {
                        wish.TargetCount += 1;
                    }
                    RefreshCraftWishlistView();
                };
                dp.Children.Add(addBtn);
                DockPanel.SetDock(addBtn, Dock.Right);

                StackPanel info = new StackPanel { Orientation = Orientation.Vertical };
                info.Children.Add(new TextBlock { Text = item.Name, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)) });
                info.Children.Add(new TextBlock { Text = item.Profession + " · Lv." + item.Level + " (" + item.Rarity + ")", FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)) });

                dp.Children.Add(info);
                row.Child = dp;
                _craftCatalogContainer.Children.Add(row);
            }
            svCatalog.Content = _craftCatalogContainer;
            leftStack.Children.Add(svCatalog);
            leftBorder.Child = leftStack;
            Grid.SetColumn(leftBorder, 0);
            g.Children.Add(leftBorder);

            // Right: Active Wishlist
            Border rightBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(12, 16, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(8, 0, 0, 0)
            };

            StackPanel rightStack = new StackPanel { Orientation = Orientation.Vertical };
            rightStack.Children.Add(new TextBlock { Text = Tr("רשימת המשאלות שלי (מעקב לייב)", "My Crafting Wishlist", "Мой список желаемого"), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(254, 240, 138)), Margin = new Thickness(0, 0, 0, 8) });

            ScrollViewer svWish = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 560 };
            _craftWishlistContainer = new StackPanel { Orientation = Orientation.Vertical };
            svWish.Content = _craftWishlistContainer;
            rightStack.Children.Add(svWish);
            rightBorder.Child = rightStack;
            Grid.SetColumn(rightBorder, 1);
            g.Children.Add(rightBorder);

            RefreshCraftWishlistView();
            return g;
        }

        private void RefreshCraftWishlistView()
        {
            if (_craftWishlistContainer == null) return;
            _craftWishlistContainer.Children.Clear();

            if (_craftWishlist.Count == 0)
            {
                _craftWishlistContainer.Children.Add(new TextBlock
                {
                    Text = Tr("רשימת המשאלות ריקה. הוסף פריטים מהקטלוג משמאל!", "Wishlist is empty. Add items from the left catalog!", "Список пуст. Добавьте предметы слева!"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(10)
                });
                return;
            }

            foreach (CraftWishlistItem wish in _craftWishlist)
            {
                Border row = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(120, 20, 26, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(234, 179, 8)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(8, 6, 8, 6)
                };

                DockPanel dp = new DockPanel { LastChildFill = true };

                StackPanel btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Button p1 = CreateSmallCraftBtn("+1", delegate { wish.TargetCount += 1; RefreshCraftWishlistView(); });
                Button m1 = CreateSmallCraftBtn("-1", delegate { if (wish.TargetCount > 1) wish.TargetCount -= 1; RefreshCraftWishlistView(); });
                Button del = CreateSmallCraftBtn("✕", delegate { _craftWishlist.Remove(wish); RefreshCraftWishlistView(); });
                btnStack.Children.Add(p1);
                btnStack.Children.Add(m1);
                btnStack.Children.Add(del);
                dp.Children.Add(btnStack);
                DockPanel.SetDock(btnStack, Dock.Right);

                StackPanel info = new StackPanel { Orientation = Orientation.Vertical };
                info.Children.Add(new TextBlock { Text = wish.Name + " (" + wish.HaveCount + " / " + wish.TargetCount + ")", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)) });
                info.Children.Add(new TextBlock { Text = wish.Profession, FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)) });

                dp.Children.Add(info);
                row.Child = dp;
                _craftWishlistContainer.Children.Add(row);
            }
        }

        private Button CreateSmallCraftBtn(string label, Action onClick)
        {
            Button b = new Button
            {
                Content = label,
                Width = 22,
                Height = 20,
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += delegate { onClick(); };
            return b;
        }

        // =====================================================================
        // VIEW 7: SYSTEM SETTINGS & HOTKEYS
        // =====================================================================
        private FrameworkElement BuildSettingsView()
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(24), MaxWidth = 750, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = Tr("הגדרות מערכת וקיצורי מקשים", "System Settings & Hotkeys", "Настройки системы и горячие клавиши"), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)), Margin = new Thickness(0, 0, 0, 16) });

            sp.Children.Add(CreateShortcutRow("Alt+E", Tr("פתיחה ומזעור מיידי של Aion 2 Studio", "Toggle Studio Hub Window", "Открыть/закрыть Studio Hub")));
            sp.Children.Add(CreateShortcutRow("F9", Tr("הסתרת / הצגת כל התוספים הצפים מעל המשחק", "Hide / Show All In-Game HUDs", "Скрыть/показать все оверлеи")));
            sp.Children.Add(CreateShortcutRow("F10", Tr("נעילת עכבר (מעבר חופשי לקליקים בקרבות)", "Lock Mouse Dragging (Click-through)", "Блокировка мыши (Click-through)")));

            return sp;
        }

        private Border CreateShortcutRow(string key, string desc)
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            DockPanel dp = new DockPanel { LastChildFill = true };

            Border keyPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2)
            };
            keyPill.Child = new TextBlock { Text = key, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) };
            dp.Children.Add(keyPill);
            DockPanel.SetDock(keyPill, Dock.Left);

            TextBlock descTb = new TextBlock { Text = desc, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            dp.Children.Add(descTb);

            b.Child = dp;
            return b;
        }

        private Button CreateLangBtn(string label, string langCode)
        {
            Button b = new Button
            {
                Content = label,
                Width = 28,
                Height = 22,
                Margin = new Thickness(2, 0, 0, 0),
                Background = (_lang == langCode) ? new SolidColorBrush(Color.FromArgb(120, 56, 189, 248)) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = (_lang == langCode) ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += delegate(object s, RoutedEventArgs e)
            {
                _lang = langCode;
                if (_petsOverlay != null) _petsOverlay.SetLanguage(langCode);
                if (_clericWindow != null) _clericWindow.SetLanguage(langCode);
                SwitchNavTab(_activeNavTab);
                SaveSettings();
            };
            return b;
        }

        private Button CreateWindowCtrlBtn(string label, RoutedEventHandler onClick)
        {
            Button b = new Button
            {
                Content = label,
                Width = 26,
                Height = 22,
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += onClick;
            return b;
        }

        private void LoadDatabaseFiles()
        {
            try
            {
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "pets-database.json");
                if (File.Exists(dbPath))
                {
                    string txt = File.ReadAllText(dbPath);
                    _petsDb = _json.Deserialize<Dictionary<string, object>>(txt);
                }

                // Add starter craft recipes
                _craftCatalog.Add(new CraftItem { Id = "rec_01", Name = "Greater Healing Potion", Profession = "Alchemy", Level = 40, Rarity = "Rare" });
                _craftCatalog.Add(new CraftItem { Id = "rec_02", Name = "Major Mana Potion", Profession = "Alchemy", Level = 42, Rarity = "Rare" });
                _craftCatalog.Add(new CraftItem { Id = "rec_03", Name = "Noble Mithril Breastplate", Profession = "Armorsmithing", Level = 45, Rarity = "Heroic" });
                _craftCatalog.Add(new CraftItem { Id = "rec_04", Name = "Fine Leather Jerkin", Profession = "Tailoring", Level = 44, Rarity = "Rare" });
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "studio-settings.json");
                if (File.Exists(p))
                {
                    string txt = File.ReadAllText(p);
                    Dictionary<string, object> s = _json.Deserialize<Dictionary<string, object>>(txt);
                    if (s != null)
                    {
                        if (s.ContainsKey("lang") && s["lang"] != null) _lang = s["lang"].ToString();
                        if (s.ContainsKey("globalOpacity")) _globalOpacity = Convert.ToDouble(s["globalOpacity"]);
                        if (s.ContainsKey("updateRateMs")) _updateRateMs = Convert.ToInt32(s["updateRateMs"]);
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string dataDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                string p = System.IO.Path.Combine(dataDir, "studio-settings.json");

                Dictionary<string, object> s = new Dictionary<string, object>
                {
                    { "lang", _lang },
                    { "globalOpacity", _globalOpacity },
                    { "updateRateMs", _updateRateMs },
                    { "clericEmergencyThreshold", _clericWindow != null ? _clericWindow.EmergencyThreshold : 40.0 },
                    { "clericNormalThreshold", _clericWindow != null ? _clericWindow.NormalThreshold : 80.0 },
                    { "clericDispelEnabled", _clericWindow != null ? _clericWindow.DispelEnabled : true },
                    { "clericResEnabled", _clericWindow != null ? _clericWindow.ResEnabled : true },
                    { "clericSoundEnabled", _clericWindow != null ? _clericWindow.SoundEnabled : true },
                    { "clericSoloMode", _clericWindow != null ? _clericWindow.SoloMode : true }
                };

                if (_petsOverlay != null)
                {
                    s["petsLeft"] = _petsOverlay.Left;
                    s["petsTop"] = _petsOverlay.Top;
                    s["petsVisible"] = _petsOverlay.IsVisible;
                    s["petsScope"] = _petsOverlay.GetScopeFilter();
                    s["petsFilter"] = _petsOverlay.GetPetFilter();
                    s["petsClass"] = _petsOverlay.PlayerClass;
                }
                if (_clericWindow != null)
                {
                    s["clericLeft"] = _clericWindow.Left;
                    s["clericTop"] = _clericWindow.Top;
                    s["clericVisible"] = _clericWindow.IsVisible;
                }

                File.WriteAllText(p, _json.Serialize(s), Encoding.UTF8);
            }
            catch { }
        }

        private string Tr(string heb, string eng, string rus)
        {
            if (_lang == "ENG") return eng;
            if (_lang == "RUS") return rus;
            return heb;
        }

        // =====================================================================
        // APPLICATION ENTRY POINT
        // =====================================================================
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try
                {
                    string crashPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                    File.AppendAllText(crashPath, string.Format("[{0}] Unhandled: {1}\n", DateTime.Now, e.ExceptionObject));
                }
                catch { }
            };

            Application app = new Application();
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                try
                {
                    string crashPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                    File.AppendAllText(crashPath, string.Format("[{0}] Dispatcher: {1}\n", DateTime.Now, e.Exception));
                }
                catch { }
                e.Handled = true;
            };
            app.Run(new OverlayWindow());
        }
    }

}
