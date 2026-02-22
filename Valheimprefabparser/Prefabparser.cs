using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ValheimPrefabParser
{
    /// <summary>
    /// Вся логика сбора, категоризации и записи префабов.
    /// Отделена от Plugin.cs по принципу единственной ответственности.
    /// </summary>
    internal static class PrefabParser
    {
        // ── Синхронный запуск ────────────────────────────────────────────────────
        internal static void ParseSync()
        {
            try
            {
                PrefabParserPlugin.Log.LogInfo("Старт парсинга (синхронный режим)...");
                var prefabs = CollectAllPrefabs();
                var categories = Categorize(prefabs);
                var content = BuildContent(prefabs.Count, categories);
                WriteFile(content);
                PrefabParserPlugin.Log.LogInfo($"Готово! Найдено префабов: {prefabs.Count}");
            }
            catch (Exception ex)
            {
                PrefabParserPlugin.Log.LogError($"Ошибка парсинга: {ex}");
            }
        }

        // ── Корутина (асинхронный запуск) ────────────────────────────────────────
        // Делает yield return null каждые 50 объектов, чтобы не фризить игру.
        internal static IEnumerator ParseCoroutine()
        {
            PrefabParserPlugin.Log.LogInfo("Старт парсинга (корутина)...");

            var prefabs = CollectAllPrefabs();
            yield return null;

            var categories = new Dictionary<string, List<GameObject>>();
            int processed = 0;

            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                var cat = GetCategory(prefab);

                if (!categories.TryGetValue(cat, out var list))
                {
                    list = new List<GameObject>();
                    categories[cat] = list;
                }
                list.Add(prefab);

                processed++;
                if (processed % 50 == 0)
                {
                    PrefabParserPlugin.Log.LogInfo($"Категоризовано: {processed}/{prefabs.Count}");
                    yield return null;
                }
            }

            var content = BuildContent(prefabs.Count, categories);
            WriteFile(content);
            PrefabParserPlugin.Log.LogInfo($"Готово! Найдено префабов: {prefabs.Count}");
        }

        // ════════════════════════════════════════════════════════════════════════
        // СБОР ПРЕФАБОВ
        // Исправление: HashSet вместо List.Contains → O(1) вместо O(n) на дедупликацию.
        // ════════════════════════════════════════════════════════════════════════
        private static List<GameObject> CollectAllPrefabs()
        {
            var seen = new HashSet<int>();   // instanceID GameObject — быстрый ключ
            var prefabs = new List<GameObject>();

            void TryAdd(GameObject go)
            {
                if (go == null) return;
                int id = go.GetInstanceID();
                if (seen.Add(id))               // Add возвращает false, если уже есть
                    prefabs.Add(go);
            }

            // 1. ObjectDB — предметы
            if (ObjectDB.instance != null)
            {
                PrefabParserPlugin.Log.LogInfo("  Читаю ObjectDB.m_items...");
                foreach (var item in ObjectDB.instance.m_items)
                    if (item != null) TryAdd(item.gameObject);

                PrefabParserPlugin.Log.LogInfo("  Читаю ObjectDB.m_recipes...");
                foreach (var recipe in ObjectDB.instance.m_recipes)
                    if (recipe?.m_item != null) TryAdd(recipe.m_item.gameObject);
            }
            else
            {
                PrefabParserPlugin.Log.LogWarning("ObjectDB.instance == null! Данные предметов будут неполными.");
            }

            // 2. ZNetScene — все сетевые префабы (существа, строения, эффекты и т.д.)
            if (ZNetScene.instance != null)
            {
                PrefabParserPlugin.Log.LogInfo("  Читаю ZNetScene.m_prefabs...");
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                    TryAdd(prefab);
            }
            else
            {
                PrefabParserPlugin.Log.LogWarning("ZNetScene.instance == null! Данные сцены будут неполными.");
            }

            // 3. Resources.FindObjectsOfTypeAll — ловит ассеты, которые не в реестрах
            //    Фильтр: нет сцены (это ассет, не объект на сцене) + нет родителя (корневой)
            PrefabParserPlugin.Log.LogInfo("  Сканирую Resources.FindObjectsOfTypeAll...");
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (string.IsNullOrEmpty(go.scene.name) && go.transform.parent == null)
                    TryAdd(go);
            }

            PrefabParserPlugin.Log.LogInfo($"  Итого уникальных префабов: {prefabs.Count}");
            return prefabs;
        }

        // ════════════════════════════════════════════════════════════════════════
        // КАТЕГОРИЗАЦИЯ
        // ════════════════════════════════════════════════════════════════════════
        private static Dictionary<string, List<GameObject>> Categorize(List<GameObject> prefabs)
        {
            var categories = new Dictionary<string, List<GameObject>>();

            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                var cat = GetCategory(prefab);

                if (!categories.TryGetValue(cat, out var list))
                {
                    list = new List<GameObject>();
                    categories[cat] = list;
                }
                list.Add(prefab);
            }

            return categories;
        }

        private static string GetCategory(GameObject go)
        {
            // ── Предметы ─────────────────────────────────────────────────────────
            var itemDrop = go.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                var t = itemDrop.m_itemData.m_shared.m_itemType;
                switch (t)
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
                        return itemDrop.m_itemData.m_shared.m_food > 0
                            ? "05_Food"
                            : "06_Consumables";
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

            // ── Строения ─────────────────────────────────────────────────────────
            if (go.GetComponent<Piece>() != null)
                return "13_Buildings";

            // ── Существа ─────────────────────────────────────────────────────────
            var character = go.GetComponent<Character>();
            if (character != null)
            {
                // Исправление: m_boss — публичное поле, Reflection не нужен
                if (character.m_boss)
                    return "14_Bosses";

                if (go.GetComponent<Humanoid>() != null)
                    return go.name.Contains("Player") ? "15_Player" : "16_Humanoids";

                if (go.GetComponent<Tameable>() != null)
                    return "18_Tameable";

                if (go.GetComponent<MonsterAI>() != null)
                    return "17_Monsters";

                return "19_Creatures";
            }

            // ── Окружение ────────────────────────────────────────────────────────
            if (go.GetComponent<Plant>() != null) return "20_Plants";
            if (go.GetComponent<TreeBase>() != null) return "21_Trees";
            if (go.GetComponent<MineRock>() != null) return "22_Minerals";
            if (go.GetComponent<MineRock5>() != null) return "22_Minerals";

            // ── Интерактивные объекты ─────────────────────────────────────────────
            if (go.GetComponent<Container>() != null) return "23_Containers";
            if (go.GetComponent<CraftingStation>() != null) return "24_Crafting_Stations";
            if (go.GetComponent<Fireplace>() != null) return "25_Fireplaces";
            if (go.GetComponent<TeleportWorld>() != null) return "26_Portals";
            if (go.GetComponent<Pickable>() != null) return "27_Pickables";
            if (go.GetComponent<SpawnArea>() != null) return "28_Spawners";
            if (go.GetComponent<Ship>() != null) return "29_Ships";
            if (go.GetComponent<Projectile>() != null) return "30_Projectiles";

            // ── VFX / эффекты ────────────────────────────────────────────────────
            // Исправление: проверяем компонент, а не только имя
            if (go.GetComponent<ParticleSystem>() != null) return "31_VFX_Effects";
            if (go.GetComponent<AudioSource>() != null &&
                go.GetComponent<ZNetView>() == null) return "32_SFX";

            // Fallback по имени (для тех, у кого нет характерных компонентов)
            var n = go.name;
            if (n.StartsWith("vfx_") || n.StartsWith("sfx_") || n.StartsWith("fx_"))
                return "31_VFX_Effects";

            return "99_Other";
        }

        // ════════════════════════════════════════════════════════════════════════
        // ПОСТРОЕНИЕ ТЕКСТА
        // Исправление: вынесено в отдельный метод — больше не дублируется
        // между синхронным и корутинным вариантами.
        // ════════════════════════════════════════════════════════════════════════
        private static string BuildContent(int totalCount, Dictionary<string, List<GameObject>> categories)
        {
            var sb = new StringBuilder();

            sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
            sb.AppendLine("║         VALHEIM PREFAB PARSER — КАТЕГОРИРОВАННЫЙ СПИСОК  ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
            sb.AppendLine($"  Дата:           {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  Всего префабов: {totalCount}");
            sb.AppendLine($"  Категорий:      {categories.Count}");
            sb.AppendLine();

            // Сводная таблица категорий
            sb.AppendLine("── СВОДКА ─────────────────────────────────────────────────");
            foreach (var cat in categories.OrderBy(c => c.Key))
                sb.AppendLine($"  {cat.Key,-35} {cat.Value.Count,5} шт.");
            sb.AppendLine();

            // Детальный список
            sb.AppendLine("── ДЕТАЛЬНЫЙ СПИСОК ────────────────────────────────────────");
            foreach (var cat in categories.OrderBy(c => c.Key))
            {
                sb.AppendLine();
                sb.AppendLine($"┌─ {cat.Key} ({cat.Value.Count}) ─────────────────────────────");

                foreach (var go in cat.Value.OrderBy(g => g.name))
                {
                    sb.AppendLine($"│  {go.name}");

                    // Опционально: список компонентов
                    if (PrefabParserPlugin.IncludeComponentList.Value)
                    {
                        var components = go.GetComponents<Component>();
                        var names = components
                            .Where(c => c != null)
                            .Select(c => c.GetType().Name)
                            .Distinct()
                            .OrderBy(s => s);
                        sb.AppendLine($"│    [{string.Join(", ", names)}]");
                    }
                }

                sb.AppendLine("└─────────────────────────────────────────────────────────");
            }

            return sb.ToString();
        }

        // ════════════════════════════════════════════════════════════════════════
        // ЗАПИСЬ ФАЙЛА
        // Исправление: файл пишется в папку самого плагина (не в общий PluginPath),
        // и только один раз из одного места.
        // ════════════════════════════════════════════════════════════════════════
        private static void WriteFile(string content)
        {
            try
            {
                // Кладём файл в папку плагина: BepInEx/plugins/ValheimPrefabParser/
                string pluginDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string outputPath = Path.Combine(pluginDir, PrefabParserPlugin.OutputFileName.Value);

                File.WriteAllText(outputPath, content, Encoding.UTF8);
                PrefabParserPlugin.Log.LogInfo($"Файл записан: {outputPath}");
            }
            catch (Exception ex)
            {
                PrefabParserPlugin.Log.LogError($"Ошибка записи файла: {ex.Message}");
            }
        }
    }
}