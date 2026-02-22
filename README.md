# Valheim Prefab Parser

> ⚠️ **This is a developer / modder tool — it adds nothing to gameplay.**  
> If you are a regular player looking for new content, this mod is not for you.

A lightweight BepInEx plugin that scans all of Valheim's loaded prefabs at runtime,
categorizes them, and writes a structured report to a text file next to the plugin `.dll`.
Intended for **mod authors and QA testers** who need a quick reference of every
prefab name available in the current game version — including those added by other mods.

---

## What it does

On every game load the mod waits for both `ObjectDB` and `ZNetScene` to finish
initializing, then collects all prefabs from three sources:

- `ObjectDB.m_items` and `m_recipes` — all items and crafting recipes
- `ZNetScene.m_prefabs` — every networked object registered in the scene
- `Resources.FindObjectsOfTypeAll` — remaining Unity assets not in either registry

The result is a single `.txt` file with a summary table and a full list sorted into
32 categories (weapons, armor, creatures, buildings, VFX, etc.).

**Typical output: 6000+ prefabs across 32 categories.**

---

## Who needs this

| Use case | Fits? |
|---|---|
| Writing a mod and need exact prefab names | ✅ |
| Testing whether a mod registers its prefabs correctly | ✅ |
| Comparing prefab lists between game versions | ✅ |
| Building a content mod and want to kitbash vanilla assets | ✅ |
| Playing Valheim for fun | ❌ install something else |

---

## Installation

1. Install [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) if you haven't already.
2. Drop `ValheimPrefabParser.dll` into `BepInEx/plugins/ValheimPrefabParser/`.
3. Launch the game and load any world.
4. The output file appears at `BepInEx/plugins/ValheimPrefabParser/valheim_prefabs.txt`.

---

## Configuration

Config file is generated at `BepInEx/config/com.yourname.valheimprefabparser.cfg` on first run.

| Key | Default | Description |
|---|---|---|
| `UseCoroutine` | `true` | Spread parsing across frames to avoid a freeze on load |
| `OutputFileName` | `valheim_prefabs.txt` | Name of the output file |
| `IncludeComponentList` | `false` | Print every Unity component on each prefab (makes the file large) |

---

## Output format

```
╔══════════════════════════════════════════════════════════╗
║          VALHEIM PREFAB PARSER — FULL LIST               ║
╚══════════════════════════════════════════════════════════╝
  Date:           2026-02-22 18:45:01
  Total prefabs:  6284
  Categories:     32

── SUMMARY ─────────────────────────────────────────────────
  01_Weapons                          87 pcs.
  02_Shields                          18 pcs.
  ...

── DETAILED LIST ────────────────────────────────────────────

┌─ 01_Weapons (87)
│  AtgeirBlackmetal
│  AtgeirBronze
│  ...
└────────────────────────────────────────────────────────────
```

---

## Categories

`01_Weapons` · `02_Shields` · `03_Armor` · `04_Ammunition` · `05_Food` · `06_Consumables` ·
`07_Materials` · `08_Trophies` · `09_Tools` · `10_Torches` · `11_Utility` · `12_Other_Items` ·
`13_Buildings` · `14_Bosses` · `15_Player` · `16_Humanoids` · `17_Monsters` · `18_Tameable` ·
`19_Creatures` · `20_Plants` · `21_Trees` · `22_Minerals` · `23_Containers` ·
`24_Crafting_Stations` · `25_Fireplaces` · `26_Portals` · `27_Pickables` · `28_Spawners` ·
`29_Ships` · `30_Projectiles` · `31_VFX_Effects` · `32_SFX` · `99_Other`

---

## Compatibility

- No gameplay changes, no ZDO writes, no RPC calls.
- Read-only — safe to add or remove mid-modpack without side effects.
- Works alongside any other mod; does not patch any method that other mods commonly touch.
- Tested on Valheim **0.220.x**.

---

## Source code

[github.com/SkarifWW/Valheimprefabparser](https://github.com/SkarifWW/Valheimprefabparser)

---

## Changelog

See [CHANGELOG.md](https://github.com/SkarifWW/Valheimprefabparser/blob/main/CHANGELOG.md).

---

*Made by **Skarif** — for modders, by a modder.*
