using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamWorkshopManager {
    [Serializable]
    public class ModificationManagerSettings {
        [JsonProperty]
        public List<string> SourceDirectory;
        [JsonProperty]
        public List<string> EnabledModifications;
    }
}
