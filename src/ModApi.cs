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

            Log.Out("[PoiMapPlus] init");

            Cfg.Load(_modInstance.Path);
            ConsoleCmdPoiMap.SetModPath(_modInstance.Path);

            var harmony = new Harmony("com.lyovi.poimapplus");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Patch_MapArea_UpdateNavObjectList.Apply(harmony);

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
            UiState.Load();
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

            // These hold references into the world and the UI that are going away
            Patch_Player_OnNewPrefabEntered.Forget();
            MapSettings.Forget();
            UiState.Reset();
        }
    }
}
