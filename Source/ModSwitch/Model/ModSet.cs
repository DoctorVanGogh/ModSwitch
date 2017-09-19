﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Harmony;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    class ModSet : IExposable {
        private readonly Settings _owner;

        private static readonly FieldInfo fiModsConfig_data;
        private static readonly FieldInfo fiModsConfigData_activeMods;
        private static readonly FieldInfo fiModsConfigData_buildNumber;

        private static readonly Regex rgxSteamModId;

        private static TipSignal? _renameTip;
        private static TipSignal? _deleteTip;


        private TipSignal? _modsTip;
        public bool AutoGenerated;
        public int BuildNumber = -1;

        public List<string> Mods = new List<string>();
        public string Name = String.Empty;

        static ModSet() {
            var tModsConfig = typeof(ModsConfig);
            var tModsConfigData = AccessTools.Inner(tModsConfig, @"ModsConfigData");
            fiModsConfigData_activeMods = AccessTools.Field(tModsConfigData, @"activeMods");
            fiModsConfigData_buildNumber = AccessTools.Field(tModsConfigData, @"buildNumber");
            fiModsConfig_data = AccessTools.Field(tModsConfig, @"data");

            rgxSteamModId = new Regex(@"^\d+$", RegexOptions.Singleline | RegexOptions.Compiled);
        }

        public ModSet(Settings owner) {
            _owner = owner;
        }

        private TipSignal Tip => (_modsTip ?? (_modsTip = new TipSignal(ToString()))).Value;
        private static TipSignal TipRename => (_renameTip ?? (_renameTip = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Rename.Translate()))).Value;
        private static TipSignal TipDelete => (_deleteTip ?? (_deleteTip = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Delete.Translate()))).Value;

        public void ExposeData() {
            Scribe_Collections.Look(ref Mods, false, @"mods");
            Scribe_Values.Look(ref Name, @"name");
            Scribe_Values.Look(ref BuildNumber, @"buildNumber");
            Scribe_Values.Look(ref AutoGenerated, @"autoGenerated", false);
        }

        private string Colorize(string modId) {
            var color = _owner.GetOrInsertAttributes(modId)?.Color;
            return color != null
                ? modId.Colorize(color.Value)
                : modId;
        }


        public override string ToString() {
            return Mods.Aggregate(new StringBuilder(), (sb, m) => sb.Length == 0 ? sb.Append(Colorize(m)) : sb.AppendFormat(@", {0}", Colorize(m)), sb => sb.ToString());
        }

        public void DoWindowContents(Rect rect) {
            const float padding = 2f;
            var height = rect.height;
            var buttonSize = height - 2*padding;

            var leftColumnsWidth = rect.width - 8*padding - 2*buttonSize;

            var left = new Rect(rect.x, rect.y + padding, leftColumnsWidth*0.6f - padding, buttonSize);
            Widgets.Label(left, Name);

            var right = new Rect(rect.x + leftColumnsWidth*0.6f + 3*padding, rect.y + padding, leftColumnsWidth*0.4f, buttonSize);
            Widgets.Label(right, LanguageKeys.keyed.ModSwitch_ModSet_Mods.Translate(Mods.Count));
            TooltipHandler.TipRegion(right, Tip);

            var rctRename = new Rect(rect.x + leftColumnsWidth + 5*padding, rect.y + padding, buttonSize, buttonSize);

            if (ExtraWidgets.ButtonImage(rctRename, Assets.Edit, false, TipRename, rctRename.ContractedBy(4)))
                Find.WindowStack.Add(
                        new Dialog_SetText(
                            s => {
                                Name = s;
                                _owner.Mod.WriteSettings();
                            },
                            Name)
                    );

            var rctDelete = new Rect(rect.x + leftColumnsWidth + 7*padding + buttonSize, rect.y + padding, buttonSize, buttonSize);


            if (ExtraWidgets.ButtonImage(rctDelete, Assets.Delete, false, TipDelete, rctDelete.ContractedBy(4)))
                Find.WindowStack.Add(
                        Dialog_MessageBox.CreateConfirmation(
                            LanguageKeys.keyed.ModSwitch_ModSet_ConfirmDelete.Translate(Name),
                            this.Delete,
                            true,
                            LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate()));
        }

        public void Apply() {

            // mix installed and set mods
            var tmp = Mods
                .Select((m, idx) => new {
                                            id = m,
                                            Index = idx
                                        })
                .FullOuterJoin(
                    ModLister.AllInstalledMods,
                    t => t.id,
                    mmd => mmd.Identifier,
                    (t, mmd, s) => new {
                                           Key = s,
                                           SetIndex = t?.Index,
                                           InstalledIdentifier = mmd?.Identifier
                                       })
                .ToArray();

            // partition by install status
            var notInstalled = tmp.Where(t => t.InstalledIdentifier == null).ToArray();
            var installedMods = tmp.Where(t => t.SetIndex != null && t.InstalledIdentifier != null).OrderBy(t => t.SetIndex).Select(t => t.Key);

            if (notInstalled.Length != 0) {
                var missing = notInstalled
                    .Select(t => new {
                                         Key = t.Key,
                                         IsSteam = rgxSteamModId.IsMatch(t.Key)
                                     })
                    .OrderBy(t => t.Key)
                    .ToArray();

                StringBuilder sb = new StringBuilder(LanguageKeys.keyed.ModSwitch_MissingMods.Translate(Name));
                sb.AppendLine();
                sb.AppendLine();
                foreach (var item in missing) {
                    sb.AppendLine(item.IsSteam ? $" - [Steam] {item.Key}" : $" - {item.Key}");
                }

                Find.WindowStack.Add(
                    new Dialog_MissingMods(
                        sb.ToString(),
                        () => ApplyMods(installedMods),
                        () => {
                            // dont know how to open multiple tabs in steam overlay right now - just pop urls to browser ;)
                            foreach (var mod in missing.Where(t => t.IsSteam)) {                                   
                                Process.Start($"http://steamcommunity.com/sharedfiles/filedetails/?id={mod.Key}");
                            }                           
                        },
                        () => {
                            this.Mods.RemoveAll(s => notInstalled.Any(ni => ni.Key == s));
                            _owner.Mod.WriteSettings();
                            ApplyMods(installedMods);
                        }
                    ));
            }
            else {
                ApplyMods(installedMods);
            }
        }

        private static void ApplyMods(IEnumerable<string> mods) {
            fiModsConfigData_activeMods.SetValue(fiModsConfig_data.GetValue(null), new List<string>(mods));
        }

        public void Delete() {
            if (_owner.Sets.Remove(this)) {
                _owner.Mod.WriteSettings();                
            }
        }


        public static ModSet FromCurrent(string name, Settings owner) {
            object modsConfigData = fiModsConfig_data.GetValue(null);

            return new ModSet(owner) {
                       Name = name,
                       BuildNumber = (int) fiModsConfigData_buildNumber.GetValue(modsConfigData),
                       Mods = new List<string>((IEnumerable<string>) fiModsConfigData_activeMods.GetValue(modsConfigData))
                   };
        }
    }
}
