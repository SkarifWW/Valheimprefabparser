using BepInEx;
using BepInEx.Configuration;
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
        public const string PluginVersion = "1.0.2";

        private static PrefabParserPlugin _instance;
        private Harmony _harmony;

        // Конфигурация
        private ConfigEntry<bool> _parseOnAwake;
        private ConfigEntry<bool> _useCoroutine;
        private ConfigEntry<string> _outputFileName;

        void Awake()
        {
            _instance = this;

            // Настройки конфигурации
            _parseOnAwake = Config.Bind("General", "ParseOnAwake", true,
                "Парсить префабы после ObjectDB.Awake");
            _useCoroutine = Config.Bind("General", "UseCoroutine", true,
                "Использовать корутину для парсинга (снижает лаги)");
            _outputFileName = Config.Bind("General", "OutputFileName", "valheim_prefabs.txt",
                "Имя выходного файла");

            // Применяем патчи Harmony
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();

            Logger.LogInfo($"{PluginName} v{PluginVersion} загружен!");
        }

        void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public static void StartParsing()
        {
            if (_instance._useCoroutine.Value)
            {
                _instance.StartCoroutine(_instance.ParsePrefabsCoroutine());
            }
            else
            {
                _instance.ParsePrefabs();
            }
        }

        private void ParsePrefabs()
        {
            try
            {
                Logger.LogInfo("Начинаю парсинг префабов...");

                var allPrefabs = GetAllPrefabs();
                var outputPath = Path.Combine(Paths.PluginPath, _outputFileName.Value);

                // Категоризируем префабы
                var categories = CategorizePrefabs(allPrefabs);

                using (StreamWriter writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                {
                    writer.WriteLine($"=== VALHEIM PREFABS BY CATEGORY ===");
                    writer.WriteLine($"Дата: {DateTime.Now}");
                    writer.WriteLine($"Всего префабов: {allPrefabs.Count}");
                    writer.WriteLine($"=======================================\n");

                    // Выводим по категориям
                    foreach (var category in categories.OrderBy(c => c.Key))
                    {
                        writer.WriteLine($"\n{'=',-60}");
                        writer.WriteLine($"{category.Key.ToUpper()} ({category.Value.Count})");
                        writer.WriteLine($"{'=',-60}");

                        foreach (var prefab in category.Value.OrderBy(p => p.name))
                        {
                            writer.WriteLine(prefab.name);
                        }
                    }
                }

                Logger.LogInfo($"Парсинг завершен! Найдено префабов: {allPrefabs.Count}");
                Logger.LogInfo($"Результаты сохранены в: {outputPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка при парсинге префабов: {ex}");
            }
        }

        private IEnumerator ParsePrefabsCoroutine()
        {
            Logger.LogInfo("Начинаю парсинг префабов (корутина)...");

            var allPrefabs = GetAllPrefabs();
            var outputPath = Path.Combine(Paths.PluginPath, _outputFileName.Value);

            // Категоризируем префабы
            var categories = CategorizePrefabs(allPrefabs);

            StringBuilder content = new StringBuilder();
            content.AppendLine($"=== VALHEIM PREFABS BY CATEGORY ===");
            content.AppendLine($"Дата: {DateTime.Now}");
            content.AppendLine($"Всего префабов: {allPrefabs.Count}");
            content.AppendLine($"=======================================\n");

            int processed = 0;
            foreach (var category in categories.OrderBy(c => c.Key))
            {
                content.AppendLine($"\n{'=',-60}");
                content.AppendLine($"{category.Key.ToUpper()} ({category.Value.Count})");
                content.AppendLine($"{'=',-60}");

                foreach (var prefab in category.Value.OrderBy(p => p.name))
                {
                    content.AppendLine(prefab.name);
                    processed++;

                    if (processed % 50 == 0)
                    {
                        Logger.LogInfo($"Обработано префабов: {processed}/{allPrefabs.Count}");
                        yield return null;
                    }
                }
            }

            // Сохраняем результат
            File.WriteAllText(outputPath, content.ToString(), Encoding.UTF8);

            Logger.LogInfo($"Парсинг завершен! Найдено префабов: {allPrefabs.Count}");
            Logger.LogInfo($"Результаты сохранены в: {outputPath}");
        }

        private List<GameObject> GetAllPrefabs()
        {
            List<GameObject> prefabs = new List<GameObject>();

            // 1. Префабы из ObjectDB
            if (ObjectDB.instance != null)
            {
                Logger.LogInfo("Собираю префабы из ObjectDB...");

                // Items
                if (ObjectDB.instance.m_items != null)
                {
                    foreach (var item in ObjectDB.instance.m_items)
                    {
                        if (item != null && item.gameObject != null)
                        {
                            prefabs.Add(item.gameObject);
                        }
                    }
                    Logger.LogInfo($"  Найдено Items: {ObjectDB.instance.m_items.Count}");
                }

                // Recipes
                if (ObjectDB.instance.m_recipes != null)
                {
                    foreach (var recipe in ObjectDB.instance.m_recipes)
                    {
                        if (recipe?.m_item != null && recipe.m_item.gameObject != null &&
                            !prefabs.Contains(recipe.m_item.gameObject))
                        {
                            prefabs.Add(recipe.m_item.gameObject);
                        }
                    }
                    Logger.LogInfo($"  Найдено Recipes: {ObjectDB.instance.m_recipes.Count}");
                }
            }

            // 2. Префабы из ZNetScene
            if (ZNetScene.instance != null)
            {
                Logger.LogInfo("Собираю префабы из ZNetScene...");

                if (ZNetScene.instance.m_prefabs != null)
                {
                    foreach (var prefab in ZNetScene.instance.m_prefabs)
                    {
                        if (prefab != null && !prefabs.Contains(prefab))
                        {
                            prefabs.Add(prefab);
                        }
                    }
                    Logger.LogInfo($"  Найдено в ZNetScene: {ZNetScene.instance.m_prefabs.Count}");
                }
            }

            // 3. Ресурсы из Resources
            Logger.LogInfo("Собираю префабы из Resources...");
            var resourcePrefabs = Resources.FindObjectsOfTypeAll<GameObject>();
            int resourceCount = 0;
            foreach (var prefab in resourcePrefabs)
            {
                if (prefab != null &&
                    !prefabs.Contains(prefab) &&
                    string.IsNullOrEmpty(prefab.scene.name) &&
                    prefab.transform.parent == null)
                {
                    prefabs.Add(prefab);
                    resourceCount++;
                }
            }
            Logger.LogInfo($"  Найдено уникальных в Resources: {resourceCount}");

            Logger.LogInfo($"Всего собрано уникальных префабов: {prefabs.Count}");
            return prefabs;
        }

        private Dictionary<string, List<GameObject>> CategorizePrefabs(List<GameObject> prefabs)
        {
            var categories = new Dictionary<string, List<GameObject>>();

            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;

                string category = DeterminePrefabCategory(prefab);

                if (!categories.ContainsKey(category))
                {
                    categories[category] = new List<GameObject>();
                }

                categories[category].Add(prefab);
            }

            return categories;
        }

        private string DeterminePrefabCategory(GameObject prefab)
        {
            // Проверяем компоненты для определения категории

            // Предметы (Items)
            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                var itemType = itemDrop.m_itemData.m_shared.m_itemType;
                switch (itemType)
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
                        if (itemDrop.m_itemData.m_shared.m_food > 0)
                            return "05_Food";
                        else
                            return "06_Consumables";

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

            // Строения (Pieces)
            var piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                return "13_Buildings";
            }

            // Существа и NPC
            var character = prefab.GetComponent<Character>();
            if (character != null)
            {
                // Проверяем, это босс?
                var bossField = character.GetType().GetField("m_boss");
                if (bossField != null)
                {
                    var isBoss = (bool)bossField.GetValue(character);
                    if (isBoss)
                        return "14_Bosses";
                }

                // Гуманоиды (включая игрока)
                var humanoid = prefab.GetComponent<Humanoid>();
                if (humanoid != null)
                {
                    if (prefab.name.Contains("Player"))
                        return "15_Player";
                    else
                        return "16_Humanoids";
                }

                // Монстры
                var monsterAI = prefab.GetComponent<MonsterAI>();
                if (monsterAI != null)
                {
                    return "17_Monsters";
                }

                // Приручаемые существа
                if (prefab.GetComponent<Tameable>() != null)
                {
                    return "18_Tameable";
                }

                // Остальные существа
                return "19_Creatures";
            }

            // Растения
            var plant = prefab.GetComponent<Plant>();
            if (plant != null)
            {
                return "20_Plants";
            }

            // Деревья
            var tree = prefab.GetComponent<TreeBase>();
            if (tree != null)
            {
                return "21_Trees";
            }

            // Минералы и руды
            var mineRock = prefab.GetComponent<MineRock>();
            if (mineRock != null)
            {
                return "22_Minerals";
            }

            // Контейнеры
            var container = prefab.GetComponent<Container>();
            if (container != null)
            {
                return "23_Containers";
            }

            // Станции крафта
            var craftingStation = prefab.GetComponent<CraftingStation>();
            if (craftingStation != null)
            {
                return "24_Crafting_Stations";
            }

            // Костры и источники тепла
            var fireplace = prefab.GetComponent<Fireplace>();
            if (fireplace != null)
            {
                return "25_Fireplaces";
            }

            // Порталы
            var teleport = prefab.GetComponent<TeleportWorld>();
            if (teleport != null)
            {
                return "26_Portals";
            }

            // Подбираемые объекты
            var pickable = prefab.GetComponent<Pickable>();
            if (pickable != null)
            {
                return "27_Pickables";
            }

            // Спавнеры
            var spawner = prefab.GetComponent<SpawnArea>();
            if (spawner != null)
            {
                return "28_Spawners";
            }

            // Корабли
            var ship = prefab.GetComponent<Ship>();
            if (ship != null)
            {
                return "29_Ships";
            }

            // Снаряды
            var projectile = prefab.GetComponent<Projectile>();
            if (projectile != null)
            {
                return "30_Projectiles";
            }

            // VFX и эффекты по имени
            if (prefab.name.Contains("vfx_") || prefab.name.Contains("sfx_") ||
                prefab.name.Contains("fx_") || prefab.name.Contains("smoke") ||
                prefab.name.Contains("spark") || prefab.name.Contains("debris"))
            {
                return "31_VFX_Effects";
            }

            // Остальное
            return "99_Other";
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        public static class ObjectDB_Awake_Patch
        {
            static void Postfix()
            {
                if (_instance?._parseOnAwake.Value == true)
                {
                    _instance.Logger.LogInfo("ObjectDB.Awake завершен, запускаю парсинг префабов...");
                    StartParsing();
                }
            }
        }

        [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
        public static class ObjectDB_CopyOtherDB_Patch
        {
            static void Postfix()
            {
                _instance?.Logger.LogInfo("ObjectDB.CopyOtherDB вызван");
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        public static class ZNetScene_Awake_Patch
        {
            static void Postfix()
            {
                _instance?.Logger.LogInfo("ZNetScene.Awake завершен");
            }
        }
    }
}