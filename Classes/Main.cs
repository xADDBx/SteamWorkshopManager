using HarmonyLib;
using ModKit;
using Newtonsoft.Json;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityModManagerNet;

namespace SteamWorkshopManager {
    public static class Main {
        internal static UnityModManager.ModEntry.ModLogger log;
        public static Settings settings;
        internal static UnityModManager.ModEntry modEntry;
        internal static UI.Browser<Mod, Mod> ModBrowser = new(true, true);
        public static AppId_t appId => SteamUtils.GetAppID();
        public const string AppDataDirName = "Warhammer 40000 Rogue Trader";
        public const string UMMDirName = "UnityModManager";
        public const string OwlcatTemplateDirName = "Modifications";
        public const string ModificationManagerSettingsName = "OwlcatModificationManagerSettings.json";
        public const string UMMInfoName = "Info.json";
        public const string ModInfoName = "OwlcatModificationManifest.json";
        public static string AppDataDir;
        public static HashSet<Mod> mods = new();
        public static HashSet<Mod> DownloadedOrSubscribedOrInstalled = new();
        public static HashSet<Mod> toInstallMods = new();
        public static HashSet<Mod> toRemoveMods = new();
        public static HashSet<Mod> Downloading = new();
        public static bool finishedOnlineInit = false;
        public static bool finishedLocalInit = false;
        public static bool isFirstInit = true;
        public static int timer = 0;
        public static CallResult<SteamUGCQueryCompleted_t> queryCompleted;
        public static Callback<DownloadItemResult_t> downloadCompleted;

        public static bool Load(UnityModManager.ModEntry modEntry) {
            log = modEntry.Logger;
            Main.modEntry = modEntry;
            if (!PreInit()) return false;
            queryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create(OnUGCQueryResult);
            downloadCompleted = Callback<DownloadItemResult_t>.Create(OnDownloadComplete);
            modEntry.OnGUI = OnGUI;
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnSaveGUI = OnSaveGUI;
            return Init();
        }
        private static bool startShow = false;
        internal static List<Mod> recentlySubscribed = new();
        internal static List<Mod> recentlyUnsubscribed = new();
        internal static HashSet<Action> todo = new();
        internal static Exception lastException = null;
        internal static bool lastFrameWasException = false;
        public static void OnGUI(UnityModManager.ModEntry modEntry) {
            try {
                if (lastException != null || (lastFrameWasException && Event.current.type == EventType.Repaint)) {
                    UI.Label(lastException.ToString().red());
                    UI.ActionButton("Reset", () => { lastException = null; lastFrameWasException = true; });
                    return;
                }
                UI.ActionButton("Reset Cache", () => todo.Add(Refresh));
                UI.Toggle("Should automatically delete unsubscribed items (still needs refresh or restart if done over Steam Website)", ref settings.ShouldAutoDeleteUnsubscribedItems);
                UI.Toggle("Should automatically delete subscribed items (still needs refresh or restart if done over Steam Website)", ref settings.ShouldAutoInstallSubscribedItems);
                lastFrameWasException = false;
                if (startShow) {
                    ModBrowser.OnGUI(DownloadedOrSubscribedOrInstalled, () => mods, m => m, m => $"{m.name} {m.description} {m.id} {m.authorName} {m.authorID}", m => new string[] { m.name ?? "" }, () => {
                        UI.Label("Name", UI.Width(200));
                        UI.Label("Id", UI.Width(100));
                        UI.Label("Author", UI.Width(100));
                        UI.Label("Description", UI.Width(600));
                        UI.Label("Subscribed", UI.Width(160));
                        UI.Label("Downloaded", UI.Width(140));
                        UI.Label("Installed", UI.Width(140));
                        UI.Label("Enabled", UI.Width(140));
                    },
                        (def, item) => {
                            if (def.dirtyManifest) {
                                using (UI.VerticalScope()) {
                                    UI.Label("Encountered problem while reading OwlcatModificationManifest".red().bold());
                                }
                            }
                            UI.Label(def.name ?? "", UI.Width(200));
                            UI.Label(def.id.ToString() ?? "", UI.Width(100));
                            UI.Label(def.authorName ?? def.authorID.ToString() ?? "", UI.Width(100));
                            UI.Label(def.description ?? "", UI.Width(600));
                            if (def.subscribed) {
                                UI.Label(UI.ChecklyphOn, UI.Width(15));
                                UI.ActionButton("Unsubscribe", () => todo.Add(() => def.Unsubscribe()), UI.Width(125));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(15));
                                UI.ActionButton("Subscribe", () => todo.Add(() => def.Subscribe()), UI.Width(125));
                            }
                            UI.Space(20);
                            if (def.downloaded) {
                                UI.Label(UI.ChecklyphOn, UI.Width(15));
                                UI.Space(105);
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(15));
                                UI.ActionButton("Download", () => todo.Add(() => def.Download()), UI.Width(105));
                            }
                            UI.Space(20);
                            if (def.installed) {
                                UI.Label(UI.ChecklyphOn, UI.Width(15));
                                UI.ActionButton("Uninstall", () => todo.Add(() => def.Uninstall()), UI.Width(105));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(15));
                                UI.ActionButton("Install", () => todo.Add(() => def.Install()), UI.Width(105));
                            }
                            UI.Space(20);
                            if (def.enabled) {
                                UI.Label(UI.ChecklyphOn, UI.Width(50));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(50));
                            }
                            UI.ActionButton("Reinstall/Update", () => todo.Add(() => def.Update()), UI.Width(200));
                        });
                }
                if (Event.current.type == EventType.Repaint && finishedOnlineInit) {
                    timer += 1;
                    foreach (var action in todo) {
                        action();
                    }
                    todo.Clear();
                    if (timer == 60) {
                        SteamAPI.RunCallbacks();
                        timer = 0;
                        foreach (var needInstall in toInstallMods.ToList()) {
                            if (needInstall.downloaded) {
                                needInstall.Install();
                            } else {
                                if (!Downloading.Contains(needInstall)) {
                                    needInstall.Download();
                                }
                            }
                        }
                    }
                    foreach (var mod in recentlyUnsubscribed) {
                        DownloadedOrSubscribedOrInstalled.Remove(mod);
                    }
                    recentlyUnsubscribed.Clear();
                    foreach (var mod in recentlySubscribed) {
                        DownloadedOrSubscribedOrInstalled.Add(mod);
                    }
                    recentlySubscribed.Clear();
                    startShow = true;
                }
            } catch (Exception ex) {
                lastException = ex;
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
                if (mods.Count == 0) return false;
            }
            log.Log($"Successfully initialized online. Found {mods.Count} mods in workshop.");
            finishedOnlineInit = true;
            try {
                InitLocal();
            } catch (Exception ex) {
                log.Log("Error while trying to initialize local caches");
                log.Log(ex.ToString());
                return false;
            }
            finishedLocalInit = true;
            isFirstInit = false;
            return true;
        }
        public static void OnDownloadComplete(DownloadItemResult_t pCallback) {
            if (pCallback.m_unAppID == appId) {
                var mod = Downloading.First(m => m.id == pCallback.m_nPublishedFileId);
                if (mod != null) {
                    Downloading.Remove(mod);
                    mod.downloaded = true;
                    mod.isDownloading = false;
                    mod.InitSteamInfo();
                }
            }
        }
        private static bool hasQueryResult;
        private static SteamUGCQueryCompleted_t currentPageResult;
        public static void OnUGCQueryResult(SteamUGCQueryCompleted_t pCallback, bool bIOFailure) {
            if (pCallback.m_eResult != EResult.k_EResultOK || bIOFailure) {
                // Logging; Error handling
            } else {
                currentPageResult = pCallback;
            }
            hasQueryResult = true;
        }
        public static void InitOnline() {
            log.Log("Try init online.");
            uint currentCount = 0;
            uint currentPageNumber = 0;
            uint totalCount = 0;

            do {
                currentPageNumber += 1;
                var queryHandle = SteamUGC.CreateQueryAllUGCRequest(EUGCQuery.k_EUGCQuery_RankedByVote, EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items, appId, appId, currentPageNumber);
                if (queryHandle == UGCQueryHandle_t.Invalid) {
                    log.Log($"Error while trying to init online: current running app is not {appId} or an internal error occured.");
                    return;
                }
                SteamAPICall_t apiCall = SteamUGC.SendQueryUGCRequest(queryHandle);
                queryCompleted.Set(apiCall);
                // Since queries are (hopefully) reasonably fast? blocking and waiting for the result here.
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < 2) {
                    Thread.Sleep(50);
                    SteamAPI.RunCallbacks();
                }
                stopwatch.Stop();
                if (!hasQueryResult) {
                    throw new Exception("Init online: got no query result within 2 seconds; SteamAPI Connection problem?");
                }
                if (totalCount == 0) {
                    totalCount = currentPageResult.m_unTotalMatchingResults;
                }

                currentCount += currentPageResult.m_unNumResultsReturned;

                try {
                    for (uint i = 0; i < currentPageResult.m_unNumResultsReturned; i++) {
                        SteamUGC.GetQueryUGCResult(currentPageResult.m_handle, i, out var entry);
                        var m = new Mod();
                        m.InitProperties(entry);
                        mods.Add(m);

                        if (m.subscribed || m.downloaded) {
                            DownloadedOrSubscribedOrInstalled.Add(m);
                        }

                        if (m.isDownloading || m.isDownloadPending) {
                            m.Download();
                        }
                    }
                } catch (Exception ex) {
                    log.Log("Error while init online: Exception while iterating over page results");
                    log.Log(ex.ToString());
                }

                if (currentPageResult.m_unNumResultsReturned == 0) {
                    log.Log("Error while init online: Got page with 0 results; breaking to prevent infinite loop");
                }

            } while (currentCount < totalCount);
        }
        public static void InitLocal() {
            var dir = new DirectoryInfo(Path.Combine(AppDataDir, "ModTemp"));
            log.Log($"Creating temp dir at {dir}");
            ModificationManagerSettings ModManagerSettings = null;
            try {
                var filePath = Path.Combine(AppDataDir, ModificationManagerSettingsName);
                ModManagerSettings = JsonConvert.DeserializeObject<ModificationManagerSettings>(File.ReadAllText(filePath));
            } catch (Exception ex) {
                log.Log("Error during Local Init: Deserialization of OwlcatModificationManagerSettings.json failed");
                log.Log(ex.ToString());
            }
            foreach (var mod in DownloadedOrSubscribedOrInstalled) {
                mod.InitState(dir, ModManagerSettings);
            }
            toRemoveMods.Union(settings.toRemoveIds.Select(id => mods.Where(m => m.id.m_PublishedFileId == id).First()));
            toInstallMods.Union(settings.toInstallIds.Select(id => mods.Where(m => m.id.m_PublishedFileId == id).First()));
            if (isFirstInit) {
                foreach (var toRemove in toRemoveMods.ToList()) {
                    toRemove.UninstallFinally();
                }
            }
        }
        public static bool PreInit() {
            if (Path.GetDirectoryName(Application.persistentDataPath) == AppDataDirName) {
                log.Log($"Error during Pre-Initialization: Persistent data wrong; Expected: {AppDataDirName} Received: {Path.GetDirectoryName(Application.persistentDataPath)}");
                return false;
            }
            AppDataDir = Application.persistentDataPath;
            try {
                if (!SteamAPI.Init()) {
                    throw new Exception("SteamAPI.Init returned false");
                }
            } catch (Exception e) {
                log.Log("Error during Pre-Initialization: Can't init SteamClient");
                log.Log(e.ToString());
                return false;
            }
            try {
                Helper.EnsureDirectories();
            } catch (Exception ex) {
                log.Log("Error during Pre-Initialization: Couldn't ensure directories");
                log.Log(ex.ToString());
                return false;
            }
            return true;
        }
        // TODO
        public static void Refresh() {
            toRemoveMods = new();
            toInstallMods = new();
            mods = new();
            Downloading = new();
            DownloadedOrSubscribedOrInstalled = new();
            InitOnline();
            InitLocal();
        }
    }
}