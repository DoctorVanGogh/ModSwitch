using System.Linq;
using Harmony;
using RimWorld;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    class Dialog_ModsConfigSettings : Dialog_ModSettings {
        private Mod _mod;
        private int _initialHash;

        public Dialog_ModsConfigSettings(Mod mod) {
            _mod = mod;
        }

        public override void PreOpen() {
            var modsConfig = Find.WindowStack.Windows.OfType<Page_ModsConfig>().FirstOrDefault();
            _initialHash = (int) ModsConfigUI.fiPage_ModsConfig_ActiveModsWhenOpenedHash.GetValue(modsConfig);
            ModsConfigUI.ChangeAction = ModsConfigUI.ModsChangeAction.Ignore;
            modsConfig.Close();
            base.PreOpen();
            AccessTools.Field(typeof(Dialog_ModSettings), @"selMod").SetValue(this, _mod);
        }

        public override void PreClose() {
            base.PreClose();
            ModsConfigUI.ChangeAction = ModsConfigUI.ModsChangeAction.Query;
            Find.WindowStack.Add(new Page_ModsConfigReopened(_initialHash));
        }
    }
}
