# 🐾 Aion 2 Overlay Hub — Pets & Crafting Tracker

![Version](https://img.shields.io/badge/Version-v2.5.0--beta-blue.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?logo=windows)
![Framework](https://img.shields.io/badge/UI-WPF%20%28.NET%204.x%29-512BD4?logo=dotnet)
![Standalone](https://img.shields.io/badge/Engine-Pure%20C%23%20Direct%20Sync-10B981)
![Languages](https://img.shields.io/badge/i18n-HEB%20%7C%20ENG%20%7C%20RUS-F59E0B)
![Status](https://img.shields.io/badge/Map%20Engine-WIP%20%2F%20Experimental-FFA500)

A lightweight, always-on-top, transparent in-game **Overlay Hub for Aion 2** with zero installation required. Built natively in C# WPF to run alongside your game seamlessly, passively tracking your **Pets & Souls progress toward Level 3**, **⚒️ Crafting & Essence Extraction Goals**, and an **Integrated Live Radar Mini-Map** inspired by MetaRoad.

---

## 📸 In-Game Preview

![Aion 2 Overlay Hub In-Game Screenshot](./screenshoot.png)

---

## 🌟 Core Modules

### 1. 🐾 Pets & Souls Tracker (Lv. 3 Soul Engine)
* **Real-time Soul Progress**: Tracks souls needed for every pet tier (`5` for Lv.1, `25` for Lv.2, `75` for Lv.3 — `105` total souls for completion).
* **Soul Stone (Genus) Color Matching**:
  * 🟠 **Feral**: Amber / Orange (`#F97316`)
  * 🔵 **Intellect**: Sky Blue (`#60A5FA`)
  * 🟢 **Nature**: Emerald Green (`#4ADE80`)
  * 🟣 **Trans**: Purple / Violet (`#C084FC`)
  * 🟡 **Special**: Gold / Yellow (`#FACC15`)
* **Live Distance & Direction**: Displays closest live monsters with dynamic arrows (`↑`, `↗`, `→`, `↘`...) and real-time distance in meters (`12 m`, `45 m`).
* **Intelligent Tab Filtering**:
  * **Around (`מסביב`)**: Only monsters currently alive within radar proximity.
  * **Area (`באזור`)**: Pets farmable in your current subzone.
  * **Map (`במפה`)**: All pets located on the active world map.
  * **All (`הכל`)**: Complete searchable database of all 209 pets.

---

### 2. ⚒️ Live Crafting & Essence Extraction Tracker
* **Real-Time Inventory Sync**: Automatically monitors your inventory stacks. When you gather an herb, mine ore, or collect drops, the counter updates immediately (`18 / 25 · Missing: 7`).
* **Wishlist & Quantity Goals**: Click items to add them to your target wishlist. Use quick buttons (`−10`, `+10`, `×2`, `🗑️`) to set goal quantities.
* **Recipe Decomposition**: Select finished items (e.g., *Major Life Potion*, *Recovery Serum*), and the tracker breaks them down into their base extraction materials (Angelica, Titanium Ore, Water Crystal).
* **🎉 Ready to Craft Celebration**: When all required ingredients for pinned crafts are gathered, a prominent green celebration banner alerts you that you are ready to craft!
* **Gathering Statistics (Rate / Hour & ETA)**: Calculates your live gathering speed (`+N / hr`) and gives an estimated completion time based on active gathering timestamps.
* **Extraction Guide & Node Levels**: Lists recommended zones and minimum Essence Extraction level requirements (`Req. Lv. 1`–`Lv. 210`) for every material.

---

### 3. 🗺️ Integrated World Mini-Map & Radar (MetaRoad Style)
* **High-Resolution World Terrain**: Renders official high-res map textures for active zones (Altgard, Morheim, Verteron, Eltnen, Reshanta/Abyss).
* **🏷️ Level Guide (Area Mob Levels)**:
  * MetaRoad-style circular badges at each subzone center with mob level bounds (`10-11`, `18-20`, `27-30`, `33`, `43-45`).
  * Color-coded by difficulty: 🟢 Green (Lv. 1–22), 🟡 Amber (Lv. 23–36), 🔴 Red (Lv. 37–50+).
  * Prominent subzone name pills underneath each badge.
* **📦 Hidden Cubes & Mystery Chests**:
  * 1,440+ Hidden Cube spawn locations and regional chests displayed as bright purple squares (`#C084FC`).
  * Hover tooltip shows cube name and location.
* **🧭 Compass vs. North-Up Mode**:
  * **Compass Mode (`🧭`)**: Map rotates around the player with continuous heading estimation and 0% black corner clipping.
  * **North-Up Mode (`⬆️`)**: Stable fixed map with 360° rotating player chevron and vision flashlight cone.
* **Interactive Navigation**:
  * Left-Click Drag: Pan around the world.
  * Mouse Wheel: Zoom in/out (`0.06x` to `4.0x`).
  * Double-Click / `🎯`: Re-center on player.
  * `⤢`: Expand / collapse map height (`180px` ↔ `330px`).

---

## ⚠️ Known Issues & Work-In-Progress (שקיפות ובאגים ידועים)

> [!WARNING]
> מודול המפה החדש נמצא כרגע בשלב בטא פעיל (`Experimental / WIP`). אנחנו ממשיכים לשפר וללטש אותו:
>
> 1. **סיבוב מפה במקום (Stationary Heading)**:
>    * כיוון השחקן מסונכרן מתנועת ה-Delta והפאקטות. בעמידה מוחלטת ללא תנועה, סיבוב המצלמה במקום עדיין עשוי שלא לעדכן מיידית את זווית המצפן. מומלץ להשתמש במצב **צפון קבוע (`⬆️`)** לניווט יציב ביותר ללא סחרחורת.
> 2. **פיקסול בזום קיצוני (Texture Scaling)**:
>    * טקסטורות המשחק המקוריות הן בגודל 2048×2048. בזום עמוק מאוד (`>2.5x`) על מפה רחבה, פני השטח עשויים להיראות מטושטשים או מפוקסלים. אנו עובדים על שילוב טקסטורות Vector/Tiles משופרות.
> 3. **גובה (Z-Axis Elevation)**:
>    * הרדאר מציג מרחק אוקלידי דו-ממדי (X/Y). במערות רב-מפלסיות או הרים תלולים, מפלצת עשויה להופיע קרובה אך להימצא במפלס שמעל או מתחת לשחקן.
> 4. **סנכרון תרגום מלא לחלק מהמתכונים**:
>    * חלק משמות צמחי הקראפט הגבוהים מוצגים באנגלית/רוסית וממתינים להשלמת מילון השמות העברי.

---

## ⌨️ Global Hotkeys

| Hotkey | Action | Description |
| :---: | :--- | :--- |
| **`F9`** | **Click-Through Mode** | Passes all mouse clicks through the overlay directly into Aion 2. Overlay becomes transparent to mouse events. |
| **`F10`** | **Show / Hide** | Instantly hides or restores the overlay window. |

---

## 🚀 Quick Start for Players

### Zero-Setup Direct Launch (ללא התקנות)
If you or a friend cloned/downloaded this repository, you **do NOT need to install Node.js or run any build tools**:

1. Download or clone this repository to your PC.
2. Launch **Aion 2** and log into your character.
3. Double-click **`Aion2PetsOverlay.exe`** (or run `start-overlay.bat`).
4. The overlay connects to Questlog / game memory in **0 ms** and starts tracking!

---

## 🛠️ Building from Source (`Overlay.cs`)

You can compile `Overlay.cs` directly using the built-in Microsoft .NET Framework C# compiler (`csc.exe`) without needing Visual Studio:

```powershell
$wpf = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /codepage:65001 /utf8output /target:winexe `
  /out:"Aion2PetsOverlay.exe" `
  /lib:$wpf `
  /r:PresentationCore.dll `
  /r:PresentationFramework.dll `
  /r:WindowsBase.dll `
  /r:System.Xaml.dll `
  /r:System.Web.Extensions.dll `
  /r:System.dll `
  /r:System.Drawing.dll `
  /r:System.Core.dll `
  "Overlay.cs"
```

---

## 🏷️ Git Versioning & Release Workflow

כדי לנהל גרסאות בצורה מסודרת ב-Git:

```bash
# 1. בדיקת סטטוס קבצים
git status

# 2. הוספת כל השינויים
git add Overlay.cs sniffer.cjs Aion2PetsOverlay.exe README.md data/chests-db.json data/area-levels.json data/crafting-wishlist.json

# 3. יצירת Commit גרסה
git commit -m "feat(v2.5.0): Add Crafting Tracker, MetaRoad Level Guide, Hidden Cubes, and Soul Stone colors"

# 4. יצירת Git Tag לגרסה
git tag -a v2.5.0 -m "Release v2.5.0: Crafting Wishlist, MetaRoad Mini-Map & Soul Stone Genus Colors"

# 5. דחיפה ל-GitHub כולל תגיות
git push origin main --tags
```

---

## 📁 Repository Structure

```text
├── Aion2PetsOverlay.exe      # Compiled native WPF overlay (portable, zero-install)
├── Overlay.cs                # Pure C# WPF engine (Hub, Pets Tracker, Crafting, Mini-Map)
├── sniffer.cjs               # Optional Node.js packet & zone synchronizer
├── overlay.html              # OBS / Browser edition
├── start-overlay.bat         # Windows one-click startup batch script
├── screenshoot.png           # In-game preview screenshot
└── data/
    ├── pets-db.json          # 209 Aion 2 Pets database with drop rates and spawn locations
    ├── chests-db.json        # 1,440+ Hidden Cubes & Chests coordinates across all 7 worlds
    ├── area-levels.json      # Precomputed subzone mob levels (p10-p90) for Level Guide
    ├── crafting-wishlist.json# Saved user craft wishlist & goals
    ├── maps/                 # High-resolution world terrain textures (.png)
    ├── icons/                # Auto-cached pet icons (.webp) [git-ignored]
    ├── overlay-state.json    # Real-time state IPC exchange [git-ignored]
    └── ui-settings.json      # User preferences, window size & active tabs [git-ignored]
```

---

## 🇮🇱 מדריך מקיף למשתמש בעברית

### מה חדש בגרסה v2.5.0?
1. **⚒️ מעקב קראפטינג ואיסוף (Crafting & Essence Extraction)**:
   * מעקב ישיר אחר חומרי גלם בתיק (צמחים, עפרות, אבני חן, את'ר וקריסטלים).
   * הגדרת כמויות יעד (למשל `100` אנג'ליקה או `50` עפרת טיטניום).
   * התרעת **`🎉 Ready to Craft!`** ירוקה ברגע שכל החומרים נאספו.
   * קצב איסוף לשעה (Rate/hr) וזמן משוער לסיום איסוף (ETA).
   * מדריך אזורי איסוף מומלצים ודרישות רמת איסוף (`Req. Lv. 1–210`).

2. **🗺️ מפת עולם ורדאר חי (בהשראת MetaRoad)**:
   * **מדריך רמות אזור (`🏷️ LVL`)**: מציג עיגולים צבעוניים עם רמות המפלצות בכל אזור (`10-11`, `18-20`, `27-30`, `33`, `43-45`) לפי קושי (ירוק, צהוב, אדום).
   * **קוביות מוסתרות (`📦`)**: מעל 1,440 נקודות של Hidden Cubes ותיבות שלל בריבועים סגולים בולטים (`#C084FC`) בדיוק כמו במפה של MetaRoad.
   * **צבע לפי אבן הנשמה (Soul Stone)**: פס ההתקדמות בכרטיסייה והנקודות במפה נצבעים בצבע האבן המדויקת של המפלצת (ענבר, כחול, ירוק, סגול, זהב).
   * **מצב מצפן (`🧭`) או צפון קבוע (`⬆️`)**: מפה מסתובבת ללא פינות שחורות, או מצב צפון יציב עם חץ שחקן מסתובב ב-360°.

3. **חברים מורידים מגיט וזה עובד מיד**:
   * הקובץ המקומפל `Aion2PetsOverlay.exe` נמצא במאגר ורץ עצמאית לחלוטין ללא התקנת ספריות חיצוניות או Node.js.
