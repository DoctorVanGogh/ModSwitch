using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    class ModSet : IExposable {

        private static readonly FieldInfo fiModsConfig_data;
        private static readonly FieldInfo fiModsConfigData_activeMods;

        static ModSet() {
            var tModsConfig = typeof(ModsConfig);
            var tModsConfigData = AccessTools.Inner(tModsConfig, "ModsConfigData");
            fiModsConfigData_activeMods = AccessTools.Field(tModsConfigData, "activeMods");
            fiModsConfig_data = AccessTools.Field(tModsConfig, "data");
        }

        public List<string> Mods = new List<string>();

        public string Name = String.Empty;
        private TipSignal? _tip;

        public void ExposeData() {
            Scribe_Collections.Look(ref Mods, false, "mods");
            Scribe_Values.Look(ref Name, "name");
        }

        private TipSignal Tip {
            get { return (_tip ?? (_tip = new TipSignal(ToString()))).Value; }
        }

        public override string ToString() {
            return Mods.Aggregate(new StringBuilder(), (sb, m) => sb.Length == 0 ? sb.Append(m) : sb.AppendFormat(", {0}", m), sb => sb.ToString());
        }

        public void DoWindowContents(Rect rect) {
            GUI.Label(rect, Name);
            TooltipHandler.TipRegion(rect, Tip);
        }

        public void Apply() {
            // TODO: improve performance, dont do multiple joins over same data...

            // join set with installed mods while perserving order from set
            List<string> installedMods = Mods
                .Select((m, idx) => new {id = m, Index = idx})
                .Join(ModLister.AllInstalledMods, t => t.id, md => md.Identifier, (t, md) => new {Id = t.id, Index = t.Index})
                .OrderBy(t => t.Index)
                .Select(t => t.Id)
                .ToList();

            fiModsConfigData_activeMods.SetValue(fiModsConfig_data.GetValue(null), new List<string>(installedMods));

            if (installedMods.Count != Mods.Count) {
                var missingMods = Mods.Where(m => !installedMods.Contains(m));
                StringBuilder sb = new StringBuilder($"Some mods from {Name} are not currently installed:");
                sb.AppendLine();
                sb.AppendLine();
                foreach (var item in missingMods) {
                    sb.AppendLine($" - {item}");
                }

                Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString(), title: "Missing mods"));
            }
        }
    }
}
