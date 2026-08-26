using System.Reflection;
using HarmonyLib;

namespace PoiMapPlus
{
    public class PoiMapPlusModApi : IModApi
    {
        static string ModPath;

        public void InitMod(Mod _modInstance)
        {
            ModPath = _modInstance.Path;

            Log.Out("[PoiMapPlus] инициализация");

            Cfg.Load(_modInstance.Path);
            ConsoleCmdPoiMap.SetModPath(_modInstance.Path);

            new Harmony("com.lyovi.poimapplus").PatchAll(Assembly.GetExecutingAssembly());

            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.SavePlayerData.RegisterHandler(OnSavePlayerData);
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
        }

        static void OnGameStartDone(ref ModEvents.SGameStartDoneData _data)
        {
            if (GameManager.IsDedicatedServer) return;

            // Re-read the config on every world load, so config edits only need a trip
            // through the main menu instead of a full game restart.
            Cfg.TrackedRanks.Clear();
            Cfg.LootListScores.Clear();
            Cfg.LootListIgnore.Clear();
            Cfg.Load(ModPath);

            PoiDb.Load();
            PoiRegistry.Build();
        }

        static void OnSavePlayerData(ref ModEvents.SSavePlayerDataData _data)
        {
            if (GameManager.IsDedicatedServer) return;

            PoiDb.Save();
        }

        static void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData _data)
        {
            if (GameManager.IsDedicatedServer) return;

            PoiDb.Save(true);
            PoiMarkers.Clear();
            PoiScanner.Clear();
            PoiRegistry.Clear();
            PoiDb.Reset();
        }
    }
}
