using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ValheimPrefabParser
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class PrefabParserPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.yourname.valheimprefabparser";
        public const string PluginName = "Valheim Prefab Parser";
        public const string PluginVersion = "2.0.0";

        internal static ManualLogSource Log;
        private static PrefabParserPlugin _instance;
        private Harmony _harmony;

        internal static ConfigEntry<bool> UseCoroutine;
        internal static ConfigEntry<string> OutputFileName;
        internal static ConfigEntry<bool> IncludeComponentList;

        void Awake()
        {
            _instance = this;
            Log = Logger;

            UseCoroutine = Config.Bind(
                "General", "UseCoroutine", true,
                "Spread parsing across frames to avoid a freeze on load.");

            OutputFileName = Config.Bind(
                "General", "OutputFileName", "valheim_prefabs.txt",
                "Output file name. Created next to the plugin .dll.");

            IncludeComponentList = Config.Bind(
                "General", "IncludeComponentList", false,
                "Print every Unity component on each prefab. Makes the file large.");

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        internal static void TriggerParsing()
        {
            if (_instance == null) return;

            if (UseCoroutine.Value)
                _instance.StartCoroutine(_instance.ParseCoroutine());
            else
                ParseSync();
        }

        private static bool _parsingStarted;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        public static class ZNetScene_Awake_Patch
        {
            static void Postfix()
            {
                Log.LogInfo("ZNetScene.Awake finished — waiting for ObjectDB...");
                if (!_parsingStarted)
                    _instance.StartCoroutine(_instance.WaitForObjectDBAndParse());
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        public static class ObjectDB_CopyOtherDB_Patch
        {
            static void Postfix()
            {
                Log.LogInfo("ObjectDB.CopyOtherDB finished.");
                if (!_parsingStarted && ZNetScene.instance != null)
                {
                    Log.LogInfo("ZNetScene is ready — starting parse from CopyOtherDB.");
                    _parsingStarted = true;
                    TriggerParsing();
                }
            }
        }

        private IEnumerator WaitForObjectDBAndParse()
        {
            float elapsed = 0f;
            while (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
            {
                elapsed += Time.deltaTime;
                if (elapsed > 30f)
                {
                    Log.LogWarning("Timeout: ObjectDB was not ready within 30 seconds. Starting parse without it.");
                    break;
                }
                yield return null;
            }

            if (_parsingStarted) yield break;
            _parsingStarted = true;

            Log.LogInfo($"ObjectDB ready ({ObjectDB.instance?.m_items.Count ?? 0} items) — starting parse.");
            TriggerParsing();
        }

        private static List<GameObject> GetAllPrefabs()
        {
            var seen = new HashSet<int>();
            var prefabs = new List<GameObject>();

            void TryAdd(GameObject go)
            {
                if (go == null) return;
                if (seen.Add(go.GetInstanceID()))
                    prefabs.Add(go);
            }

            if (ObjectDB.instance != null)
            {
                Log.LogInfo("  Reading ObjectDB.m_items...");
                foreach (var item in ObjectDB.instance.m_items)
                    if (item != null) TryAdd(item.gameObject);

                Log.LogInfo("  Reading ObjectDB.m_recipes...");
                foreach (var recipe in ObjectDB.instance.m_recipes)
                    if (recipe?.m_item != null) TryAdd(recipe.m_item.gameObject);

                Log.LogInfo($"  Items: {ObjectDB.instance.m_items.Count}, Recipes: {ObjectDB.instance.m_recipes.Count}");
            }
            else
            {
                Log.LogWarning("ObjectDB.instance is null — item data will be incomplete.");
            }

            if (ZNetScene.instance != null)
            {
                Log.LogInfo("  Reading ZNetScene.m_prefabs...");
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                    TryAdd(prefab);

                Log.LogInfo($"  ZNetScene prefabs: {ZNetScene.instance.m_prefabs.Count}");
            }
            else
            {
                Log.LogWarning("ZNetScene.instance is null — scene data will be incomplete.");
            }

            Log.LogInfo("  Scanning Resources.FindObjectsOfTypeAll...");
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (string.IsNullOrEmpty(go.scene.name))
                    TryAdd(go);

            Log.LogInfo($"  Total unique prefabs collected: {prefabs.Count}");
            return prefabs;
        }

        private static string DeterminePrefabCategory(GameObject go)
        {
            var itemDrop = go.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                switch (itemDrop.m_itemData.m_shared.m_itemType)
                {
                    case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                    case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                    case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    case ItemDrop.ItemData.ItemType.Bow:
                        return "01_Weapons";
                    case ItemDrop.ItemData.ItemType.Shield:
                        return "02_Shields";
                    case ItemDrop.ItemData.ItemType.Helmet:
                    case ItemDrop.ItemData.ItemType.Chest:
                    case ItemDrop.ItemData.ItemType.Legs:
                    case ItemDrop.ItemData.ItemType.Shoulder:
                    case ItemDrop.ItemData.ItemType.Hands:
                        return "03_Armor";
                    case ItemDrop.ItemData.ItemType.Ammo:
                    case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                        return "04_Ammunition";
                    case ItemDrop.ItemData.ItemType.Consumable:
                        return itemDrop.m_itemData.m_shared.m_food > 0 ? "05_Food" : "06_Consumables";
                    case ItemDrop.ItemData.ItemType.Material:
                        return "07_Materials";
                    case ItemDrop.ItemData.ItemType.Trophy:
                        return "08_Trophies";
                    case ItemDrop.ItemData.ItemType.Tool:
                        return "09_Tools";
                    case ItemDrop.ItemData.ItemType.Torch:
                        return "10_Torches";
                    case ItemDrop.ItemData.ItemType.Utility:
                        return "11_Utility";
                    default:
                        return "12_Other_Items";
                }
            }

            if (go.GetComponent<Piece>() != null) return "13_Buildings";

            var character = go.GetComponent<Character>();
            if (character != null)
            {
                if (character.m_boss) return "14_Bosses";
                if (go.name.Contains("Player")) return "15_Player";
                if (go.GetComponent<Humanoid>() != null) return "16_Humanoids";
                if (go.GetComponent<MonsterAI>() != null) return "17_Monsters";
                if (go.GetComponent<Tameable>() != null) return "18_Tameable";
                return "19_Creatures";
            }

            if (go.GetComponent<Plant>() != null) return "20_Plants";
            if (go.GetComponent<TreeBase>() != null) return "21_Trees";
            if (go.GetComponent<MineRock>() != null) return "22_Minerals";
            if (go.GetComponent<MineRock5>() != null) return "22_Minerals";

            if (go.GetComponent<Container>() != null) return "23_Containers";
            if (go.GetComponent<CraftingStation>() != null) return "24_Crafting_Stations";
            if (go.GetComponent<Fireplace>() != null) return "25_Fireplaces";
            if (go.GetComponent<TeleportWorld>() != null) return "26_Portals";
            if (go.GetComponent<Pickable>() != null) return "27_Pickables";
            if (go.GetComponent<SpawnArea>() != null) return "28_Spawners";
            if (go.GetComponent<Ship>() != null) return "29_Ships";
            if (go.GetComponent<Projectile>() != null) return "30_Projectiles";

            if (go.GetComponent<ParticleSystem>() != null) return "31_VFX_Effects";
            if (go.GetComponent<AudioSource>() != null &&
                go.GetComponent<ZNetView>() == null) return "32_SFX";

            var n = go.name;
            if (n.StartsWith("vfx_") || n.StartsWith("sfx_") || n.StartsWith("fx_"))
                return "31_VFX_Effects";

            return "99_Other";
        }

        private static Dictionary<string, List<GameObject>> CategorizePrefabs(List<GameObject> prefabs)
        {
            var categories = new Dictionary<string, List<GameObject>>();

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var cat = DeterminePrefabCategory(go);

                if (!categories.TryGetValue(cat, out var list))
                {
                    list = new List<GameObject>();
                    categories[cat] = list;
                }
                list.Add(go);
            }

            return categories;
        }

        private static string BuildContent(int totalCount, Dictionary<string, List<GameObject>> categories)
        {
            var sb = new StringBuilder();

            sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
            sb.AppendLine("║           VALHEIM PREFAB PARSER — FULL LIST              ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
            sb.AppendLine($"  Date:             {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  Total prefabs:    {totalCount}");
            sb.AppendLine($"  Categories:       {categories.Count}");
            sb.AppendLine();

            sb.AppendLine("── SUMMARY ─────────────────────────────────────────────────");
            foreach (var cat in categories.OrderBy(c => c.Key))
                sb.AppendLine($"  {cat.Key,-35} {cat.Value.Count,5} pcs.");
            sb.AppendLine();

            sb.AppendLine("── DETAILED LIST ───────────────────────────────────────────");
            foreach (var cat in categories.OrderBy(c => c.Key))
            {
                sb.AppendLine();
                sb.AppendLine($"┌─ {cat.Key} ({cat.Value.Count})");

                foreach (var go in cat.Value.OrderBy(g => g.name))
                {
                    sb.AppendLine($"│  {go.name}");

                    if (IncludeComponentList.Value)
                    {
                        var comps = go.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => c.GetType().Name)
                            .Distinct()
                            .OrderBy(s => s);
                        sb.AppendLine($"│    [{string.Join(", ", comps)}]");
                    }
                }

                sb.AppendLine("└───────────────────────────────────────────────────────────");
            }

            return sb.ToString();
        }

        private static void WriteFile(string content)
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string outputPath = Path.Combine(pluginDir, OutputFileName.Value);
                File.WriteAllText(outputPath, content, Encoding.UTF8);
                Log.LogInfo($"File written: {outputPath}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to write file: {ex.Message}");
            }
        }

        private static void ParseSync()
        {
            try
            {
                Log.LogInfo("Starting parse (synchronous mode)...");
                var prefabs = GetAllPrefabs();
                var categories = CategorizePrefabs(prefabs);
                WriteFile(BuildContent(prefabs.Count, categories));
                Log.LogInfo($"Done! Total prefabs found: {prefabs.Count}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Parse failed: {ex}");
            }
        }

        private IEnumerator ParseCoroutine()
        {
            Log.LogInfo("Starting parse (coroutine mode)...");

            var prefabs = GetAllPrefabs();
            yield return null;

            var categories = new Dictionary<string, List<GameObject>>();
            int processed = 0;

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var cat = DeterminePrefabCategory(go);

                if (!categories.TryGetValue(cat, out var list))
                {
                    list = new List<GameObject>();
                    categories[cat] = list;
                }
                list.Add(go);

                if (++processed % 50 == 0)
                {
                    Log.LogInfo($"Categorized: {processed}/{prefabs.Count}");
                    yield return null;
                }
            }

            WriteFile(BuildContent(prefabs.Count, categories));
            Log.LogInfo($"Done! Total prefabs found: {prefabs.Count}");
        }
    }
}