using HarmonyLib;
using ModKit;
using Newtonsoft.Json;
using Steamworks;
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
        public PublishedFileId_t id;
        public bool isDownloading;
        public bool isDownloadPending;
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
            SteamUGC.SubscribeItem(id);
            subscribed = true;
            if (Main.settings.ShouldAutoInstallSubscribedItems) {
                Install();
            }
            Main.recentlySubscribed.Add(this);
        }
        public void Unsubscribe() {
            SteamUGC.UnsubscribeItem(id);
            subscribed = false;
            if (Main.settings.ShouldAutoDeleteUnsubscribedItems) {
                Uninstall();
            }
            Main.recentlyUnsubscribed.Add(this);
        }
        public void Download() {
            if (isDownloading && !Main.Downloading.Contains(this)) {
                Main.Downloading.Add(this);
            } else {
                if (!SteamUGC.DownloadItem(id, true)) {
                    Main.log.Log($"Error while trying to download mod {id} with name {name}: nPublishedFileID is invalid or the user is not logged on");
                } else {
                    isDownloading = true;
                    Main.Downloading.Add(this);
                }
            }
        }
        public void Install() {
            if (downloaded) {
                Main.toInstallMods.Remove(this);
                Main.settings.toInstallIds.Remove(id.m_PublishedFileId);
                if (enabled && !isUmm) {
                    // This is an update of an installed Owlcat Template mod. This should be done on game start
                    if (!Main.isFirstInit) {
                        return;
                    }
                }
                Main.settings.toUpdateIds.Remove(id.m_PublishedFileId);
                Main.toUpdateMods.Remove(this);
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
                    modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(Path.Combine(tempDir.FullName, Main.ModInfoName)));
                    if (modInfo == null) {
                        throw new Exception("Deserialization of Modinfo resulted in Null");
                    }
                } catch (Exception ex) {
                    Main.log.Log($"Can't read manifest of mod {name} with id {id} at {Path.Combine(tempDir.FullName, Main.ModInfoName)}; Marking dirty");
                    Main.log.Log(ex.ToString());
                    dirtyManifest = true;
                    return;
                }
                isUmm = new FileInfo(Path.Combine(tempDir.FullName, Main.UMMInfoName)).Exists;
                uniqueName = modInfo.UniqueName;
                DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, modInfo.UniqueName));
                Helper.MoveDirectoryContents(tempDir, targetDir.FullName);
                if (!isUmm) {
                    Helper.HandleManagerSettings(true, modInfo.UniqueName);
                }
                installed = true;
            } else {
                Download();
                Main.settings.toInstallIds.Add(id.m_PublishedFileId);
                Main.toInstallMods.Add(this);
            }
        }
        public void Uninstall() {
            installed = false;
            Main.settings.toRemoveIds.Add(id.m_PublishedFileId);
            Main.toRemoveMods.Add(this);
            DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, uniqueName));
            if (isUmm) {
                File.Delete(Path.Combine(targetDir.FullName, Main.UMMInfoName));
            } else {
                Helper.HandleManagerSettings(false, uniqueName);
            }
        }
        public void UninstallFinally() {
            if (!enabled) {
                try {
                    DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, uniqueName));
                    targetDir.Delete(true);
                    installed = false;
                    Main.toRemoveMods.Remove(this);
                    Main.settings.toRemoveIds.Remove(id.m_PublishedFileId);
                } catch (Exception e) {
                    Main.log.Log($"Error while finally uninstalling mod {id} with name {name}");
                    Main.log.Log(e.ToString());
                }
            }
        }
        public void Update() {
            hasUpdate = false;
            downloaded = false;
            Main.settings.toUpdateIds.Add(id.m_PublishedFileId);
            Main.toUpdateMods.Add(this);
            Install();
        }
        public void InitProperties(SteamUGCDetails_t entry) {
            // This would be the correct way to get the name of a Steam User, but it has the following constrainst:
            // This will only be known to the current user if the other user is in their friends list, on the same game server, in a chat room or lobby, or in a small Steam group with the local user.
            // So it would only show [unknown] for most mods
            // authorName = SteamFriends.GetFriendPersonaName(new CSteamID(entry.m_ulSteamIDOwner));
            description = entry.m_rgchDescription;
            authorID = entry.m_ulSteamIDOwner;
            name = entry.m_rgchTitle;
            id = entry.m_nPublishedFileId;
            InitSteamInfo();
        }
        public void InitSteamInfo() {
            var state = (EItemState)SteamUGC.GetItemState(id);
            downloaded = state.HasFlag(EItemState.k_EItemStateInstalled);
            subscribed = state.HasFlag(EItemState.k_EItemStateSubscribed);
            hasUpdate = state.HasFlag(EItemState.k_EItemStateNeedsUpdate);
            isDownloading = state.HasFlag(EItemState.k_EItemStateDownloading);
            isDownloadPending = state.HasFlag(EItemState.k_EItemStateDownloadPending);
            if (downloaded) {
                SteamUGC.GetItemInstallInfo(id, out var size, out var dir, 256, out var timestamp);
                cacheLocation = dir;
            }
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
                    modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(Path.Combine(tempDir.FullName, Main.ModInfoName)));
                    if (modInfo == null) {
                        throw new Exception("Deserialization of Modinfo resulted in Null");
                    }
                } catch (Exception ex) {
                    Main.log.Log($"Can't read manifest of mod {name} with id {id} at {Path.Combine(tempDir.FullName, Main.ModInfoName)}; Marking dirty");
                    Main.log.Log(ex.ToString());
                    dirtyManifest = true;
                    return;
                }
                isUmm = new FileInfo(Path.Combine(tempDir.FullName, Main.UMMInfoName)).Exists;
                uniqueName = modInfo.UniqueName;
                DirectoryInfo targetDir = new DirectoryInfo(Path.Combine(ModManagerPath, modInfo.UniqueName));
                if (targetDir.Exists) {
                    installed = true;
                    if (isUmm) {
                        if (new FileInfo(Path.Combine(targetDir.FullName, Main.UMMInfoName)).Exists) {
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
                if (installed && !subscribed && Main.settings.ShouldAutoDeleteUnsubscribedItems) {
                    Uninstall();
                } else if (!installed && subscribed && Main.settings.ShouldAutoInstallSubscribedItems) {
                    Install();
                }
            }
        }
    }
}
