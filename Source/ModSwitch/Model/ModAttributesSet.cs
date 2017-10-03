using System.Collections.ObjectModel;
using System.Linq;

namespace DoctorVanGogh.ModSwitch {
    class ModAttributesSet : KeyedCollection<string, ModAttributes> {
        protected override string GetKeyForItem(ModAttributes item) {
            return item.Key;
        }

        public bool TryGetValue(string key, out ModAttributes item) {
            item = Items.FirstOrDefault(ma => GetKeyForItem(ma) == key);
            return item != null;
        }
    }
}
