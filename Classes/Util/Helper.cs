using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamWorkshopManager {
    public static class Helper {
        public static void EnsureDirectories() {
            var filePath = Path.Combine(Main.AppDataDir, Main.ModificationManagerSettings);
            ModificationManagerSettings ModManagerSettings = null;
            bool needInit = true;
            if (File.Exists(filePath)) {
                ModManagerSettings = JsonConvert.DeserializeObject<ModificationManagerSettings>(File.ReadAllText(filePath));
                if (ModManagerSettings != null && ModManagerSettings.EnabledModifications != null) {
                    needInit = false;
                    if (ModManagerSettings.SourceDirectory == null || ModManagerSettings.SourceDirectory?.Count < 2) {
                        ModManagerSettings.SourceDirectory = new() { Main.UMM, Main.OwlcatTemplate };
                    }
                }
            }
            if (needInit) {
                ModManagerSettings = new();
                ModManagerSettings.SourceDirectory = new() { Main.UMM, Main.OwlcatTemplate };
                ModManagerSettings.EnabledModifications = new();
            }
            File.WriteAllText(filePath, JsonConvert.SerializeObject(ModManagerSettings, Formatting.Indented));
            new DirectoryInfo(Path.Combine(Main.AppDataDir, Main.UMM)).Create();
            new DirectoryInfo(Path.Combine(Main.AppDataDir, Main.OwlcatTemplate)).Create();
        }
        public static void MoveDirectoryContents(DirectoryInfo dir, string basePath) {
            foreach (var file in dir.GetFiles()) {
                string path = Path.Combine(basePath, file.Name);
                if (File.Exists(path)) File.Delete(path);
                file.MoveTo(path);
            }
            foreach (var directory in dir.GetDirectories()) {
                MoveDirectoryContents(directory, Path.Combine(basePath, directory.Name));
            }
        }
        public static void HandleManagerSettings(bool install, string uniqueName) {
            var filePath = Path.Combine(Main.AppDataDir, Main.ModificationManagerSettings);
            ModificationManagerSettings ModManagerSettings = JsonConvert.DeserializeObject<ModificationManagerSettings>(File.ReadAllText(filePath));
            if (install) {
                if (!ModManagerSettings.EnabledModifications.Contains(uniqueName)) {
                    ModManagerSettings.EnabledModifications.Add(uniqueName);
                }
            } else if (ModManagerSettings.EnabledModifications.Contains(uniqueName)) {
                ModManagerSettings.EnabledModifications.Remove(uniqueName);
            }
            File.WriteAllText(filePath, JsonConvert.SerializeObject(ModManagerSettings, Formatting.Indented));
        }
    }
}
