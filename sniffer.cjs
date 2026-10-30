const fs = require("fs");
const path = require("path");
const http = require("http");
const EventEmitter = require("events");

const PORT = Number(process.env.OVERLAY_PORT || 47420);
const DATA_DIR = path.join(__dirname, "data");
const DB_PATH = path.join(DATA_DIR, "pets-db.json");
const LOCAL_STATE_PATH = path.join(DATA_DIR, "live-state.json");
const OVERLAY_STATE_PATH = path.join(DATA_DIR, "overlay-state.json");
const MAP_OVERRIDE_PATH = path.join(DATA_DIR, "map-override.json");
const QUESTLOG_DIR = path.join(process.env.APPDATA || "C:\\Users\\costa\\AppData\\Roaming", "Questlog", "aion-2");
const KOFFI_PATH = "C:\\Program Files\\Questlog\\resources\\app.asar.unpacked\\node_modules\\koffi\\build\\koffi\\win32_x64\\koffi.node";
const NPCAP_DIR = path.join(process.env.SystemRoot || "C:\\Windows", "System32", "Npcap");

// Load pre-indexed Pet & Map database
const db = JSON.parse(fs.readFileSync(DB_PATH, "utf8"));
const STEPS = [5, 25, 75]; // Lv0->1: 5, Lv1->2: 25, Lv2->3: 75 (Total = 105)
const MAX_LEVEL = 3;
const TOTAL_SOULS_LV3 = 105;

function calcStepGain(currentLevel, spentPoints) {
  let sum = 0;
  for (let i = currentLevel; i < MAX_LEVEL; i++) {
    sum += STEPS[i];
    if (sum === spentPoints) return i + 1 - currentLevel;
    if (sum > spentPoints) break;
  }
  return 0;
}

function getPetProgress(rawLevel = 0, rawSouls = 0) {
  rawLevel = Math.max(0, Math.min(MAX_LEVEL, Number(rawLevel) || 0));
  rawSouls = Math.max(0, Number(rawSouls) || 0);

  let effLevel = rawLevel;
  let effSouls = rawSouls;
  while (effLevel < MAX_LEVEL && effSouls >= STEPS[effLevel]) {
    effSouls -= STEPS[effLevel];
    effLevel++;
  }

  let neededFromRawTo3 = 0;
  for (let l = rawLevel; l < MAX_LEVEL; l++) {
    neededFromRawTo3 += STEPS[l];
  }

  const spentBeforeRaw = TOTAL_SOULS_LV3 - neededFromRawTo3;
  const isLevel3 = rawLevel >= MAX_LEVEL;
  const remainingToLevel3 = isLevel3 ? 0 : Math.max(0, neededFromRawTo3 - rawSouls);
  const totalHaveToward3 = isLevel3 ? TOTAL_SOULS_LV3 : Math.min(TOTAL_SOULS_LV3, spentBeforeRaw + rawSouls);
  const nextStepNeed = effLevel < MAX_LEVEL ? STEPS[effLevel] : 0;
  const currentStepNeed = rawLevel < MAX_LEVEL ? STEPS[rawLevel] : 0;

  return {
    rawLevel,
    rawSouls,
    effectiveLevel: effLevel,
    soulsInStep: effLevel >= MAX_LEVEL ? 0 : Math.max(0, effSouls),
    nextStepNeed,
    currentStepNeed,
    neededFromRawTo3,
    totalHaveToward3,
    totalRequiredFor3: TOTAL_SOULS_LV3,
    remainingToLevel3, // 0 if Level 3, else exact missing souls to reach Level 3
    isLevel3,
    canLevelUpNow: effLevel > rawLevel
  };
}

function pointInPoly(poly, pt) {
  const x = pt[0], y = pt[1];
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const xi = poly[i][0], yi = poly[i][1];
    const xj = poly[j][0], yj = poly[j][1];
    if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) {
      inside = !inside;
    }
  }
  return inside;
}

function findAreaAt(worldId, gameX, gameY) {
  if (worldId === undefined || worldId === null || gameX === undefined || gameY === undefined) return null;
  const list = db.areasByWorld[String(worldId)];
  if (!list) return null;
  // Game coordinates have Y inverted relative to map coordinates: [x, -y]
  const coord = [gameX, -gameY];
  for (const item of list) {
    for (let i = 0; i < item.polygons.length; i++) {
      const [minX, minY, maxX, maxY] = item.boxes[i];
      if (coord[0] >= minX && coord[0] <= maxX && coord[1] >= minY && coord[1] <= maxY) {
        if (pointInPoly(item.polygons[i], coord)) {
          return { id: item.id, name: item.name, kind: item.kind };
        }
      }
    }
  }
  return null;
}

// ============================================================================
// Live State Store
// ============================================================================
const bus = new EventEmitter();
bus.setMaxListeners(0);

const state = {
  sniffer: {
    status: "starting",
    connections: 0,
    packetsDecoded: 0,
    lastPacketAt: 0,
    lastPacketName: null,
    error: null
  },
  character: {
    name: null,
    server: null,
    race: null,
    level: null,
    class: null,
    confirmedAt: 0
  },
  location: {
    mapId: null,
    mapName: null,
    rawMapId: null,
    inInstance: false,
    faction: null,
    areaId: null,
    areaName: null,
    x: null,
    y: null,
    z: null,
    updatedAt: 0
  },
  manualMapId: null,
  lastMapOverrideAt: 0,
  selfEntity: null,
  levels: {},      // petId -> 0..3
  souls: {},       // petId -> current unspent souls
  kills: {},       // npcId -> count
  soulsGained: {}, // petId -> session gained
  liveNpcs: new Map(), // entityId -> { entity, npc, x, y, z, hp, maxHp, updatedAt }
  aroundCounts: {},    // npcId -> count (merged from live packets + Questlog tracker)
  recentEvents: [],    // Recent soul drops / level-ups
  cleric: {
    self: {
      entity: 0,
      name: "ManatDam",
      class: "cleric",
      level: 45,
      hp: 11527,
      maxHp: 11527,
      mp: 3286,
      maxMp: 3286,
      isDead: false,
      debuffs: []
    },
    party: []
  }
};

function pushRecentEvent(evt) {
  state.recentEvents.unshift({ id: Date.now() + "-" + Math.random().toString(36).slice(2, 6), at: Date.now(), ...evt });
  if (state.recentEvents.length > 25) state.recentEvents.length = 25;
}

function readJsonSafe(filePath) {
  try {
    if (!fs.existsSync(filePath)) return null;
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

function saveLocalState() {
  try {
    const payload = {
      savedAt: Date.now(),
      character: state.character,
      location: state.location,
      levels: state.levels,
      souls: state.souls,
      kills: state.kills,
      soulsGained: state.soulsGained
    };
    fs.writeFileSync(LOCAL_STATE_PATH + ".tmp", JSON.stringify(payload));
    fs.renameSync(LOCAL_STATE_PATH + ".tmp", LOCAL_STATE_PATH);
  } catch {}
}

function getDefaultWorldForRace(race) {
  return race === "light" ? "1010" : "1110";
}

function updateMapAndArea(mapId, x, y, z, isManual = false, heading = null) {
  if (mapId !== undefined && mapId !== null && mapId !== "") {
    const mapStr = String(mapId);
    const w = db.worlds[mapStr];
    if (isManual && w) {
      state.manualMapId = mapStr;
      state.location.mapId = mapStr;
      state.location.mapName = w.name;
      state.location.faction = w.faction;
    } else if (w) {
      state.location.rawMapId = mapStr;
      state.location.inInstance = false;
      if (!state.manualMapId) {
        state.location.mapId = mapStr;
        state.location.mapName = w.name;
        state.location.faction = w.faction;
      }
    } else {
      // Player is inside an instance/dungeon or sub-map not in pet worlds
      state.location.rawMapId = mapStr;
      state.location.inInstance = true;
      if (!state.location.mapId || !db.worlds[state.location.mapId]) {
        const defId = state.manualMapId || getDefaultWorldForRace(state.character.race);
        const defW = db.worlds[defId];
        state.location.mapId = defId;
        state.location.mapName = defW ? defW.name : `Map #${defId}`;
        state.location.faction = defW ? defW.faction : "other";
      }
    }
  } else if (!state.location.mapId || !db.worlds[state.location.mapId]) {
    const defId = state.manualMapId || getDefaultWorldForRace(state.character.race);
    const defW = db.worlds[defId];
    state.location.mapId = defId;
    state.location.mapName = defW ? defW.name : `Map #${defId}`;
    state.location.faction = defW ? defW.faction : "other";
  }

  if (typeof x === "number" && typeof y === "number") {
    if (heading === null && state.location.x !== null && state.location.y !== null) {
      const dx = x - state.location.x;
      const dy = -(y - state.location.y);
      if (Math.hypot(dx, dy) > 20) {
        let deg = (Math.atan2(dx, dy) * 180) / Math.PI;
        if (deg < 0) deg += 360;
        state.location.heading = Math.round(deg);
      }
    }
    state.location.x = Math.round(x);
    state.location.y = Math.round(y);
    if (typeof z === "number") state.location.z = Math.round(z);
    state.location.updatedAt = Date.now();
  }
  if (typeof heading === "number" && !isNaN(heading)) {
    state.location.heading = Math.round(heading);
  }
  if (state.location.mapId && state.location.x !== null && state.location.y !== null && !state.location.inInstance) {
    const area = findAreaAt(state.location.mapId, state.location.x, state.location.y);
    state.location.areaId = area ? area.id : "";
    state.location.areaName = area ? area.name : null;
  }
}

function syncFromQuestlogFiles() {
  let changed = false;

  // Check manual map override file (for native overlay IPC)
  const mapOverride = readJsonSafe(MAP_OVERRIDE_PATH);
  if (mapOverride && mapOverride.at > state.lastMapOverrideAt && mapOverride.mapId) {
    state.lastMapOverrideAt = mapOverride.at;
    updateMapAndArea(mapOverride.mapId, undefined, undefined, undefined, true);
    changed = true;
  }

  // 1. Load local persisted state first if we have no levels loaded yet
  if (Object.keys(state.levels).length === 0) {
    const local = readJsonSafe(LOCAL_STATE_PATH);
    if (local) {
      if (local.levels) Object.assign(state.levels, local.levels);
      if (local.souls) Object.assign(state.souls, local.souls);
      if (local.character?.name && !state.character.name) Object.assign(state.character, local.character);
      if (local.location?.mapId && !state.location.mapId && db.worlds[String(local.location.mapId)]) {
        Object.assign(state.location, local.location);
      }
    }
  }

  // 2. Sync from Questlog map-tracker.json (Always keep levels & souls synced!)
  const tracker = readJsonSafe(path.join(QUESTLOG_DIR, "map-tracker.json"));
  if (tracker) {
    const ch = tracker.character;
    if (ch) {
      if (ch.name) state.character.name = ch.name;
      if (ch.server) state.character.server = ch.server;
      if (ch.race) state.character.race = ch.race;
      if (ch.confirmedAt) state.character.confirmedAt = Math.max(state.character.confirmedAt || 0, ch.confirmedAt);
      if (ch.levels) {
        for (const [k, v] of Object.entries(ch.levels)) {
          if ((state.levels[k] || 0) !== v && v >= (state.levels[k] || 0)) {
            state.levels[k] = v;
            changed = true;
          }
        }
      }
      if (ch.souls) {
        for (const [k, v] of Object.entries(ch.souls)) {
          if (state.souls[k] !== v) {
            state.souls[k] = v;
            changed = true;
          }
        }
      }
    }
    if (tracker.kills) state.kills = { ...state.kills, ...tracker.kills };
    if (tracker.soulsGained) state.soulsGained = { ...state.soulsGained, ...tracker.soulsGained };
    if (tracker.map && db.worlds[String(tracker.map)]) {
      updateMapAndArea(tracker.map);
      changed = true;
    }

    // Infer current location & area from recent killedAt if live coordinates are missing
    if (tracker.killedAt && typeof tracker.killedAt === "object") {
      let newestNpc = null;
      let newestTime = 0;
      const now = Date.now();
      for (const [npcId, time] of Object.entries(tracker.killedAt)) {
        if (typeof time === "number" && time > newestTime) {
          newestTime = time;
          newestNpc = npcId;
        }
      }
      if (newestNpc && (now - newestTime < 2 * 3600 * 1000)) {
        for (const pet of db.pets) {
          if (pet.npcs.includes(Number(newestNpc))) {
            for (const [wId, spawns] of Object.entries(pet.spawnsByWorld)) {
              const npcSpawns = spawns.filter(s => String(s.npc) === String(newestNpc));
              if (npcSpawns.length > 0) {
                if (!state.location.mapId || (state.location.mapId === "1010" && wId !== "1010")) {
                  updateMapAndArea(wId);
                  changed = true;
                }
                if (state.location.x === null || (Date.now() - (state.location.updatedAt || 0) > 45000)) {
                  let sx = 0, sy = 0;
                  for (const s of npcSpawns) { sx += s.x; sy += s.y; }
                  const cx = Math.round(sx / npcSpawns.length);
                  const cy = Math.round(sy / npcSpawns.length);
                  updateMapAndArea(wId, cx, -cy);
                  changed = true;
                }
                break;
              }
            }
            break;
          }
        }
      }

      // If live packets have 0 NPCs around, populate around from tracker.around or recent kills
      if (state.liveNpcs.size === 0) {
        const aroundMap = {};
        if (tracker.around && Object.keys(tracker.around).length > 0) {
          Object.assign(aroundMap, tracker.around);
        } else {
          for (const [npcId, time] of Object.entries(tracker.killedAt)) {
            if (now - time < 300000) { // within last 5 minutes
              aroundMap[npcId] = (aroundMap[npcId] || 0) + 1;
            }
          }
        }
        if (Object.keys(aroundMap).length > 0) {
          state.aroundCounts = aroundMap;
          changed = true;
        }
      }
    }
  }

  // 3. Sync from Questlog characters.json for character class/level fallback
  const charsFile = readJsonSafe(path.join(QUESTLOG_DIR, "characters.json"));
  if (charsFile && charsFile.characters) {
    const list = Object.values(charsFile.characters).sort((a, b) => (b.savedAt || 0) - (a.savedAt || 0));
    const active = list.find(c => c.name === state.character.name) || list[0];
    if (active) {
      if (active.entity && !state.selfEntity) {
        state.selfEntity = active.entity;
      }
      if (!state.character.name) state.character.name = active.name;
      if (!state.character.server) state.character.server = active.server;
      if (!state.character.class) state.character.class = active.class;
      if (!state.character.level) state.character.level = active.level;
      if (!state.character.race) state.character.race = active.race;
      if (Array.isArray(active.pets)) {
        for (const p of active.pets) {
          if ((state.levels[p.pet] || 0) < p.level) {
            state.levels[p.pet] = p.level;
            changed = true;
          }
        }
      }
    }
  }

  // 4. Sync live inventory stacks from tickets.json for Crafting & Gathering Tracker
  const ticketsFile = readJsonSafe(path.join(QUESTLOG_DIR, "tickets.json"));
  if (ticketsFile && ticketsFile.characters) {
    const targetCharKey = state.character.name
      ? Object.keys(ticketsFile.characters).find(k => k.endsWith(":" + state.character.name))
      : Object.keys(ticketsFile.characters)[0];
    const charData = targetCharKey ? ticketsFile.characters[targetCharKey] : Object.values(ticketsFile.characters)[0];
    if (charData && Array.isArray(charData.stacks)) {
      const inv = {};
      for (const st of charData.stacks) {
        if (st.item && st.count) {
          inv[st.item] = (inv[st.item] || 0) + Number(st.count);
        }
      }
      state.inventory = inv;
    }
  }

  // 5. Sync active session gathering & drops from loot-sessions/index.json
  const lootSessions = readJsonSafe(path.join(QUESTLOG_DIR, "loot-sessions", "index.json"));
  if (lootSessions && Array.isArray(lootSessions.entries) && lootSessions.entries.length > 0) {
    const activeSession = lootSessions.entries[0];
    if (activeSession) {
      state.craftingSession = {
        activeMs: activeSession.activeMs || 0,
        startedAt: activeSession.startedAt || 0,
        lastAt: activeSession.lastAt || 0,
        items: activeSession.items || []
      };
    }
  }

  // 6. Sync Cleric combat telemetry from newest combat-logs ndjson
  const combatLogsDir = path.join(QUESTLOG_DIR, "combat-logs");
  if (fs.existsSync(combatLogsDir)) {
    try {
      const files = fs.readdirSync(combatLogsDir).filter(f => f.endsWith(".ndjson"));
      if (files.length > 0) {
        let newestFile = null;
        let newestMtime = 0;
        for (const f of files) {
          const st = fs.statSync(path.join(combatLogsDir, f));
          if (st.mtimeMs > newestMtime) {
            newestMtime = st.mtimeMs;
            newestFile = path.join(combatLogsDir, f);
          }
        }
        if (newestFile) {
          const content = fs.readFileSync(newestFile, "utf8");
          const lines = content.split("\n");
          state.cleric.party = [];

          // Pass 1: Read initial 250 lines for player & party declarations
          for (let i = 0; i < Math.min(lines.length, 250); i++) {
            const line = lines[i]?.trim();
            if (!line) continue;
            try {
              const ev = JSON.parse(line);
              if (ev.type === "player") {
                if (ev.self || (state.character.name && ev.name === state.character.name)) {
                  if (ev.entity) {
                    state.cleric.self.entity = ev.entity;
                    if (!state.selfEntity) state.selfEntity = ev.entity;
                  }
                  if (ev.name) state.cleric.self.name = ev.name;
                  if (ev.level) state.cleric.self.level = ev.level;
                  if (ev.class) state.cleric.self.class = ev.class;
                }
              }
            } catch {}
          }

          // Pass 2: Read backward for latest live HP and maxHp
          const foundHp = new Set();
          const foundMaxHp = new Set();
          const scanLimit = Math.max(0, lines.length - 30000);
          for (let i = lines.length - 1; i >= scanLimit; i--) {
            const line = lines[i]?.trim();
            if (!line) continue;
            try {
              const ev = JSON.parse(line);
              if (ev.type === "hp") {
                const ent = ev.entity;
                const isSelf = (ent === state.cleric.self.entity || ent === state.selfEntity);
                if (isSelf) {
                  if (!foundHp.has(ent) && ev.hp !== undefined) {
                    state.cleric.self.hp = Number(ev.hp);
                    state.cleric.self.isDead = (state.cleric.self.hp <= 0);
                    foundHp.add(ent);
                  }
                  if (!foundMaxHp.has(ent) && (ev.maxHp !== undefined || ev.max !== undefined)) {
                    state.cleric.self.maxHp = Number(ev.maxHp || ev.max);
                    foundMaxHp.add(ent);
                  }
                } else {
                  let member = state.cleric.party.find(p => p.entity === ent);
                  if (member) {
                    if (!foundHp.has(ent) && ev.hp !== undefined) {
                      member.hp = Number(ev.hp);
                      member.isDead = (member.hp <= 0);
                      foundHp.add(ent);
                    }
                    if (!foundMaxHp.has(ent) && (ev.maxHp !== undefined || ev.max !== undefined)) {
                      member.maxHp = Number(ev.maxHp || ev.max);
                      foundMaxHp.add(ent);
                    }
                  }
                }
              }
            } catch {}
          }
        }
      }
    } catch {}
  }

  // Ensure default world is always set
  if (!state.location.mapId || !db.worlds[state.location.mapId]) {
    updateMapAndArea(null);
    changed = true;
  }

  if (changed) notifyClients();
}

// ============================================================================
// Binary Packet Reader & Aion 2 Opcode Parsers (Direct from Questlog combat.cjs)
// ============================================================================
const textDecoder = new TextDecoder();
const EMPTY_U8 = new Uint8Array(0);

class PacketReader {
  constructor(bytes, pos, end) {
    this.bytes = bytes;
    this.pos = pos;
    this.end = end;
    this.status = 0;
    this.bitAt = 0;
    this.bit = 0;
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  }
  get exact() {
    return this.status === 0 && this.pos === this.end;
  }
  take(n) {
    if (this.status !== 0) return -1;
    if (this.pos >= this.end || this.pos + n > this.end) {
      this.status = 4;
      return -1;
    }
    const p = this.pos;
    this.pos += n;
    return p;
  }
  u8() { const p = this.take(1); return p < 0 ? 0 : this.view.getUint8(p); }
  u16() { const p = this.take(2); return p < 0 ? 0 : this.view.getUint16(p, true); }
  u32() { const p = this.take(4); return p < 0 ? 0 : this.view.getUint32(p, true); }
  i32() { const p = this.take(4); return p < 0 ? 0 : this.view.getInt32(p, true); }
  f32() { const p = this.take(4); return p < 0 ? 0 : this.view.getFloat32(p, true); }
  i64() { const p = this.take(8); return p < 0 ? 0n : this.view.getBigInt64(p, true); }
  v32() {
    if (this.status !== 0) return 0;
    if (this.pos >= this.end) { this.status = 4; return 0; }
    let val = 0, shift = 0, i = this.pos;
    for (;;) {
      if (i >= this.end) { this.status = 4; return val >>> 0; }
      const b = this.bytes[i++];
      val = (val | (b & 127) << (shift & 31)) >>> 0;
      shift += 7;
      if ((b & 128) === 0) break;
    }
    this.pos = i;
    return val;
  }
  vi32() { return this.v32() | 0; }
  vi64() {
    if (this.status !== 0) return 0n;
    if (this.pos >= this.end) { this.status = 4; return 0n; }
    let val = 0n, shift = 0n, i = this.pos;
    for (;;) {
      if (i >= this.end) { this.status = 4; return BigInt.asIntN(64, val); }
      const b = this.bytes[i++];
      val = BigInt.asUintN(64, val | BigInt(b & 127) << (shift & 63n));
      shift += 7n;
      if ((b & 128) === 0) break;
    }
    this.pos = i;
    return BigInt.asIntN(64, val);
  }
  str() {
    if (this.status !== 0) return "";
    const start = this.pos, len = this.v32();
    if (this.status !== 0) return "";
    if (this.pos + len > this.end) { this.status = 4; this.pos = start + len; return ""; }
    let actual = len;
    if (actual > 0 && this.bytes[this.pos + actual - 1] === 0) actual--;
    const s = actual > 0 ? textDecoder.decode(this.bytes.subarray(this.pos, this.pos + actual)) : "";
    this.pos += len;
    return s;
  }
  bool() {
    if (this.status === 0 && this.pos >= this.end) this.status = 4;
    if (this.status !== 0) {
      if (this.status !== 4 || this.pos !== this.end || this.bitAt === 0) return false;
      this.status = 0;
    } else if (this.bitAt === 0) {
      this.bitAt = this.pos;
      this.bit = 0;
      this.pos++;
    }
    const res = (this.bytes[this.bitAt] & (1 << this.bit)) !== 0;
    this.bit++;
    if (this.bit > 7) { this.bitAt = 0; this.bit = 0; }
    return res;
  }
  list(fn) {
    const len = this.v32();
    if (len > 2147483646 || len > (this.end - this.pos) * 8 + 64) {
      this.status = 4;
      return [];
    }
    return Array.from({ length: len }, () => fn(this));
  }
  map(fn) { return new Map(this.list(fn)); }
  optional(cond, fn) { return fn(cond ? this : new PacketReader(EMPTY_U8, 0, 0)); }
}

function lz4DecompressBlock(src, uncompressedLen) {
  if (!Number.isSafeInteger(uncompressedLen) || uncompressedLen < 0 || uncompressedLen > src.length * 255) return;
  const out = new Uint8Array(uncompressedLen);
  let i = 0, n = 0;
  function readLen(len) {
    if (len === 15) {
      let b;
      do {
        if (i >= src.length) return -1;
        b = src[i++];
        len += b;
      } while (b === 255);
    }
    return len;
  }
  for (;;) {
    if (i >= src.length) return;
    const token = src[i++];
    const litLen = readLen(token >> 4);
    if (litLen < 0 || n + litLen > uncompressedLen || i + litLen > src.length) return;
    out.set(src.subarray(i, i + litLen), n);
    i += litLen;
    n += litLen;
    if (i === src.length) return n === uncompressedLen ? out : undefined;
    if (i + 2 > src.length) return;
    const offset = src[i] | (src[i + 1] << 8);
    i += 2;
    if (offset === 0 || offset > n) return;
    const matchLen = readLen(token & 15);
    if (matchLen < 0 || n + matchLen + 4 > uncompressedLen) return;
    const end = n + matchLen + 4;
    for (let ref = n - offset; n < end; n++, ref++) out[n] = out[ref];
  }
}

function readVec3(e) { return { x: e.f32(), y: e.f32(), z: e.f32() }; }
function readStat(e) { return { statType: e.u16(), statValue: e.i32() }; }
function readAbnormal(e) {
  const t = e.u8();
  return {
    uid: e.vi32(), abnormalId: e.i32(), durationMs: e.i64(), expireTime: e.i64(),
    casterKey: t & 1 ? e.vi32() : 0, abnormalLevel: e.u8(), skillId: t & 2 ? e.i32() : 0,
    readyToRemove: e.bool(), multiplyCasterKey: t & 4 ? e.vi32() : 0,
    multiplyValue: t & 8 ? e.i32() : 0, rangeBase: e.optional(t & 16, readVec3)
  };
}
function readDecoSlot(e) {
  return {
    slot: e.u8(), colorR: e.u8(), colorG: e.u8(), colorB: e.u8(), colorRemove: e.bool(),
    materialId: e.i32(), patternId: e.i32(), patternColorR: e.u8(), patternColorG: e.u8(),
    patternColorB: e.u8(), patternColorRemove: e.bool(), patternColorOpacity: e.u16(),
    patternOpacity: e.u16(), patternSizeHorizontal: e.u16(), patternSizeVertical: e.u16(),
    patternType: e.u8(), patternAdditional: e.bool(), patternGlossy: e.u16(), patternMetallic: e.u16()
  };
}
function readVisualEquip(e) {
  return {
    itemId: e.i32(), enchantLevel: e.u8(), visualEquipSlot: e.u8(), periodExpireTime: e.i64(),
    isPeriodExpired: e.bool(), skinId: e.i32(), decoSlots: e.list(readDecoSlot),
    godstoneSlots: e.list(t => t.i32()), display: e.bool(), defaultMesh: e.bool(),
    override: e.bool(), fxBit: e.vi32()
  };
}
function readEntityDesc(e) {
  const t = e.u8();
  return {
    nickname: t & 1 ? e.str() : "", pcId: e.i32(), race: t & 2 ? e.u8() : 0,
    gender: t & 4 ? e.u8() : 0,
    anonymousData: e.optional(t & 8, r => ({ type: r.u8(), index: r.i32() })),
    overrideRace: t & 16 ? e.u8() : 0
  };
}
function readEntityState(e) {
  const t = e.u16();
  return {
    pos: readVec3(e), dir: e.f32(), moveDir: e.u16(),
    posLocal: e.optional(t & 1, readVec3), dirLocal: t & 2 ? e.f32() : 0,
    moveDirLocal: t & 4 ? e.u16() : 0, moveVelocity3d: e.optional(t & 8, readVec3),
    moveVelocity3dLocal: e.optional(t & 16, readVec3), targetKey: t & 32 ? e.i32() : 0,
    live: e.u8(), hp: e.vi64(), hpMax: e.vi64(), mp: e.i32(), mpMax: e.i32(),
    ap: e.i32(), apMax: e.i32(), sp: e.i32(), spMax: e.i32(), dp: e.i32(), dpMax: e.i32(),
    dpStep: e.i32(), op: e.i32(), opMax: e.i32(), fp: e.i32(), fpMax: e.i32(),
    combatState: t & 64 ? e.u8() : 0, toggleFlags: t & 128 ? e.i32() : 0,
    phase: t & 256 ? e.u8() : 0, relationshipEntity: t & 512 ? e.u8() : 0,
    lockonTargetKey: t & 1024 ? e.vi32() : 0, lockonTargetPartsId: t & 2048 ? e.i32() : 0,
    wingId: t & 4096 ? e.i32() : 0, wingLevel: t & 8192 ? e.u8() : 0,
    wingSkinId: t & 16384 ? e.i32() : 0, titleId: t & 32768 ? e.i32() : 0
  };
}
function readGuildDesc(e) {
  return {
    guildId: e.i64(), guildName: e.str(), guildEmblemId: e.u16(), guildMemberGrade: e.u8(),
    guildRanking: e.i32(), guildUnionId: e.i64(), guildUnionEmblemId: e.u16(), guildUnionName: e.str()
  };
}
function readGroggy(e) {
  const t = e.u8();
  return {
    state: e.u8(), groggyGuardMaxEnergy: t & 1 ? e.i32() : 0, groggyGuardEnergy: t & 2 ? e.i32() : 0,
    groggyAttackCount: t & 4 ? e.i32() : 0, groggyAttackDurationMs: t & 8 ? e.i64() : 0n,
    groggyAttackExpireTime: t & 16 ? e.i64() : 0n, groggyPause: e.bool(),
    groggyCooltimeExpireTime: t & 32 ? e.i64() : 0n, groggyActivated: e.bool()
  };
}
function readSkillPoint(e) { return { pointType: e.u8(), point: e.u16() }; }
function readMyCharDetail(e) {
  const t = e.u8();
  return {
    nickname: t & 1 ? e.str() : "", serverId: e.u16(), pcId: e.i32(), race: t & 2 ? e.u8() : 0,
    charLevel: e.i32(), equipItemLevel: e.i32(), highestTotalItemLevel: e.i32(), levelMax: e.i32(),
    exp: e.i64(), combatLevel: e.i32(), combatExp: e.i64(), gender: t & 4 ? e.u8() : 0,
    gatherSourceStats: e.list(r => ({ gatherLevel: r.u16(), gatherExp: r.i64(), gatherSkillPoint: r.u16(), gatherSpecializedPoint: r.u16() })),
    craftStats: e.list(r => ({ craftCategory: r.u8(), craftLevel: r.u16(), craftExp: r.i64(), craftSkillPoint: r.u16() })),
    glideCooltime: e.i32(), sprintCooltime: e.i32(), reputation: e.i64(), ascension: e.u8(),
    overrideRace: t & 8 ? e.u8() : 0, skillPoints: t & 16 ? e.list(readSkillPoint) : [],
    bonusSkillPoints: t & 32 ? e.list(readSkillPoint) : []
  };
}
function readMappin(e) {
  const t = e.u8();
  return {
    mappinIdx: e.u8(), mappinName: e.str(), mapId: e.i32(), pos: readVec3(e),
    updateTime: e.i64(), dbid: e.i64(), removeMemberList: t & 1 ? e.list(r => r.i64()) : []
  };
}

const OPCODES = new Map([
  [13857, {
    name: "PrepareEnterMap_NT",
    read: e => ({
      enterMapTicket: e.i32(), mapId: e.i32(), gameTime: e.i64(), pos: readVec3(e),
      dir: e.f32(), reason: e.u8(), envState: e.u8(), envStartTime: e.i64(),
      groupEnvState: e.list(t => ({ groupId: t.i32(), envState: t.u8(), envStartTime: t.i64() })),
      teleportEffectId: e.vi32(), cutsceneName: e.str(), informType: e.u8()
    })
  }],
  [13875, {
    name: "MyCharAppear_NT",
    read: e => {
      const key = e.vi32(), r = e.u32();
      return {
        key, detail: readMyCharDetail(e), state: readEntityState(e),
        guildDesc: e.optional(r & 1, readGuildDesc), stats: e.list(readStat),
        abnormals: e.list(readAbnormal), equips: e.list(readVisualEquip),
        reason: r & 2 ? e.u8() : 0, mapKey: e.i64(), mapId: e.i32(),
        subzoneId: r & 4 ? e.i32() : 0, prevSubzoneId: r & 8 ? e.i32() : 0,
        prevPos: e.optional(r & 16, readVec3), channelNo: r & 32 ? e.u16() : 0,
        dbid: e.i64(), lastOnGroundPosZ: r & 64 ? e.f32() : 0,
        weatherId: r & 128 ? e.i32() : 0, weatherType: r & 256 ? e.u8() : 0,
        moveState: r & 512 ? e.u8() : 0, moveSubstate: r & 1024 ? e.u8() : 0,
        deadReckoningOn: r & 2048 ? e.bool() : false, teleportEffectId: r & 4096 ? e.vi32() : 0,
        customizingData: e.list(i => i.u8()), isPurchasedCustomizing: e.bool(),
        voiceType: r & 8192 ? e.u8() : 0, airspacePoint: r & 16384 ? e.vi32() : 0,
        sealstoneItemId: r & 32768 ? e.i32() : 0, abyssRankingPoint: r & 65536 ? e.i64() : 0n,
        mappinInfos: r & 131072 ? e.list(readMappin) : [], rideDataId: r & 262144 ? e.i32() : 0,
        arcanaExtractPoint: r & 524288 ? e.i32() : 0, arcanaExtractPoint2: r & 1048576 ? e.i32() : 0,
        arcanaCreateEndTime: r & 2097152 ? e.i64() : 0n,
        lastDeathInfo: e.optional(r & 4194304, i => ({ regTime: i.i64(), pcKillerServerId: i.u16(), pcKillerNickname: i.str(), monsterKillerDataId: i.i32(), dieType: i.u8() })),
        matchServerId: r & 8388608 ? e.u16() : 0,
        conquerorInfo: { conquerorLevel: e.i32(), conquerorLevelMax: e.i32(), conquerorExp: e.i64() },
        riftPvpOn: r & 16777216 ? e.bool() : false, riftPvpSetTime: r & 33554432 ? e.i64() : 0n,
        polymorphAbnormalUid: r & 67108864 ? e.i32() : 0,
        successionCountInfo: e.optional(r & 134217728, i => ({ successionCount: i.i32(), advancedSuccessionCount: i.i32(), restoreSuccessionCount: i.i32(), restoreAdvancedSuccessionCount: i.i32() })),
        combatPower: e.i64(), highestCombatPower: e.i64(),
        usedDaevanionPointInfos: e.list(readSkillPoint), bonusDaevanionPointInfos: e.list(readSkillPoint),
        trialClearInfo: r & 268435456 ? e.list(i => ({ dungeonId: i.i32(), clearLevel: i.i32() })) : [],
        rolevoteCount: r & 536870912 ? e.map(i => [i.i32(), i.i32()]) : new Map()
      };
    }
  }],
  [13889, {
    name: "NpcAppear_NT",
    read: e => {
      const key = e.vi32(), r = e.u16();
      return {
        key, desc: readEntityDesc(e), state: readEntityState(e), abnormals: e.list(readAbnormal),
        moveState: r & 1 ? e.u8() : 0, moveSubstate: r & 2 ? e.u8() : 0,
        reason: r & 4 ? e.u8() : 0, aggressive: r & 8 ? e.bool() : false,
        parentKey: r & 16 ? e.i32() : 0, parentDesc: e.optional(r & 32, readEntityDesc),
        parentGuildDesc: e.optional(r & 64, readGuildDesc), stats: e.list(readStat),
        charLevel: e.i32(), waypointsId: r & 128 ? e.i64() : 0n,
        waypointsElapsedFrame: r & 256 ? e.i32() : 0, groggyInfo: e.optional(r & 512, readGroggy),
        socialAction: e.optional(r & 1024, i => ({ id: i.i32(), state: i.u8(), envObjPropKey: i.i32() })),
        socialActionStartTime: r & 2048 ? e.i64() : 0n, spawnActId: r & 4096 ? e.i32() : 0,
        spawnUniqueKey: r & 8192 ? e.i32() : 0, tags: e.list(i => i.i32()),
        bossBerserkApplyTime: r & 16384 ? e.i64() : 0n, polymorphAbnormalUid: r & 32768 ? e.i32() : 0
      };
    }
  }],
  [13890, {
    name: "NpcDisappear_NT",
    read: e => {
      const key = e.vi32(), r = e.u8();
      return { key, reason: e.u8(), despawnActId: r & 1 ? e.i32() : 0 };
    }
  }],
  [14106, {
    name: "CharMoveStop_NT",
    read: e => {
      const key = e.vi32(), r = e.u8();
      return {
        key, moveState: e.u8(), moveSubstate: r & 1 ? e.u8() : 0, pos: readVec3(e),
        rotateDir: e.u16(),
        localCoord: e.optional(r & 2, i => ({ vesselKey: i.i32(), pos: readVec3(i), rotateDir: i.v32() & 65535 })),
        penaltyOn: r & 4 ? e.bool() : false, penaltyMs: r & 8 ? e.i64() : 0n
      };
    }
  }],
  [14107, {
    name: "CharMoveStart_NT",
    read: e => {
      const key = e.vi32(), r = e.u8();
      return {
        key, isRunning: r & 1 ? e.bool() : false, pos: readVec3(e),
        moveDir: e.u16(), rotateDir: e.u16(),
        localCoord: e.optional(r & 2, i => ({ vesselKey: i.i32(), pos: readVec3(i), moveDir: i.v32() & 65535, rotateDir: i.v32() & 65535 })),
        deadReckoningOn: r & 4 ? e.bool() : false
      };
    }
  }],
  [14108, {
    name: "CharMoveUpdateSnapshot_NT",
    read: e => {
      const key = e.vi32(), r = e.u8();
      return {
        key, isRunning: r & 1 ? e.bool() : false, pos: readVec3(e),
        moveDir: e.u16(), rotateDir: e.u16(),
        localCoord: e.optional(r & 2, i => ({ vesselKey: i.i32(), pos: readVec3(i), moveDir: i.v32() & 65535, rotateDir: i.v32() & 65535 })),
        deadReckoningOn: r & 4 ? e.bool() : false
      };
    }
  }],
  [36100, {
    name: "CharDie_NT",
    read: e => ({
      key: e.vi32(), skillId: e.i32(), casterKey: e.vi32(), pcCasterServerId: e.u16(),
      pcCasterNickname: e.str(), pcCasterGuildName: e.str(), pcCasterGuildEmblemId: e.u16(),
      npcCasterDataId: e.vi32(), npcDieActId: e.i32(), reason: e.u8(), dieType: e.u8()
    })
  }],
  [36864, {
    name: "RideInitData_NT",
    read: e => ({
      defaultRideDataId: e.i32(),
      acquiredRideMap: e.map(t => [t.i32(), { rideDataId: t.i32(), level: t.i32() }]),
      acquiredRideSoulPointMap: e.map(t => [t.i32(), t.i32()]),
      acquiredRideSoulCrystalMap: e.map(t => [t.u8(), t.i32()]),
      acquiredRideCollectionList: e.list(t => t.i32()),
      creatureBoardMap: e.map(t => [t.u8(), {
        creatureType: t.u8(), level: t.i32(), exp: t.i64(),
        statSlotPanels: t.map(r => [r.u8(), {
          statSlotList: r.list(s => ({
            slotIdx: s.u8(), grade: s.u8(), statType: s.u16(),
            statValue: s.i64(), lock: s.bool(), activated: s.bool()
          }))
        }])
      }]),
      creatureBoardSelectedPanelNo: e.u8()
    })
  }],
  [36873, {
    name: "CharRideAcquire_NT",
    read: e => ({
      key: e.vi32(), rideDataId: e.i32(), rideLevel: e.i32(),
      creatureBoardExpInfo: { creatureType: e.u8(), level: e.i32(), exp: e.i64() }
    })
  }],
  [36877, {
    name: "RideSoulPointIncrease_NT",
    read: e => ({
      increasedSoulPoints: e.list(t => ({ rideDataId: t.i32(), point: t.i32() }))
    })
  }],
  [36878, {
    name: "RideSoulPointDecrease_NT",
    read: e => ({
      decreasedSoulPoints: e.list(t => ({ rideDataId: t.i32(), point: t.i32() }))
    })
  }],
  [36096, {
    name: "CharPointUpdate_NT",
    read: e => {
      const key = e.vi32(), r = e.u8();
      return {
        key,
        updates: r & 1 ? e.map(i => [i.u8(), i.i32()]) : new Map(),
        updates64: r & 2 ? e.map(i => [i.u8(), i.i64()]) : new Map()
      };
    }
  }],
  [37403, {
    name: "PartyUpdateMemberHPMP_NT",
    read: e => ({
      key: e.vi32(),
      hp: e.vi64(),
      hpMax: e.vi64(),
      mp: e.i32(),
      mpMax: e.i32(),
      live: e.bool()
    })
  }],
  [38443, {
    name: "ForceUpdateMemberHPMP_NT",
    read: e => ({
      key: e.vi32(),
      hp: e.vi64(),
      hpMax: e.vi64(),
      mp: e.i32(),
      mpMax: e.i32(),
      live: e.bool()
    })
  }],
  [14378, {
    name: "AbnormalAdd_NT",
    read: e => ({
      key: e.vi32(),
      abnormal: {
        id: e.i32(),
        casterKey: e.vi32()
      }
    })
  }],
  [14380, {
    name: "AbnormalRemoveList_NT",
    read: e => ({
      key: e.vi32(),
      abnormals: e.list(t => t.i32())
    })
  }]
]);

function readVarintHeader(buf, start, end) {
  let length = 0, shift = 0;
  for (let o = start; o < end; o++) {
    const b = buf[o];
    length = (length | (b & 127) << (shift & 31)) >>> 0;
    shift += 7;
    if ((b & 128) === 0) {
      return { length, size: o - start + 1 };
    }
  }
}

function getFrameSize(buf, start, end) {
  if (end - start < 3) return;
  const h = readVarintHeader(buf, start, end);
  if (!h) return;
  const n = 4 - h.size;
  if (n < 0 || h.length < n) return -1;
  const size = h.length - n;
  return size < h.size + 2 ? -1 : size;
}

function decodeBundleFrames(buf, start, end, at, out) {
  let pos = start;
  while (pos < end) {
    const s = getFrameSize(buf, pos, end);
    if (s === undefined) break;
    if (s < 0 || pos + s > end) return -1;
    if (decodeSingleFrame(buf, pos, pos + s, at, out) === "broken") return -1;
    pos += s;
  }
  return pos;
}

function decodeSingleFrame(buf, start, end, at, out) {
  const h = readVarintHeader(buf, start, end);
  const payloadStart = start + h.size + 2;
  const opcode = buf[start + h.size] | (buf[start + h.size + 1] << 8);
  if (opcode === 65535) {
    if (end - payloadStart <= 4) return "broken";
    const uncompressedLen = new DataView(buf.buffer, buf.byteOffset + payloadStart, 4).getUint32(0, true);
    const decompressed = lz4DecompressBlock(buf.subarray(payloadStart + 4, end), uncompressedLen);
    if (!decompressed || decodeBundleFrames(decompressed, 0, decompressed.length, at, out) !== decompressed.length) {
      return "broken";
    }
    return "decoded";
  }
  const spec = OPCODES.get(opcode);
  if (!spec) return "skipped";
  const reader = new PacketReader(buf, payloadStart, end);
  let data;
  try {
    data = spec.read(reader);
  } catch {
    return "skipped";
  }
  if (reader.exact) {
    out.push({ name: spec.name, opcode, at, data });
    return "decoded";
  }
  return "skipped";
}

function createPacketFramer() {
  let buf = new Uint8Array(0);
  const MAX_FRAME = 64 * 1024;
  return {
    push(chunk, at) {
      const merged = new Uint8Array(buf.length + chunk.length);
      merged.set(buf);
      merged.set(chunk, buf.length);
      const out = [];
      let pos = 0;
      while (pos < merged.length) {
        const size = getFrameSize(merged, pos, merged.length);
        if (size === undefined) break;
        if (size < 0 || size > MAX_FRAME) {
          pos++;
          continue;
        }
        if (pos + size > merged.length) break;
        const res = decodeSingleFrame(merged, pos, pos + size, at, out);
        if (res === "broken") {
          pos++;
          continue;
        }
        pos += size;
      }
      buf = merged.slice(pos);
      return out;
    },
    gap() {
      buf = new Uint8Array(0);
    }
  };
}

function rebuildAroundCounts() {
  const counts = {};
  for (const npc of state.liveNpcs.values()) {
    counts[npc.npc] = (counts[npc.npc] || 0) + 1;
  }
  state.aroundCounts = counts;
  updatePlayerPosFromLiveNpcs();
}

function updatePlayerPosFromLiveNpcs() {
  if (state.liveNpcs.size === 0) return;
  const now = Date.now();
  // Use recently updated NPCs (within 45s) to estimate player position when walking
  const recent = [];
  for (const npc of state.liveNpcs.values()) {
    if (now - (npc.updatedAt || 0) < 45000) recent.push(npc);
  }
  const list = recent.length > 0 ? recent : [...state.liveNpcs.values()];
  if (list.length === 0) return;
  let sx = 0, sy = 0, sz = 0;
  for (const n of list) {
    sx += n.x;
    sy += n.y;
    sz += (n.z || 0);
  }
  updateMapAndArea(null, sx / list.length, sy / list.length, sz / list.length);
}

// Track pending soul spend for level calculation (matches w1 in combat.cjs)
const pendingDecrease = new Map();
const acquireCredit = new Map();

function handleDecodedPacket(pkt) {
  state.sniffer.packetsDecoded++;
  state.sniffer.lastPacketAt = Date.now();
  state.sniffer.lastPacketName = pkt.name;
  let dirty = false;

  switch (pkt.name) {
    case "PrepareEnterMap_NT": {
      state.liveNpcs.clear();
      state.aroundCounts = {};
      updateMapAndArea(pkt.data.mapId, pkt.data.pos?.x, pkt.data.pos?.y, pkt.data.pos?.z);
      dirty = true;
      break;
    }
    case "MyCharAppear_NT": {
      const { key, detail, state: entState, mapId } = pkt.data;
      state.selfEntity = key;
      if (detail.nickname) state.character.name = detail.nickname;
      if (detail.serverId) state.character.server = detail.serverId;
      if (detail.charLevel) state.character.level = detail.charLevel;
      if (detail.race) state.character.race = detail.race === 1 ? "light" : "dark";
      if (entState) {
        if (entState.hp) state.cleric.self.hp = Number(entState.hp);
        if (entState.hpMax) state.cleric.self.maxHp = Number(entState.hpMax);
        if (entState.mp) state.cleric.self.mp = entState.mp;
        if (entState.mpMax) state.cleric.self.maxMp = entState.mpMax;
        state.cleric.self.isDead = (state.cleric.self.hp <= 0);
      }
      if (detail && detail.nickname) state.cleric.self.name = detail.nickname;
      if (detail && detail.charLevel) state.cleric.self.level = detail.charLevel;
      state.cleric.self.entity = key;
      updateMapAndArea(mapId, entState.pos?.x, entState.pos?.y, entState.pos?.z);
      dirty = true;
      break;
    }
    case "CharMoveStart_NT":
    case "CharMoveUpdateSnapshot_NT":
    case "CharMoveStop_NT": {
      const { key, pos, rotateDir, moveDir } = pkt.data;
      if (key === state.selfEntity) {
        let heading = null;
        if (typeof rotateDir === "number") {
          heading = (rotateDir / 65536.0) * 360.0;
        } else if (typeof moveDir === "number") {
          heading = (moveDir / 65536.0) * 360.0;
        } else if (typeof pkt.data.dir === "number") {
          heading = (pkt.data.dir * 180.0) / Math.PI;
        }
        updateMapAndArea(null, pos.x, pos.y, pos.z, false, heading);
        dirty = true;
      } else {
        const npc = state.liveNpcs.get(key);
        if (npc) {
          npc.x = Math.round(pos.x);
          npc.y = Math.round(pos.y);
          npc.z = Math.round(pos.z);
          npc.updatedAt = Date.now();
        }
      }
      break;
    }
    case "NpcAppear_NT": {
      const d = pkt.data;
      if (!d.parentKey && d.state.hp > 0n) {
        state.liveNpcs.set(d.key, {
          entity: d.key,
          npc: d.desc.pcId,
          level: d.charLevel,
          x: Math.round(d.state.pos.x),
          y: Math.round(d.state.pos.y),
          z: Math.round(d.state.pos.z),
          hp: Number(d.state.hp),
          maxHp: Number(d.state.hpMax),
          updatedAt: Date.now()
        });
        rebuildAroundCounts();
        dirty = true;
      }
      break;
    }
    case "NpcDisappear_NT": {
      if (state.liveNpcs.delete(pkt.data.key)) {
        rebuildAroundCounts();
        dirty = true;
      }
      break;
    }
    case "CharDie_NT": {
      if (pkt.data.key === state.selfEntity) {
        state.cleric.self.isDead = true;
        state.cleric.self.hp = 0;
        dirty = true;
      }
      const deadMember = state.cleric.party.find(p => p.entity === pkt.data.key);
      if (deadMember) {
        deadMember.isDead = true;
        deadMember.hp = 0;
        dirty = true;
      }
      const npc = state.liveNpcs.get(pkt.data.key);
      if (npc) {
        state.kills[npc.npc] = (state.kills[npc.npc] || 0) + 1;
        state.liveNpcs.delete(pkt.data.key);
        rebuildAroundCounts();
        dirty = true;
      }
      break;
    }
    case "CharPointUpdate_NT": {
      const { key, updates, updates64 } = pkt.data;
      const getVal = (id) => {
        if (updates && updates.has(id)) return updates.get(id);
        if (updates64 && updates64.has(id)) return Number(updates64.get(id));
        return undefined;
      };
      const hp = getVal(0);
      const maxHp = getVal(7);
      const mp = getVal(1);
      const maxMp = getVal(8);
      if (key === state.selfEntity || !state.selfEntity || state.cleric.self.entity === key) {
        if (hp !== undefined) state.cleric.self.hp = hp;
        if (maxHp !== undefined) state.cleric.self.maxHp = maxHp;
        if (mp !== undefined) state.cleric.self.mp = mp;
        if (maxMp !== undefined) state.cleric.self.maxMp = maxMp;
        state.cleric.self.isDead = (state.cleric.self.hp <= 0);
        dirty = true;
      }
      let member = state.cleric.party.find(p => p.entity === key);
      if (member) {
        if (hp !== undefined) member.hp = hp;
        if (maxHp !== undefined) member.maxHp = maxHp;
        member.isDead = (member.hp <= 0);
        dirty = true;
      }
      break;
    }
    case "PartyUpdateMemberHPMP_NT":
    case "ForceUpdateMemberHPMP_NT": {
      const { key, hp, hpMax, mp, maxMp, live } = pkt.data;
      if (key === state.selfEntity) {
        state.cleric.self.hp = Number(hp);
        state.cleric.self.maxHp = Number(hpMax);
        state.cleric.self.isDead = !live || (state.cleric.self.hp <= 0);
        dirty = true;
      } else {
        let member = state.cleric.party.find(p => p.entity === key);
        if (!member) {
          member = { entity: key, name: "Party_" + key, hp: Number(hp), maxHp: Number(hpMax), debuffs: [], isDead: !live };
          state.cleric.party.push(member);
        } else {
          member.hp = Number(hp);
          member.maxHp = Number(hpMax);
          member.isDead = !live || (member.hp <= 0);
        }
        dirty = true;
      }
      break;
    }
    case "AbnormalAdd_NT": {
      const { key, abnormal } = pkt.data;
      const target = (key === state.selfEntity) ? state.cleric.self : state.cleric.party.find(p => p.entity === key);
      if (target && abnormal && abnormal.id) {
        if (!target.debuffs.includes(abnormal.id)) target.debuffs.push(abnormal.id);
        dirty = true;
      }
      break;
    }
    case "AbnormalRemoveList_NT": {
      const { key, abnormals } = pkt.data;
      const target = (key === state.selfEntity) ? state.cleric.self : state.cleric.party.find(p => p.entity === key);
      if (target && abnormals) {
        target.debuffs = target.debuffs.filter(d => !abnormals.includes(d));
        dirty = true;
      }
      break;
    }
    case "RideInitData_NT": {
      pendingDecrease.clear();
      acquireCredit.clear();
      const newLevels = {};
      for (const [, val] of pkt.data.acquiredRideMap) {
        newLevels[val.rideDataId] = val.level;
      }
      const newSouls = {};
      for (const [petId, points] of pkt.data.acquiredRideSoulPointMap) {
        newSouls[petId] = points;
      }
      state.levels = newLevels;
      state.souls = newSouls;
      state.character.confirmedAt = Date.now();
      saveLocalState();
      pushRecentEvent({ type: "init", text: `נטענו נתוני חיות מהמשחק (${Object.keys(newLevels).length} חיות)` });
      dirty = true;
      break;
    }
    case "CharRideAcquire_NT": {
      const { rideDataId, rideLevel } = pkt.data;
      if (!state.levels[rideDataId]) {
        acquireCredit.set(rideDataId, STEPS[0]);
      }
      if ((state.levels[rideDataId] || 0) < rideLevel) {
        state.levels[rideDataId] = rideLevel;
      }
      const petInfo = db.pets.find(p => p.id === rideDataId);
      pushRecentEvent({
        type: "levelup",
        petId: rideDataId,
        petName: petInfo ? petInfo.name : `#${rideDataId}`,
        level: rideLevel,
        text: `${petInfo ? petInfo.name : "#" + rideDataId} עלה לשלב ${rideLevel}!`
      });
      saveLocalState();
      dirty = true;
      break;
    }
    case "RideSoulPointIncrease_NT": {
      for (const { rideDataId, point } of pkt.data.increasedSoulPoints) {
        state.souls[rideDataId] = (state.souls[rideDataId] || 0) + point;
        state.soulsGained[rideDataId] = (state.soulsGained[rideDataId] || 0) + point;
        const petInfo = db.pets.find(p => p.id === rideDataId);
        const prog = getPetProgress(state.levels[rideDataId] || 0, state.souls[rideDataId] || 0);
        pushRecentEvent({
          type: "soul",
          petId: rideDataId,
          petName: petInfo ? petInfo.name : `#${rideDataId}`,
          points: point,
          remainingToLevel3: prog.remainingToLevel3,
          text: `+${point} נשמה: ${petInfo ? petInfo.name : "#" + rideDataId} (נשאר לשלב 3: ${prog.remainingToLevel3})`
        });
      }
      saveLocalState();
      dirty = true;
      break;
    }
    case "RideSoulPointDecrease_NT": {
      for (const { rideDataId, point } of pkt.data.decreasedSoulPoints) {
        const credit = Math.min(point, acquireCredit.get(rideDataId) || 0);
        acquireCredit.set(rideDataId, (acquireCredit.get(rideDataId) || 0) - credit);
        const rem = point - credit;
        if (rem > 0) {
          const curLv = state.levels[rideDataId] || 0;
          const gain = calcStepGain(curLv, rem);
          if (gain > 0) {
            state.levels[rideDataId] = curLv + gain;
          } else {
            pendingDecrease.set(rideDataId, (pendingDecrease.get(rideDataId) || 0) + rem);
            const tot = pendingDecrease.get(rideDataId);
            const g2 = calcStepGain(curLv, tot);
            if (g2 > 0) {
              state.levels[rideDataId] = curLv + g2;
              pendingDecrease.delete(rideDataId);
            }
          }
        }
        state.souls[rideDataId] = Math.max(0, (state.souls[rideDataId] || 0) - point);
      }
      saveLocalState();
      dirty = true;
      break;
    }
  }

  if (dirty) notifyClients();
}

// ============================================================================
// Npcap + Win32 Live Sniffer Setup
// ============================================================================
function startNpcapSniffer() {
  if (!fs.existsSync(KOFFI_PATH) || !fs.existsSync(path.join(NPCAP_DIR, "wpcap.dll"))) {
    state.sniffer.status = "questlog-sync";
    return;
  }
  try {
    const koffi = require(KOFFI_PATH);
    const kernel32 = koffi.load("kernel32.dll");
    const iphlpapi = koffi.load("iphlpapi.dll");
    const wpcap = koffi.load(path.join(NPCAP_DIR, "wpcap.dll"));

    const PROCESSENTRY32W = koffi.struct("PROCESSENTRY32W", {
      dwSize: "uint32", cntUsage: "uint32", th32ProcessID: "uint32",
      th32DefaultHeapID: "uintptr", th32ModuleID: "uint32", cntThreads: "uint32",
      th32ParentProcessID: "uint32", pcPriClassBase: "int32", dwFlags: "uint32",
      szExeFile: koffi.array("char16", 260, "String")
    });
    const processEntrySize = koffi.sizeof(PROCESSENTRY32W);
    const CreateToolhelp32Snapshot = kernel32.func("void* __stdcall CreateToolhelp32Snapshot(uint32 flags, uint32 pid)");
    const Process32FirstW = kernel32.func("bool __stdcall Process32FirstW(void* snapshot, _Inout_ PROCESSENTRY32W* entry)");
    const Process32NextW = kernel32.func("bool __stdcall Process32NextW(void* snapshot, _Inout_ PROCESSENTRY32W* entry)");
    const CloseHandle = kernel32.func("bool __stdcall CloseHandle(void* handle)");

    const GetExtendedTcpTable = iphlpapi.func("uint32 __stdcall GetExtendedTcpTable(void* table, _Inout_ uint32* size, bool order, uint32 family, int tableClass, uint32 reserved)");
    const GetIpAddrTable = iphlpapi.func("uint32 __stdcall GetIpAddrTable(void* table, _Inout_ uint32* size, bool order)");
    const ConvertInterfaceIndexToLuid = iphlpapi.func("uint32 __stdcall ConvertInterfaceIndexToLuid(uint32 index, void* luid)");
    const ConvertInterfaceLuidToGuid = iphlpapi.func("uint32 __stdcall ConvertInterfaceLuidToGuid(void* luid, void* guid)");

    const pcapIf = koffi.struct("pcap_if", { next: "void*", name: "str", description: "str", addresses: "void*", flags: "uint32" });
    const pcapAddr = koffi.struct("pcap_addr", { next: "void*", addr: "void*", netmask: "void*", broadaddr: "void*", dstaddr: "void*" });
    const pcapHeader = koffi.struct("pcap_pkthdr", { seconds: "int32", microseconds: "int32", caplen: "uint32", len: "uint32" });
    koffi.struct("bpf_program", { bf_len: "uint32", bf_insns: "void*" });

    const findalldevs = wpcap.func("int pcap_findalldevs(_Out_ void** devices, void* error)");
    const freealldevs = wpcap.func("void pcap_freealldevs(void* devices)");
    const openLive = wpcap.func("void* pcap_open_live(str device, int snaplen, int promisc, int timeout, void* error)");
    const setnonblock = wpcap.func("int pcap_setnonblock(void* handle, int nonblock, void* error)");
    const datalink = wpcap.func("int pcap_datalink(void* handle)");
    const compile = wpcap.func("int pcap_compile(void* handle, _Out_ bpf_program* program, str filter, int optimize, uint32 netmask)");
    const setfilter = wpcap.func("int pcap_setfilter(void* handle, bpf_program* program)");
    const freecode = wpcap.func("void pcap_freecode(bpf_program* program)");
    const nextEx = wpcap.func("int pcap_next_ex(void* handle, _Out_ void** header, _Out_ void** data)");
    const closePcap = wpcap.func("void pcap_close(void* handle)");

    function getAion2Pids() {
      const snap = CreateToolhelp32Snapshot(2, 0);
      if (!snap) return new Set();
      const pids = new Set();
      try {
        const entry = { dwSize: processEntrySize };
        if (Process32FirstW(snap, entry)) {
          do {
            if (typeof entry.szExeFile === "string" && /^aion2\.exe$/i.test(entry.szExeFile)) {
              pids.add(entry.th32ProcessID);
            }
          } while (Process32NextW(snap, entry));
        }
      } finally {
        CloseHandle(snap);
      }
      return pids;
    }

    function fmtIp(buf, off) {
      return `${buf[off]}.${buf[off + 1]}.${buf[off + 2]}.${buf[off + 3]}`;
    }

    function getTcpConnections(pids) {
      if (pids.size === 0) return [];
      const size = [0];
      GetExtendedTcpTable(null, size, false, 2, 5, 0);
      const buf = Buffer.alloc((size[0] || 0) + 384);
      size[0] = buf.length;
      if (GetExtendedTcpTable(buf, size, false, 2, 5, 0) !== 0) return [];
      const count = buf.readUInt32LE(0);
      const out = [];
      for (let i = 0; i < count; i++) {
        const off = 4 + i * 24;
        const st = buf.readUInt32LE(off);
        const pid = buf.readUInt32LE(off + 20);
        if (st === 5 && pids.has(pid)) {
          out.push({
            pid,
            localAddress: fmtIp(buf, off + 4),
            localPort: buf.readUInt16BE(off + 8),
            remoteAddress: fmtIp(buf, off + 12),
            remotePort: buf.readUInt16BE(off + 16)
          });
        }
      }
      return out;
    }

    function getGuidForIp(ip) {
      const size = [0];
      GetIpAddrTable(null, size, false);
      const buf = Buffer.alloc((size[0] || 0) + 96);
      size[0] = buf.length;
      if (GetIpAddrTable(buf, size, false) !== 0) return;
      const count = buf.readUInt32LE(0);
      for (let i = 0; i < count; i++) {
        const off = 4 + i * 24;
        if (fmtIp(buf, off) !== ip) continue;
        const luid = Buffer.alloc(8), guid = Buffer.alloc(16);
        if (ConvertInterfaceIndexToLuid(buf.readUInt32LE(off + 4), luid) !== 0) return;
        if (ConvertInterfaceLuidToGuid(luid, guid) !== 0) return;
        const c = guid.readUInt32LE(0).toString(16).padStart(8, "0");
        const l = guid.readUInt16LE(4).toString(16).padStart(4, "0");
        const f = guid.readUInt16LE(6).toString(16).padStart(4, "0");
        return `{${c}-${l}-${f}-${guid.toString("hex", 8, 10)}-${guid.toString("hex", 10, 16)}}`.toUpperCase();
      }
    }

    function listDevs() {
      const err = Buffer.alloc(256), ptr = [null];
      if (findalldevs(ptr, err) !== 0) return [];
      const list = [];
      try {
        for (let cur = ptr[0]; cur; ) {
          const d = koffi.decode(cur, pcapIf);
          const addrs = [];
          for (let a = d.addresses; a; ) {
            const pa = koffi.decode(a, pcapAddr);
            if (pa.addr && koffi.decode(pa.addr, "uint16_t") === 2) {
              addrs.push([...koffi.decode(pa.addr, "uint8_t", 8).subarray(4)].join("."));
            }
            a = pa.next;
          }
          list.push({ name: d.name, addresses: addrs });
          cur = d.next;
        }
      } finally {
        freealldevs(ptr[0]);
      }
      return list;
    }

    function findAdapterName(ip) {
      const devs = listDevs();
      if (ip.startsWith("127.")) return devs.find(d => /loopback/i.test(d.name))?.name;
      const direct = devs.find(d => d.addresses.includes(ip));
      if (direct) return direct.name;
      const guid = getGuidForIp(ip);
      return guid ? devs.find(d => d.name.toUpperCase().endsWith(`_${guid}`))?.name : undefined;
    }

    function createTcpAssembler(onPayload, onGap) {
      let nextSeq;
      const bufferMap = new Map();
      let bufferedBytes = 0;
      let bufferSince = 0;
      function emitChunk(seq, payload, at) {
        const skip = -(seq - nextSeq | 0);
        if (skip < payload.length) {
          onPayload(skip > 0 ? payload.subarray(skip) : payload, at);
          nextSeq = (seq + payload.length) >>> 0;
        }
      }
      function drain(at) {
        let more = true;
        while (more) {
          more = false;
          for (const [seq, payload] of bufferMap) {
            if ((seq - nextSeq | 0) <= 0) {
              bufferMap.delete(seq);
              bufferedBytes -= payload.length;
              emitChunk(seq, payload, at);
              more = true;
            }
          }
        }
      }
      return (seq, payload, syn, at) => {
        if (syn) { nextSeq = (seq + 1) >>> 0; return; }
        if (payload.length === 0) return;
        if (nextSeq === undefined) { nextSeq = seq; onGap(); }
        if ((seq - nextSeq | 0) > 0) {
          if (!bufferMap.has(seq)) {
            if (bufferMap.size === 0) bufferSince = Date.now();
            bufferMap.set(seq, payload);
            bufferedBytes += payload.length;
          }
          if (bufferedBytes <= (1 << 20) && Date.now() - bufferSince <= 1000) return;
          nextSeq = [...bufferMap.keys()].reduce((a, b) => ((b - a | 0) < 0 ? b : a));
          onGap();
        } else {
          emitChunk(seq, payload, at);
        }
        drain(at);
      };
    }

    function parseFrame(frame, linkType) {
      const view = new DataView(frame.buffer, frame.byteOffset, frame.byteLength);
      let ipOff = 0;
      if (linkType === 1) {
        let ethType = view.getUint16(12);
        ipOff = 14;
        if (ethType === 33024) { ethType = view.getUint16(16); ipOff = 18; }
        if (ethType !== 2048) return;
      } else if (linkType === 0 || linkType === 108) {
        ipOff = 4;
      } else if (linkType !== 12 && linkType !== 101) {
        return;
      }
      if (frame.length < ipOff + 20 || (view.getUint8(ipOff) >> 4) !== 4 || view.getUint8(ipOff + 9) !== 6) return;
      const totalLen = view.getUint16(ipOff + 2);
      const tcpOff = ipOff + (view.getUint8(ipOff) & 15) * 4;
      if (frame.length < tcpOff + 20) return;
      const end = totalLen > 0 ? Math.min(frame.length, ipOff + totalLen) : frame.length;
      const flags = view.getUint8(tcpOff + 13);
      return {
        source: `${frame.subarray(ipOff + 12, ipOff + 16).join(".")}:${view.getUint16(tcpOff)}`,
        destination: `${frame.subarray(ipOff + 16, ipOff + 16 + 4).join(".")}:${view.getUint16(tcpOff + 2)}`,
        seq: view.getUint32(tcpOff + 4),
        syn: (flags & 2) !== 0,
        payload: frame.subarray(tcpOff + (view.getUint8(tcpOff + 12) >> 4) * 4, end)
      };
    }

    const connections = new Map();
    const adapters = new Map();

    setInterval(() => {
      try {
        const pids = getAion2Pids();
        const conns = getTcpConnections(pids);
        const activeKeys = new Set();

        for (const c of conns) {
          const local = `${c.localAddress}:${c.localPort}`;
          const remote = `${c.remoteAddress}:${c.remotePort}`;
          const key = `${local}-${remote}`;
          activeKeys.add(key);
          if (!connections.has(key)) {
            const framer = createPacketFramer();
            connections.set(key, {
              in: createTcpAssembler(
                (chunk, at) => {
                  const pkts = framer.push(chunk, at);
                  for (const pkt of pkts) handleDecodedPacket(pkt);
                },
                () => framer.gap()
              )
            });
          }
        }
        for (const k of connections.keys()) {
          if (!activeKeys.has(k)) connections.delete(k);
        }

        const byLocalIp = new Map();
        for (const c of conns) {
          const list = byLocalIp.get(c.localAddress) || [];
          list.push(c);
          byLocalIp.set(c.localAddress, list);
        }
        for (const [ip, ad] of adapters) {
          if (!byLocalIp.has(ip)) {
            closePcap(ad.handle);
            adapters.delete(ip);
          }
        }
        for (const [ip, list] of byLocalIp) {
          const filter = `tcp and (${[...new Set(list.map(x => `port ${x.localPort}`))].join(" or ")})`;
          const existing = adapters.get(ip);
          if (existing && existing.filter === filter) continue;
          const devName = existing?.devName || findAdapterName(ip);
          if (!devName) continue;
          const err = Buffer.alloc(256);
          const handle = openLive(devName, 65535, 0, 1, err);
          if (!handle) continue;
          setnonblock(handle, 1, err);
          const prog = {};
          if (compile(handle, prog, filter, 1, 0xffffffff) === 0) {
            setfilter(handle, prog);
            freecode(prog);
          }
          if (existing) closePcap(existing.handle);
          adapters.set(ip, {
            devName,
            handle,
            linkType: datalink(handle),
            filter,
            hdrPtr: [null],
            dataPtr: [null]
          });
        }

        state.sniffer.connections = connections.size;
        state.sniffer.status = pids.size > 0 ? (connections.size > 0 ? "capturing" : "game-running") : "waiting-for-game";
      } catch (e) {
        state.sniffer.error = String(e.message || e);
      }
    }, 1000);

    setInterval(() => {
      for (const [, ad] of adapters) {
        try {
          while (nextEx(ad.handle, ad.hdrPtr, ad.dataPtr) === 1) {
            const hdr = koffi.decode(ad.hdrPtr[0], pcapHeader);
            const frame = koffi.decode(ad.dataPtr[0], "uint8_t", hdr.caplen);
            const at = hdr.seconds * 1000 + Math.floor(hdr.microseconds / 1000);
            const parsed = parseFrame(frame, ad.linkType);
            if (!parsed) continue;
            const connIn = connections.get(`${parsed.destination}-${parsed.source}`);
            if (connIn) connIn.in(parsed.seq, parsed.payload, parsed.syn, at);
          }
        } catch {}
      }
    }, 25);
  } catch (e) {
    state.sniffer.status = "questlog-sync";
    state.sniffer.error = String(e.message || e);
  }
}

// ============================================================================
// Build Enriched Pets Table Payload for the Overlay
// ============================================================================
function angleToArrow(deg) {
  if (deg === null || deg === undefined || Number.isNaN(deg)) return "";
  // Screen coordinates: 0=Right(→), 45=DownRight(↘), 90=Down(↓), 135=DownLeft(↙), ±180=Left(←), -135=UpLeft(↖), -90=Up(↑), -45=UpRight(↗)
  const norm = ((deg % 360) + 360) % 360;
  const idx = Math.round(norm / 45) % 8;
  return ["→", "↘", "↓", "↙", "←", "↖", "↑", "↗"][idx];
}

function buildOverlayPayload() {
  const curMapId = state.location.mapId;
  let curAreaId = state.location.areaId;
  let curAreaName = state.location.areaName;
  const playerX = state.location.x;
  const playerY = state.location.y;
  const playerZ = state.location.z;
  const hasPlayerCoords = typeof playerX === "number" && typeof playerY === "number";
  const playerMapCoords = hasPlayerCoords ? [playerX, -playerY] : null;

  // Also check Questlog's live map-tracker.json around counts if sniffer has 0 liveNpcs
  let aroundMap = { ...state.aroundCounts };
  const trackerFile = readJsonSafe(path.join(QUESTLOG_DIR, "map-tracker.json"));
  if (trackerFile?.around && Object.keys(aroundMap).length === 0) {
    aroundMap = { ...trackerFile.around };
  }

  // If player coordinates aren't locked yet, infer current Area from nearby spawned monsters
  if (!curAreaId && curMapId && Object.keys(aroundMap).length > 0) {
    const areaVotes = new Map();
    for (const pet of db.pets) {
      const hasNearby = pet.monsters.some(m => (aroundMap[m.id] || 0) > 0);
      if (!hasNearby) continue;
      const spawns = pet.spawnsByWorld[curMapId] || [];
      for (const sp of spawns) {
        if (sp.areaId && sp.areaName) {
          const prev = areaVotes.get(sp.areaId) || { id: sp.areaId, name: sp.areaName, count: 0 };
          prev.count++;
          areaVotes.set(sp.areaId, prev);
        }
      }
    }
    if (areaVotes.size > 0) {
      const best = [...areaVotes.values()].sort((a, b) => b.count - a.count)[0];
      curAreaId = best.id;
      curAreaName = best.name;
    }
  }

  let totalLevel3 = 0;
  let totalRegistered = 0;
  let totalMissingSoulsAll = 0;
  let totalMissingSoulsCurrentMap = 0;
  let totalMissingSoulsCurrentArea = 0;
  let inCurrentAreaPetsCount = 0;
  let inCurrentAreaLevel3Count = 0;
  let missingPetsInCurrentArea = 0;
  let totalAroundPets = 0;
  let inCurrentMapPetsCount = 0;
  let inCurrentMapLevel3Count = 0;
  const totalNpcsAroundCount = Object.values(aroundMap).reduce((a, b) => a + (Number(b) || 0), 0);

  const rows = db.pets.map(pet => {
    const rawLevel = state.levels[pet.id] || 0;
    const rawSouls = state.souls[pet.id] || 0;
    const progress = getPetProgress(rawLevel, rawSouls);

    if (progress.rawLevel >= 1) totalRegistered++;
    if (progress.isLevel3) totalLevel3++;
    if (pet.farmable) totalMissingSoulsAll += progress.remainingToLevel3;

    // Count how many monsters for this pet are around the player right now
    let aroundCount = 0;
    let nearestLiveDistanceM = null;
    let bestLiveTarget = null;
    const activeMonstersAround = [];

    for (const m of pet.monsters) {
      const cnt = aroundMap[m.id] || 0;
      if (cnt > 0) {
        aroundCount += cnt;
        activeMonstersAround.push({ id: m.id, name: m.name, count: cnt, level: m.level });
      }
    }

    if (hasPlayerCoords && state.liveNpcs.size > 0) {
      for (const npcObj of state.liveNpcs.values()) {
        if (pet.npcs.includes(npcObj.npc)) {
          const d = Math.round(Math.hypot(npcObj.x - playerX, npcObj.y - playerY) / 100);
          if (nearestLiveDistanceM === null || d < nearestLiveDistanceM) {
            nearestLiveDistanceM = d;
            bestLiveTarget = { x: npcObj.x, y: -npcObj.y, z: npcObj.z };
          }
        }
      }
    }

    // Spawns in current map & current area
    const mapSpawns = curMapId ? (pet.spawnsByWorld[curMapId] || []) : [];
    const spawnsInCurrentMap = mapSpawns.length;
    const areaSpawns = curAreaId ? mapSpawns.filter(s => s.areaId === curAreaId) : [];
    const spawnsInCurrentArea = areaSpawns.length;

    // Nearest static spawn distance & angle if player coordinates are known
    let nearestSpawnDistanceM = null;
    let bestSpawnTarget = null;
    const searchSpawns = areaSpawns.length > 0 ? areaSpawns : mapSpawns;
    if (playerMapCoords && searchSpawns.length > 0) {
      for (const sp of searchSpawns) {
        const d = Math.round(Math.hypot(sp.x - playerMapCoords[0], sp.y - playerMapCoords[1]) / 100);
        if (nearestSpawnDistanceM === null || d < nearestSpawnDistanceM) {
          nearestSpawnDistanceM = d;
          bestSpawnTarget = sp;
        }
      }
    }

    const chosenTarget = bestLiveTarget || bestSpawnTarget;
    let directionAngleDeg = null;
    let directionArrow = "";
    let riseM = 0;
    if (playerMapCoords && chosenTarget) {
      const dx = chosenTarget.x - playerMapCoords[0];
      const dy = chosenTarget.y - playerMapCoords[1];
      if (Math.hypot(dx, dy) > 100) {
        directionAngleDeg = Math.round((Math.atan2(dy, dx) * 180) / Math.PI);
        directionArrow = angleToArrow(directionAngleDeg);
      }
      if (typeof playerZ === "number" && typeof chosenTarget.z === "number") {
        const dz = Math.round((chosenTarget.z - playerZ) / 100);
        if (Math.abs(dz) >= 8) riseM = dz;
      }
    }

    // Primary Monster Name in current area/map (like "Red Eye Agrint", "Biruta Herbalist", "Red Shell Mole")
    let primaryMonsterName = pet.name;
    if (activeMonstersAround.length > 0) {
      primaryMonsterName = activeMonstersAround[0].name;
    } else if (searchSpawns.length > 0) {
      const npcFreq = new Map();
      for (const sp of searchSpawns) {
        npcFreq.set(sp.npc, (npcFreq.get(sp.npc) || 0) + 1);
      }
      const bestNpc = [...npcFreq.entries()].sort((a, b) => b[1] - a[1])[0]?.[0];
      const foundMob = pet.monsters.find(m => m.id === bestNpc);
      if (foundMob) primaryMonsterName = foundMob.name;
      else if (pet.monsters.length > 0) primaryMonsterName = pet.monsters[0].name;
    } else if (pet.monsters.length > 0) {
      primaryMonsterName = pet.monsters[0].name;
    }

    // Areas in current map where this pet spawns
    const curWorldInfo = curMapId ? pet.worlds.find(w => w.world === curMapId) : null;
    const currentMapAreas = curWorldInfo ? curWorldInfo.areas.map(a => a.name).slice(0, 3) : [];

    // Kills for this pet's monsters
    const killsCount = pet.npcs.reduce((acc, npcId) => acc + (state.kills[npcId] || 0), 0);
    const soulsGainedSession = state.soulsGained[pet.id] || 0;

    if (spawnsInCurrentMap > 0) {
      inCurrentMapPetsCount++;
      if (progress.isLevel3) inCurrentMapLevel3Count++;
      if (pet.farmable) totalMissingSoulsCurrentMap += progress.remainingToLevel3;
    }
    if ((spawnsInCurrentArea > 0 || aroundCount > 0) && pet.farmable) {
      inCurrentAreaPetsCount++;
      if (progress.isLevel3) {
        inCurrentAreaLevel3Count++;
      } else {
        missingPetsInCurrentArea++;
        totalMissingSoulsCurrentArea += progress.remainingToLevel3;
      }
    }
    if (aroundCount > 0) {
      totalAroundPets++;
    }

    const stepFillRatio = progress.isLevel3
      ? 1.0
      : (progress.nextStepNeed > 0 ? Math.min(1.0, progress.soulsInStep / progress.nextStepNeed) : 0.0);

    const nearestDistanceM = nearestLiveDistanceM ?? nearestSpawnDistanceM;

    return {
      id: pet.id,
      petId: pet.id,
      name: pet.name,
      primaryMonsterName,
      icon: pet.icon,
      genus: pet.genus,
      farmable: pet.farmable,
      monsters: pet.monsters,
      worlds: pet.worlds,
      totalSpawns: pet.totalSpawns,
      // Progress & Level 3 calculation:
      level: progress.rawLevel,
      effectiveLevel: progress.effectiveLevel,
      souls: progress.rawSouls,
      soulsInStep: progress.soulsInStep,
      nextStepNeed: progress.nextStepNeed,
      totalHaveToward3: progress.totalHaveToward3,
      totalRequiredFor3: progress.totalRequiredFor3,
      remainingToLevel3: progress.remainingToLevel3, // 0 if Level 3!
      remainingToLv3: progress.remainingToLevel3,
      distMeters: nearestDistanceM,
      isLevel3: progress.isLevel3,
      canLevelUpNow: progress.canLevelUpNow,
      stepFillRatio: Number(stepFillRatio.toFixed(3)),
      // Location, Distance, Direction & Around Player:
      aroundCount,
      activeMonstersAround,
      spawnsInCurrentMap,
      spawnsInCurrentArea,
      currentMapAreas,
      nearestDistanceM,
      isLiveDistance: nearestLiveDistanceM !== null,
      directionAngleDeg,
      directionArrow,
      riseM,
      targetCoords: chosenTarget ? [chosenTarget.x, chosenTarget.y] : null,
      killsCount,
      soulsGainedSession
    };
  });

  // Sort:
  // 1. Pets with monsters currently around the player (aroundCount > 0)
  // 2. Pets in current area (spawnsInCurrentArea > 0)
  // 3. Nearest distance to player
  // 4. Not yet Level 3 before Level 3, then fewest remaining souls to Level 3
  rows.sort((a, b) => {
    if ((b.aroundCount > 0) !== (a.aroundCount > 0)) return (b.aroundCount > 0) ? 1 : -1;
    if (b.aroundCount !== a.aroundCount) return b.aroundCount - a.aroundCount;
    if ((b.spawnsInCurrentArea > 0) !== (a.spawnsInCurrentArea > 0)) return (b.spawnsInCurrentArea > 0) ? 1 : -1;
    if ((b.spawnsInCurrentMap > 0) !== (a.spawnsInCurrentMap > 0)) return (b.spawnsInCurrentMap > 0) ? 1 : -1;
    if (a.isLevel3 !== b.isLevel3) return a.isLevel3 ? 1 : -1;
    const da = a.nearestDistanceM ?? 999999;
    const dbDist = b.nearestDistanceM ?? 999999;
    if (Math.abs(da - dbDist) > 15) return da - dbDist;
    if (a.remainingToLevel3 !== b.remainingToLevel3) return a.remainingToLevel3 - b.remainingToLevel3;
    return b.spawnsInCurrentMap - a.spawnsInCurrentMap || a.name.localeCompare(b.name);
  });

  // Build Mini-Map geometry for current area, neighboring areas & labeled pet spawn clusters
  let areaPolygons = [];
  const allAreas = [];
  if (curMapId && db.areasByWorld[curMapId]) {
    for (const a of db.areasByWorld[curMapId]) {
      if (a.id === curAreaId && a.polygons) areaPolygons = a.polygons;
      if (a.polygons && a.polygons.length > 0 && a.boxes && a.boxes.length > 0) {
        const [minX, minY, maxX, maxY] = a.boxes[0];
        allAreas.push({
          id: a.id,
          name: a.name,
          isCurrent: a.id === curAreaId,
          cx: Math.round((minX + maxX) / 2),
          cy: Math.round((minY + maxY) / 2),
          polygons: a.polygons
        });
      }
    }
  }

  const miniMapSpawns = [];
  const miniMapClusters = [];
  for (const r of rows) {
    if (r.spawnsInCurrentMap <= 0 && r.aroundCount <= 0) continue;
    const spList = curMapId ? (db.pets.find(p => p.id === r.id)?.spawnsByWorld[curMapId] || []) : [];
    if (spList.length === 0) continue;

    const mobLevel = (r.monsters && r.monsters.length > 0 && r.monsters[0].level) ? r.monsters[0].level : 0;

    // Group spawns by area so we get proper clusters across the whole world
    const areaGroups = new Map();
    for (const sp of spList) {
      const aId = sp.areaId || "unknown";
      if (!areaGroups.has(aId)) areaGroups.set(aId, []);
      areaGroups.get(aId).push(sp);
    }

    for (const [aId, gSpawns] of areaGroups.entries()) {
      let sx = 0, sy = 0;
      for (const sp of gSpawns) {
        sx += sp.x;
        sy += sp.y;
      }
      miniMapClusters.push({
        petId: r.id,
        petName: r.name,
        monsterName: r.primaryMonsterName,
        level: mobLevel,
        genus: r.genus || "feral",
        areaId: aId,
        areaName: gSpawns[0]?.areaName || "",
        x: Math.round(sx / gSpawns.length),
        y: Math.round(sy / gSpawns.length),
        count: gSpawns.length,
        aroundCount: (aId === curAreaId) ? r.aroundCount : 0,
        inCurrentArea: (aId === curAreaId),
        isLevel3: r.isLevel3,
        remainingToLevel3: r.remainingToLevel3
      });
    }

    // Include individual spawn dots for current area or if around
    if (r.spawnsInCurrentArea > 0 || r.aroundCount > 0) {
      const focusSpawns = (curAreaId && r.spawnsInCurrentArea > 0)
        ? spList.filter(s => s.areaId === curAreaId)
        : spList;
      for (const sp of focusSpawns.slice(0, 35)) {
        miniMapSpawns.push({
          x: sp.x,
          y: sp.y,
          petId: r.id,
          name: r.primaryMonsterName,
          genus: r.genus || "feral",
          isLevel3: r.isLevel3
        });
      }
    }
  }

  const miniMapLive = [];
  for (const npcObj of state.liveNpcs.values()) {
    const petIds = db.npcToPets[npcObj.npc];
    miniMapLive.push({
      x: npcObj.x,
      y: -npcObj.y,
      isPet: Array.isArray(petIds) && petIds.length > 0
    });
  }

  return {
    timestamp: Date.now(),
    sniffer: state.sniffer,
    character: state.character,
    location: {
      ...state.location,
      areaId: curAreaId || "",
      areaName: curAreaName || null
    },
    summary: {
      totalPets: db.pets.length,
      farmablePets: db.pets.filter(p => p.farmable).length,
      registeredPets: totalRegistered,
      level3Pets: totalLevel3,
      aroundPetsCount: totalAroundPets,
      totalNpcsAroundCount,
      inCurrentMapPetsCount,
      inCurrentMapLevel3Count,
      inCurrentAreaPetsCount,
      inCurrentAreaLevel3Count,
      missingPetsInCurrentArea,
      missingSoulsCurrentArea: totalMissingSoulsCurrentArea,
      missingSoulsCurrentMap: totalMissingSoulsCurrentMap,
      missingSoulsAll: totalMissingSoulsAll
    },
    miniMap: {
      worldId: curMapId,
      player: playerMapCoords,
      playerHeading: state.location.heading ?? null,
      areaName: curAreaName,
      areaPolygons,
      allAreas,
      clusters: miniMapClusters,
      spawns: miniMapSpawns,
      live: miniMapLive
    },
    worlds: db.worlds,
    recentEvents: state.recentEvents,
    inventory: state.inventory || {},
    craftingSession: state.craftingSession || null,
    cleric: state.cleric,
    around: rows.filter(r => (r.aroundCount > 0) || (r.nearestDistanceM !== null && r.nearestDistanceM <= 150)).sort((a, b) => ((a.nearestDistanceM !== null ? a.nearestDistanceM : 99999) - (b.nearestDistanceM !== null ? b.nearestDistanceM : 99999))),
    area: rows.filter(r => r.spawnsInCurrentArea > 0).sort((a, b) => ((a.nearestDistanceM !== null ? a.nearestDistanceM : 99999) - (b.nearestDistanceM !== null ? b.nearestDistanceM : 99999))),
    all: rows,
    pets: rows
  };
}

let notifyTimer = null;
function writeOverlayStateFile(payload) {
  const json = JSON.stringify(payload);
  try {
    fs.writeFileSync(OVERLAY_STATE_PATH, json);
  } catch {
    try {
      fs.writeFileSync(OVERLAY_STATE_PATH + ".tmp", json);
      fs.renameSync(OVERLAY_STATE_PATH + ".tmp", OVERLAY_STATE_PATH);
    } catch {}
  }
}

function notifyClients() {
  if (notifyTimer) return;
  notifyTimer = setTimeout(() => {
    notifyTimer = null;
    const payload = buildOverlayPayload();
    writeOverlayStateFile(payload);
    bus.emit("update", payload);
  }, 100);
}

// Initial sync & periodic watch of Questlog files
syncFromQuestlogFiles();
writeOverlayStateFile(buildOverlayPayload());

if (process.argv.includes("--dump-test")) {
  process.exit(0);
}

setInterval(() => {
  syncFromQuestlogFiles();
  writeOverlayStateFile(buildOverlayPayload());
}, 1000);
startNpcapSniffer();

// ============================================================================
// HTTP + SSE Server for Desktop Overlay & Web Overlay
// ============================================================================
const server = http.createServer((req, res) => {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");

  if (req.method === "OPTIONS") {
    res.writeHead(204);
    res.end();
    return;
  }

  const url = new URL(req.url, `http://127.0.0.1:${PORT}`);

  if (url.pathname === "/api/state") {
    const payload = buildOverlayPayload();
    res.writeHead(200, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
    res.end(JSON.stringify(payload));
    return;
  }

  if (url.pathname === "/api/set-map" && req.method === "POST") {
    let body = "";
    req.on("data", c => body += c);
    req.on("end", () => {
      try {
        const { mapId } = JSON.parse(body);
        updateMapAndArea(mapId, undefined, undefined, undefined, true);
        notifyClients();
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ ok: true }));
      } catch (e) {
        res.writeHead(400);
        res.end(JSON.stringify({ error: String(e) }));
      }
    });
    return;
  }

  if (url.pathname === "/events") {
    res.writeHead(200, {
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-cache, no-transform",
      "Connection": "keep-alive"
    });
    res.write(`data: ${JSON.stringify(buildOverlayPayload())}\n\n`);
    const onUpdate = payload => {
      try { res.write(`data: ${JSON.stringify(payload)}\n\n`); } catch {}
    };
    bus.on("update", onUpdate);
    req.on("close", () => bus.off("update", onUpdate));
    return;
  }

  // Serve overlay.html for "/"
  const htmlPath = path.join(__dirname, "overlay.html");
  if (fs.existsSync(htmlPath)) {
    res.writeHead(200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
    res.end(fs.readFileSync(htmlPath));
  } else {
    res.writeHead(200, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Aion 2 Pets Sniffer Running on port " + PORT);
  }
});

server.on("error", err => {
  // Ignore EADDRINUSE so the file writer + sniffer keep running even if port is taken
});

server.listen(PORT, "127.0.0.1", () => {
  console.log(`[Aion2-Pets-Overlay] Sniffer & State Server listening on http://127.0.0.1:${PORT}`);
});
