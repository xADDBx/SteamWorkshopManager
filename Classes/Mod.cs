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

namespace SteamWorkshopManager {

    public class Mod {
        public Item steamItem;
        public ulong id = 0;
        public string name = null;
        public string description = null;
        public string authorName = null;
        public ulong authorID = 0;
        public string version = null;
        public bool downloaded = false;
        public bool installed = false;
        public bool enabled = false;
        public bool subscribed = false;
        public bool hasUpdate = false;
        public bool isUmm;
        public bool dirtyManifest = false;
        public string uniqueName = null;
        public string cacheLocation = null;
        public string ModManagerPath => isUmm ? Path.Combine(Main.AppDataDir, "UnityModManager") : Path.Combine(Main.AppDataDir, "Modifications");
        public void Subscribe() {
            steamItem.Subscribe();
            subscribed = true;
            Install();
            Main.recentlySubscribed.Add(this);
        }
        public void Unsubscribe() {
            steamItem.Unsubscribe();
            subscribed = false;
            Uninstall();
            Main.recentlyUnsubscribed.Add(this);
        }
        public void Download() {
            // Passing new CancellationTokenSource to prevent default 1minute timeout
            Main.Downloading[this] = steamItem.DownloadAsync(null, 60, new());
        }
        public void Install() {
            if (downloaded) {
                var tempDir = new DirectoryInfo(Path.Combine(Main.AppDataDir, "ModTemp"));
                if (tempDir.Exists) {
                    tempDir.Delete(true);
                }
                tempDir.Create();
                string pathToZip = null;
                if (cacheLocation.EndsWith(@"_legacy.bin")) {
                    pathToZip = cacheLocation;
                } else {
                    var zips = Directory.GetFiles(cacheLocation, "*.zip");
                    if (zips.Length > 0) {
                        // I'll assume there will only ever be one zip?
                        pathToZip = Path.Combine(cacheLocation, zips[0]);
                    }
                }
                if (pathToZip != null) {
                    ZipFile.ExtractToDirectory(pathToZip, tempDir.FullName);
                } else {
                    foreach (var file in new DirectoryInfo(cacheLocation).GetFiles()) {
                        File.Copy(file.FullName, Path.Combine(tempDir.FullName, file.Name), true);
                    }
                }
                OwlcatTemplateClass modInfo = null;
                try {
                    modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(Path.Combine(tempDir.FullName, Main.ModInfoFile)));
                    if (modInfo == null) {
                        throw new Exception("Deserialization of Modinfo resulted in Null");
                    }
                } catch (Exception ex) {
                    Main.log.Log($"Can't read manifest of mod {name} with id {id} at {Path.Combine(tempDir.FullName, Main.ModInfoFile)}; Marking dirty");
                    Main.log.Log(ex.ToString());
                    dirtyManifest = true;
                    return;
                }
                isUmm = new FileInfo(Path.Combine(tempDir.FullName, Main.UMMInfoFile)).Exists;
                uniqueName = modInfo.UniqueName;
                DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, modInfo.UniqueName));
                Helper.MoveDirectoryContents(tempDir, targetDir.FullName);
                if (!isUmm) {
                    Helper.HandleManagerSettings(true, modInfo.UniqueName);
                }
                installed = true;
            } else {
                Download();
                Main.settings.toInstallIds.Add(id);
                Main.toInstallMods.Add(this);
            }
        }
        public void Uninstall() {
            installed = false;
            Main.settings.toRemoveIds.Add(id);
            Main.toRemoveMods.Add(this);
            DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, uniqueName));
            if (isUmm) {
                File.Delete(Path.Combine(targetDir.FullName, Main.UMMInfoFile));
            } else {
                Helper.HandleManagerSettings(false, uniqueName);
            }
        }
        public void UninstallFinally() {
            if (!enabled) {
                DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, uniqueName));
                targetDir.Delete(true);
                installed = false;
                Main.toRemoveMods.Remove(this);
                Main.settings.toRemoveIds.Remove(id);
            }
        }
        public void Update() {
            hasUpdate = false;
            downloaded = false;
            Install();
        }
        public void InitProperties(Item entry) {
            authorName = entry.Owner.Name;
            description = entry.Description;
            authorID = entry.Owner.Id.Value;
            name = entry.Title;
            id = entry.Id;
            downloaded = entry.IsInstalled;
            subscribed = entry.IsSubscribed;
            hasUpdate = entry.NeedsUpdate;
            cacheLocation = entry.Directory;
            steamItem = entry;
        }
        public void InitState(DirectoryInfo tempDir, ModificationManagerSettings ModManagerSettings) {
            if (downloaded) {
                if (tempDir.Exists) {
                    tempDir.Delete(true);
                }
                tempDir.Create();
                string pathToZip = null;
                if (cacheLocation.EndsWith(@"_legacy.bin")) {
                    pathToZip = cacheLocation;
                } else {
                    var zips = Directory.GetFiles(cacheLocation, "*.zip");
                    if (zips.Length > 0) {
                        // I'll assume there will only ever be one zip?
                        pathToZip = Path.Combine(cacheLocation, zips[0]);
                    }
                }
                if (pathToZip != null) {
                    ZipFile.ExtractToDirectory(pathToZip, tempDir.FullName);
                } else {
                    foreach (var file in new DirectoryInfo(cacheLocation).GetFiles()) {
                        File.Copy(file.FullName, Path.Combine(tempDir.FullName, file.Name), true);
                    }
                }
                OwlcatTemplateClass modInfo = null;
                try {
                    modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(Path.Combine(tempDir.FullName, Main.ModInfoFile)));
                    if (modInfo == null) {
                        throw new Exception("Deserialization of Modinfo resulted in Null");
                    }
                } catch (Exception ex) {
                    Main.log.Log($"Can't read manifest of mod {name} with id {id} at {Path.Combine(tempDir.FullName, Main.ModInfoFile)}; Marking dirty");
                    Main.log.Log(ex.ToString());
                    dirtyManifest = true;
                    return;
                }
                isUmm = new FileInfo(Path.Combine(tempDir.FullName, Main.UMMInfoFile)).Exists;
                uniqueName = modInfo.UniqueName;
                DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, modInfo.UniqueName));
                if (targetDir.Exists) {
                    installed = true;
                    if (isUmm) {
                        if (new FileInfo(Path.Combine(targetDir.FullName, Main.UMMInfoFile)).Exists) {
                            enabled = true;
                        }
                    } else {
                        if (ModManagerSettings != null) {
                            if (ModManagerSettings.EnabledModifications.Contains(modInfo.UniqueName)) {
                                enabled = true;
                            }
                        }
                    }
                }
            }
        }
    }
}
