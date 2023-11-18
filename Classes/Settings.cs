using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace SteamWorkshopManager {
    public class Settings : UnityModManager.ModSettings {
        public int browserDetailSearchLimit = 20;
        public int browserSearchLimit = 50;
        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
    }
}
