using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamWorkshopManager {

    [Serializable]
    public class OwlcatTemplateClass {
        [JsonProperty]
        public string UniqueName;
        [JsonProperty]
        public string Version;
        [JsonProperty]
        public string DisplayName;
        [JsonProperty]
        public string Description;
        [JsonProperty]
        public string Author;
        [JsonProperty]
        public string ImageName;
        [JsonProperty]
        public string WorkshopId;
        [JsonProperty]
        public string Repository;
        [JsonProperty]
        public string HomePage;
        [JsonProperty]
        public IEnumerable<IDictionary<string, string>> Dependencies;
    }
}
