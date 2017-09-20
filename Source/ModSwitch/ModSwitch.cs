using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Harmony;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {

    class ModSwitch : Mod {
        private Settings _settings;

        private static readonly FieldInfo fiModLister_mods;

        static ModSwitch() {
            fiModLister_mods = AccessTools.Field(typeof(ModLister), "mods");
        }

        public ModSwitch(ModContentPack content) : base(content) {
            var harmony = HarmonyInstance.Create("DoctorVanGogh.ModSwitch");          
            harmony.PatchAll(typeof(ModSwitch).Assembly);

            Log.Message("Initialized ModSwitch patches...");

            _settings = GetSettings<Settings>();
        }

        public override string SettingsCategory() {
            return LanguageKeys.keyed.ModSwitch.Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect) {
            _settings.DoWindowContents(inRect);
        }

        public void DoModsConfigWindowContents(Rect bottom) {
            _settings.DoModsConfigWindowContents(bottom);
        }

        public Color GetModColor(ModMetaData mod) {
            return _settings.GetOrInsertAttributes(mod.Identifier)?.Color ?? Color.white;
        }

        public void SetModColor(ModMetaData mod, Color value) {
            _settings.GetOrInsertAttributes(mod.Identifier).Color = value;
        }

        public void MovePosition(ModMetaData mod, Position position) {
            List<ModMetaData> mods  = (List<ModMetaData>) fiModLister_mods.GetValue(null);
          
            if (mods.Remove(mod)) {
                switch (position) {
                    case Position.Top:
                        mods.Insert(0, mod);
                        break;
                    case Position.Bottom:
                        mods.Add(mod);
                        break;
                }
            }
        }
    }
}
