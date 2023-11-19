using HarmonyLib;
using ModKit;
using Newtonsoft.Json;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager.Param;

namespace SteamWorkshopManager {
    public static class Main {
        internal static UnityModManager.ModEntry.ModLogger log;
        public static Settings settings;
        internal static UnityModManager.ModEntry modEntry;
        internal static UI.Browser<Mod, Mod> ModBrowser = new(true, true);
        public static AppId appId = 2186680;
        public const string AppDataDirName = "WH 40000 RT";
        public const string UMM = "UnityModManager";
        public const string OwlcatTemplate = "Modifications";
        public const string ModificationManagerSettings = "OwlcatModificationManagerSettings.json";
        public const string UMMInfoFile = "Info.json";
        public const string ModInfoFile = "OwlcatModificationManifest.json";
        public static string AppDataDir;
        public static HashSet<Mod> mods = new();
        public static HashSet<Mod> DownloadedOrSubscribedOrInstalled = new();
        public static HashSet<Mod> toInstallMods = new();
        public static HashSet<Mod> toRemoveMods = new();
        public static Dictionary<Mod, Task<bool>> Downloading = new();
        public static bool finishedOnlineInit = false;
        public static bool finishedLocalInit = false;
        public static int timer = 0;

        public static bool Load(UnityModManager.ModEntry modEntry) {
            log = modEntry.Logger;
            Main.modEntry = modEntry;
            if (!PreInit()) return false;
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
                lastFrameWasException = false;
                if (startShow) {
                    UI.ActionButton("Refresh", () => Refresh());
                    ModBrowser.OnGUI(DownloadedOrSubscribedOrInstalled, () => mods, m => m, m => $"{m.name} {m.description} {m.id} {m.authorName} {m.authorID}", m => new string[] { m.name ?? "" }, () => {
                        UI.Label("Name", UI.Width(200));
                        UI.Label("Id", UI.Width(100));
                        UI.Label("Author", UI.Width(100));
                        UI.Label("Description", UI.Width(600));
                        UI.Label("Subscribed", UI.Width(200));
                        UI.Label("Downloaded", UI.Width(200));
                        UI.Label("Installed", UI.Width(200));
                        UI.Label("Enabled", UI.Width(200));
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
                                UI.Label(UI.ChecklyphOn, UI.Width(100));
                                UI.ActionButton("Unsubscribe", () => todo.Add(() => def.Unsubscribe()), UI.Width(100));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(100));
                                UI.ActionButton("Subscribe", () => todo.Add(() => def.Subscribe()), UI.Width(100));
                            }
                            if (def.downloaded) {
                                UI.Label(UI.ChecklyphOn, UI.Width(200));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(100));
                                UI.ActionButton("Download", () => todo.Add(() => def.Download()), UI.Width(100));
                            }
                            if (def.installed) {
                                UI.Label(UI.ChecklyphOn, UI.Width(100));
                                UI.ActionButton("Uninstall", () => todo.Add(() => def.Uninstall()), UI.Width(100));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(100));
                                UI.ActionButton("Install", () => todo.Add(() => def.Install()), UI.Width(100));
                            }
                            if (def.enabled) {
                                UI.Label(UI.ChecklyphOn, UI.Width(100));
                            } else {
                                UI.Label(UI.CheckGlyphOff, UI.Width(100));
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
                        timer = 0;
                        foreach (var download in Downloading.Keys.ToList()) {
                            if (Downloading[download].IsCompleted) {
                                if (Downloading[download].Result) {
                                    // I'd assume there aren't too many downloads started here so bundling calls isn't really worth it
                                    var page = Query.GameManagedItems.WithFileId(download.id).GetPageAsync(1).GetAwaiter().GetResult();
                                    if (page.HasValue) {
                                        foreach (var entry in page.Value.Entries) {
                                            download.InitProperties(entry);
                                        }
                                        if (page.Value.ResultCount != 1) {
                                            log.Log($"Encountered error while trying to download {download.name} with id {download.id}: Got {page.Value.ResultCount} results instead of 1");
                                        }
                                    } else {
                                        log.Log($"Encountered error while trying to download {download.name} with id {download.id}: Can't query item");
                                    }
                                } else {
                                    log.Log($"Encountered error while trying to download {download.name} with id {download.id}");
                                }
                                Downloading.Remove(download);
                            }
                        }
                        foreach (var needInstall in toInstallMods) {
                            if (needInstall.downloaded) {
                                needInstall.Install();
                            } else {
                                if (!Downloading.ContainsKey(needInstall)) {
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
                // Since queries are async (but hopefully reasonably fast?) blocking awaiting the result here.
                InitOnline().GetAwaiter().GetResult();
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
            return true;
        }
        public async static Task InitOnline() {
            log.Log($"Try init online.");
            var items = Query.GameManagedItems.SortByTitleAsc();
            var currentCount = 0;
            var currentPageNumber = 0;
            var totalCount = 0;
            do {
                currentPageNumber += 1;
                var currentPage = await items.GetPageAsync(currentPageNumber);
                if (totalCount == 0) {
                    totalCount = currentPage.Value.TotalCount;
                }
                currentCount += currentPage.Value.ResultCount;
                try {
                    foreach (var entry in currentPage.Value.Entries) {
                        var m = new Mod();
                        m.InitProperties(entry);
                        mods.Add(m);
                        if (entry.IsSubscribed || entry.IsInstalled) {
                            DownloadedOrSubscribedOrInstalled.Add(m);
                        }
                        // Want a handle for the download to allow checking for completion without having to persistently 
                        if (entry.IsDownloading || entry.IsDownloadPending) {
                            m.Download();
                        }
                    }
                } catch (Exception ex) {
                    log.Log("Error while init online: Exception while iterating over page results");
                    log.Log(ex.ToString());
                }
                if (currentPage.Value.ResultCount == 0) {
                    log.Log("Error while init online: Got page with 0 results; breaking to prevent infinite loop");
                }
            } while (currentCount < totalCount);
        }
        public static void InitLocal() {
            var dir = new DirectoryInfo(Path.Combine(AppDataDir, "ModTemp"));
            log.Log($"Creating temp dir at {dir}");
            ModificationManagerSettings ModManagerSettings = null;
            try {
                var filePath = Path.Combine(AppDataDir, ModInfoFile);
                ModManagerSettings = JsonConvert.DeserializeObject<ModificationManagerSettings>(File.ReadAllText(filePath));
            } catch (Exception ex) {
                log.Log("Error during Local Init: Deserialization of OwlcatModificationManagerSettings.json failed");
                log.Log(ex.ToString());
            }
            foreach (var mod in DownloadedOrSubscribedOrInstalled) {
                mod.InitState(dir, ModManagerSettings);
            }
            toRemoveMods.Union(settings.toRemoveIds.Select(id => mods.Where(m => m.id == id).First()));
            toInstallMods.Union(settings.toInstallIds.Select(id => mods.Where(m => m.id == id).First()));
            foreach (var toRemove in toRemoveMods) {
                toRemove.UninstallFinally();
            }
        }
        public static bool PreInit() {
            if (Path.GetDirectoryName(Application.persistentDataPath) == AppDataDirName) {
                log.Log($"Error during Pre-Initialization: Persistent data wrong; Expected: {AppDataDirName} Received: {Path.GetDirectoryName(Application.persistentDataPath)}");
                return false;
            }
            AppDataDir = Application.persistentDataPath;
            try {
                SteamClient.Init(appId);
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

        }
    }
}