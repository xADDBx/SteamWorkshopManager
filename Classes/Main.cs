using HarmonyLib;
using ModKit;
using Newtonsoft.Json;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace SteamWorkshopManager {
    public static class Main {
        internal static Harmony HarmonyInstance;
        internal static UnityModManager.ModEntry.ModLogger log;
        public static Settings settings;
        internal static UnityModManager.ModEntry modEntry;
        internal static UI.Browser<Mod, Mod> ModBrowser = new(true, true);
        public static AppId_t AppId => SteamUtils.GetAppID();
        public const string AppDataDirName = "WH 40000 RT";
        public const string UMM = "UnityModManager";
        public const string OwlcatTemplate = "Modifications";
        public const string ModificationManagerSettings = "OwlcatModificationManagerSettings.json";
        public static string AppDataDir;
        public static Dictionary<PublishedFileId_t, Mod> mods = new();
        public static HashSet<PublishedFileId_t> InstalledMods = new();
        public static HashSet<PublishedFileId_t> UMMMods = new();
        public static HashSet<PublishedFileId_t> OwlcatTemplateMods = new();
        public static Callback<SteamUGCQueryCompleted_t> getModsCallback;
        public static bool finishedOnlineInit = false;
        public static bool finishedOfflineInit = false;

        public static bool Load(UnityModManager.ModEntry modEntry) {
            log = modEntry.Logger;
            Main.modEntry = modEntry;
            if (!PreInit()) return false;
            modEntry.OnGUI = OnGUI;
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;
            HarmonyInstance = new Harmony(modEntry.Info.Id);
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            return Init();
        }
        private static bool startShow = false;
        public static void OnGUI(UnityModManager.ModEntry modEntry) {
            SteamAPI.RunCallbacks();
            if (startShow) {
                ModBrowser.OnGUI(mods.Values, () => mods.Values, m => m, m => $"{m.name} {m.description} {m.id} {m.author} {m.authorId}", m => new string[] { m.name ?? "" }, null,
                    (def, item) => {
                        UI.Label(def.name ?? "", UI.Width(200));
                        UI.Label(def.id.ToString() ?? "", UI.Width(200));
                        UI.Label(def.author ?? def.authorId.ToString() ?? "", UI.Width(200));
                        UI.Label(def.description ?? "", UI.Width(600));
                    });
            }
            if (Event.current.type == EventType.Repaint && finishedOnlineInit) {
                startShow = true;
            }
        }

        public static void OnSaveGUI(UnityModManager.ModEntry modEntry) {
            settings.Save(modEntry);
        }
        public static bool Init() {
            try {
                InitOnline();
            } catch (Exception ex) {
                log.Log("Error while trying to initialize online caches");
                log.Log(ex.ToString());
            }
            try {
                InitLocal();
            } catch (Exception ex) {
                log.Log("Error while trying to initialize local caches");
                log.Log(ex.ToString());
                return false;
            }
            return true;
        }
        public static uint matchedResults = uint.MaxValue;
        public static uint matchesLeft = 0;
        public static uint matchesGot = 0;
        public static readonly object matchesGotLock = new();
        public static HashSet<UGCQueryHandle_t> queries = new();
        public static void InitOnline() {
            log.Log($"Try init online.");
            // is EUGCMatchingUGCType.k_EUGCMatchingUGCType_GameManagedItems correct?
            UGCQueryHandle_t Query = SteamUGC.CreateQueryAllUGCRequest(EUGCQuery.k_EUGCQuery_RankedByVote, EUGCMatchingUGCType.k_EUGCMatchingUGCType_All, AppId, AppId, 1);
            queries.Add(Query);
            SteamAPICall_t ApiCall = SteamUGC.SendQueryUGCRequest(Query);
            if (ApiCall == SteamAPICall_t.Invalid) {
                log.Log("First API call invalid; failed online init");
                throw new Exception("Api Call was invalid");
            }
        }
        public static void OnQueryCompleted(SteamUGCQueryCompleted_t callback) {
            log.Log($"On query completed with handle: {callback.m_handle}");
            if (!queries.Contains(callback.m_handle)) return;
            if (callback.m_eResult != EResult.k_EResultOK) {
                log.Log($"Error while getting Workshop Mods. Query resulted in: {callback.m_eResult}");
                return;
            }
            lock (matchesGotLock) {
                matchesGot += callback.m_unNumResultsReturned;
                if (matchedResults == uint.MaxValue) {
                    matchedResults = callback.m_unTotalMatchingResults;
                    matchesLeft = matchedResults - 50;
                    uint page = 1;
                    for (; matchesLeft > 0; matchesLeft -= 50) {
                        page += 1;
                        UGCQueryHandle_t Query = SteamUGC.CreateQueryAllUGCRequest(EUGCQuery.k_EUGCQuery_RankedByVote, EUGCMatchingUGCType.k_EUGCMatchingUGCType_All, AppId, AppId, page);
                        queries.Add(Query);
                        SteamAPICall_t ApiCall = SteamUGC.SendQueryUGCRequest(Query);
                        if (ApiCall == SteamAPICall_t.Invalid) {
                            log.Log($"{page + 1}st API call invalid");
                        }
                    }
                }
            }
            lock (mods) {
                for (uint idx = 0; idx < callback.m_unNumResultsReturned; idx++) {
                    if (SteamUGC.GetQueryUGCResult(callback.m_handle, idx, out SteamUGCDetails_t pDetails)) {
                        mods[pDetails.m_nPublishedFileId] = new Mod() {
                            authorId = pDetails.m_ulSteamIDOwner, description = pDetails.m_rgchDescription,
                            name = pDetails.m_rgchTitle, id = pDetails.m_nPublishedFileId.m_PublishedFileId
                        };
                    } else {
                        log.Log($"Failed getting result from UGC Query with index: {idx}; expected: {callback.m_unNumResultsReturned} returned items");
                    }
                }
            }
            log.Log($"Fetched {matchesGot}/{matchedResults} workshop items.");
            SteamUGC.ReleaseQueryUGCRequest(callback.m_handle);
            queries.Remove(callback.m_handle);
        }
        public static void InitLocal() {
            //ScanForInstalledUMM();
            //ScanForInstalledOwlcatTemplate();
            //ScanForDownloaded();
            //ScanForSubscribed();
        }
        public static bool PreInit() {
            if (Path.GetDirectoryName(Application.persistentDataPath) == AppDataDirName) {
                log.Log($"Error during Pre-Initialization: Persistent data wrong; Expected: {AppDataDirName} Received: {Path.GetDirectoryName(Application.persistentDataPath)}");
                return false;
            }
            AppDataDir = Application.persistentDataPath;
            if (!SteamManager.Initialized) {
                log.Log("Error during Pre-Initialization: Steamworks not initialized");
                return false;
            }
            getModsCallback = Callback<SteamUGCQueryCompleted_t>.Create(OnQueryCompleted);
            try {
                EnsureDirectories();
            } catch (Exception ex) {
                log.Log("Error during Pre-Initialization: Couldn't ensure directories");
                log.Log(ex.ToString());
                return false;
            }
            return true;
        }
        public static void EnsureDirectories() {
            var filePath = Path.Combine(AppDataDir, ModificationManagerSettings);
            ModificationManagerSettings ModManagerSettings = null;
            bool needInit = true;
            if (File.Exists(filePath)) {
                ModManagerSettings = JsonConvert.DeserializeObject<ModificationManagerSettings>(File.ReadAllText(filePath));
                if (ModManagerSettings != null && ModManagerSettings.EnabledModifications != null) {
                    needInit = false;
                    if (ModManagerSettings.SourceDirectory == null || ModManagerSettings.SourceDirectory?.Count < 2) {
                        ModManagerSettings.SourceDirectory = new() { UMM, OwlcatTemplate };
                    }
                }
            }
            if (needInit) {
                ModManagerSettings = new();
                ModManagerSettings.SourceDirectory = new() { UMM, OwlcatTemplate };
                ModManagerSettings.EnabledModifications = new();
            }
            File.WriteAllText(filePath, JsonConvert.SerializeObject(ModManagerSettings, Formatting.Indented));
            new DirectoryInfo(Path.Combine(AppDataDir, UMM)).Create();
            new DirectoryInfo(Path.Combine(AppDataDir, OwlcatTemplate)).Create();
        }
        public static bool OnUnload(UnityModManager.ModEntry modEntry) {
            getModsCallback.Unregister();
            return true;
        }
    }
    public class Mod {
        public ulong id = 0;
        public string name = null;
        public string description = null;
        public ulong authorId = 0;
        public string author = null;
        public string version = null;
        public bool downloaded = false;
        public bool installed = false;
        public bool subscribed = false;
    }
    [Serializable]
    public class ModificationManagerSettings {
        [JsonProperty]
        public List<string> SourceDirectory;
        [JsonProperty]
        public List<string> EnabledModifications;
    }
}