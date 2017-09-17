using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {

    class ModSwitch : Mod {

        private Settings _settings;

        public ModSwitch(ModContentPack content) : base(content) {
            var harmony = HarmonyInstance.Create("DoctorVanGogh.ModSwitch");
            harmony.PatchAll(typeof(ModSwitch).Assembly);

            Log.Message("Initialized ModSwitch patches...");

            _settings =  GetSettings<Settings>();
        }

        public override string SettingsCategory() {
            return "ModSwitch";
        }

        public override void DoSettingsWindowContents(Rect inRect) {
            _settings.DoWindowContents(inRect);
        }

        public void DoModsConfigWindowContents(Rect bottom) {
            _settings.DoModsConfigWindowContents(bottom);
        }

        public void DeleteSet(ModSet modSet) {
            if (_settings.Sets.Remove(modSet))
                WriteSettings();
        }       
    }
}
