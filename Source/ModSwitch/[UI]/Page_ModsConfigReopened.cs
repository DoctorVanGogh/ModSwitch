using RimWorld;

namespace DoctorVanGogh.ModSwitch {
    class Page_ModsConfigReopened : Page_ModsConfig{
        private readonly int _fixedModsHash;

        public Page_ModsConfigReopened(int fixedModsHash) {
            _fixedModsHash = fixedModsHash;
        }

        public override void PostOpen() {
            base.PostOpen();
            ModsConfigUI.fiPage_ModsConfig_ActiveModsWhenOpenedHash.SetValue(this, _fixedModsHash);
        }
    }
}
