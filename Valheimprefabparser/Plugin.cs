using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
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

        // ── Конфигурация ────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> UseCoroutine;
        internal static ConfigEntry<string> OutputFileName;
        internal static ConfigEntry<bool> IncludeComponentList;

        void Awake()
        {
            _instance = this;
            Log = Logger;

            // Конфиги
            UseCoroutine = Config.Bind(
                "General", "UseCoroutine", true,
                "Использовать корутину для парсинга (снижает фризы при запуске)");

            OutputFileName = Config.Bind(
                "General", "OutputFileName", "valheim_prefabs.txt",
                "Имя выходного файла (будет создан рядом с плагином)");

            IncludeComponentList = Config.Bind(
                "General", "IncludeComponentList", false,
                "Добавить список всех компонентов каждого префаба в вывод (файл станет большим)");

            // Harmony патчи
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();

            Log.LogInfo($"{PluginName} v{PluginVersion} загружен.");
        }

        void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        // ── Точка входа для запуска парсинга ────────────────────────────────────
        // Вызывается из патчей (см. ниже). Использует корутину или синхронный вызов.
        internal static void TriggerParsing()
        {
            if (_instance == null) return;

            if (UseCoroutine.Value)
                _instance.StartCoroutine(PrefabParser.ParseCoroutine());
            else
                PrefabParser.ParseSync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // ПАТЧИ
    // Правило IV из «Формулы»: не используй Awake() для чтения данных игры.
    // Правильные хуки:
    //   • ObjectDB.CopyOtherDB — вызывается когда ObjectDB полностью готов с
    //     предметами и рецептами (в т.ч. от других модов).
    //   • ZNetScene.Awake — вызывается когда регистр сцены полностью загружен.
    //
    // Мы ждём обоих событий и запускаем парсинг только один раз, когда оба
    // завершились. Это гарантирует полноту данных.
    // ════════════════════════════════════════════════════════════════════════════
    internal static class ReadyFlags
    {
        internal static bool ObjectDBReady;
        internal static bool ZNetSceneReady;

        internal static void TryStart()
        {
            if (ObjectDBReady && ZNetSceneReady)
            {
                PrefabParserPlugin.Log.LogInfo("Оба реестра готовы — запускаю парсинг...");
                PrefabParserPlugin.TriggerParsing();
            }
        }
    }

    // ObjectDB.CopyOtherDB — также строковый патч для надёжности
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class ObjectDB_CopyOtherDB_Patch
    {
        static void Postfix()
        {
            PrefabParserPlugin.Log.LogInfo("ObjectDB.CopyOtherDB завершён.");
            ReadyFlags.ObjectDBReady = true;
            ReadyFlags.TryStart();
        }
    }

    // ZNetScene.Awake — приватный метод, nameof() не работает, используем строку
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake_Patch
    {
        static void Postfix()
        {
            PrefabParserPlugin.Log.LogInfo("ZNetScene.Awake завершён.");
            ReadyFlags.ZNetSceneReady = true;
            ReadyFlags.TryStart();
        }
    }
}