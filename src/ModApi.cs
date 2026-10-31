using System.Reflection;
using HarmonyLib;

namespace PoiMapPlus
{
    public class PoiMapPlusModApi : IModApi
    {
        static string ModPath;

        // A client learns which server it is on - and so where its files live - only after the
        // game has started, so their loading may have to wait for that
        static bool filesPending;

        public void InitMod(Mod _modInstance)
        {
            ModPath = _modInstance.Path;

            Log.Out("[PoiMapPlus] init");

            Cfg.Load(_modInstance.Path);
            ConsoleCmdPoiMap.SetModPath(_modInstance.Path);

            var harmony = new Harmony("com.lyovi.poimapplus");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Patch_MapArea_UpdateNavObjectList.Apply(harmony);
            Patch_MapArea_PressedLeft.Apply(harmony);

            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.SavePlayerData.RegisterHandler(OnSavePlayerData);
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
            ModEvents.GameUpdate.RegisterHandler(OnGameUpdate);
        }

        static void OnGameUpdate(ref ModEvents.SGameUpdateData _data)
        {
            if (GameManager.IsDedicatedServer) return;

            if (filesPending && PoiDb.StateDirKnown)
            {
                filesPending = false;
                LoadFiles();
                MapSettings.Refresh();
            }

            try { PartyShare.Tick(); }
            catch (System.Exception e) { Log.Error("[PoiMapPlus] party share: " + e); }
        }

        static void LoadFiles()
        {
            PoiDb.Load();
            PartyShare.Load();
            UiState.Load();
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

            if (PoiDb.StateDirKnown) LoadFiles();
            else filesPending = true;

            PoiRegistry.Build();
        }

        static void OnSavePlayerData(ref ModEvents.SSavePlayerDataData _data)
        {
            if (GameManager.IsDedicatedServer) return;

            PoiDb.Save();
            PartyShare.Save();
        }

        static void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData _data)
        {
            if (GameManager.IsDedicatedServer) return;

            filesPending = false;
            PoiDb.Save(true);
            PartyShare.Save(true);
            PoiMarkers.Clear();
            PoiScanner.Clear();
            PoiRegistry.Clear();
            PoiDb.Reset();
            PartyShare.Reset();

            // These hold references into the world and the UI that are going away
            Patch_Player_OnNewPrefabEntered.Forget();
            MapSettings.Forget();
            UiState.Reset();
        }
    }
}
