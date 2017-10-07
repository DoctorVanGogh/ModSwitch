using System.Collections.ObjectModel;
using System.Linq;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    internal class ModAttributesSet : KeyedCollection<string, ModAttributes> {
        public new ModAttributes this[string key] {
            get {
                ModAttributes result;
                if (!TryGetValue(key, out result)) {
                    result = new ModAttributes {Key = key};
                    Add(result);
                }
                return result;
            }
        }

        public ModAttributes this[ModMetaData mod] => this[mod.Identifier];

        protected override string GetKeyForItem(ModAttributes item) {
            return item.Key;
        }

        public bool TryGetValue(string key, out ModAttributes item) {
            item = Items.FirstOrDefault(ma => GetKeyForItem(ma) == key);
            return item != null;
        }
    }
}