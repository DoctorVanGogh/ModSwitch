﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    internal class ModSet : IExposable {
        private static readonly FieldInfo fiModsConfig_data;
        private static readonly FieldInfo fiModsConfigData_version;

        private static readonly Regex rgxSteamModId;

        private static TipSignal? _renameTip;
        private static TipSignal? _deleteTip;
        private static TipSignal? _dragTip;
        private static TipSignal? _exportTip;
        private readonly Settings _owner;


        private TipSignal? _modsTip;
        public bool AutoGenerated;
        public int BuildNumber = -1;

        public List<string> Mods = new List<string>();
        public string Name = string.Empty;


        static ModSet() {
            Type tModsConfig = typeof(ModsConfig);
            Type tModsConfigData = AccessTools.Inner(tModsConfig, @"ModsConfigData");
            fiModsConfigData_version = AccessTools.Field(tModsConfigData, @"version");
            fiModsConfig_data = AccessTools.Field(tModsConfig, @"data");

            rgxSteamModId = new Regex(@"^\d+$", RegexOptions.Singleline | RegexOptions.Compiled);        
         }

        public ModSet(Settings owner) {
            _owner = owner;
        }

        private TipSignal Tip => (_modsTip ?? (_modsTip = new TipSignal(ToString()))).Value;
        private static TipSignal TipRename => (_renameTip ?? (_renameTip = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Rename.Translate()))).Value;
        private static TipSignal TipDelete => (_deleteTip ?? (_deleteTip = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Delete.Translate()))).Value;
        private static TipSignal TipExport => (_exportTip ?? (_exportTip = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Export.Translate()))).Value;

        private static TipSignal TipDrag => (_dragTip ?? (_dragTip = new TipSignal("DragToReorder".Translate()))).Value;

        public void ExposeData() {
            Scribe_Collections.Look(ref Mods, false, @"mods");
            Scribe_Values.Look(ref Name, @"name");
            Scribe_Values.Look(ref BuildNumber, @"buildNumber");
            Scribe_Values.Look(ref AutoGenerated, @"autoGenerated", false);
        }

        public void Apply(Page_ModsConfig owner) {
            // mix installed and set mods
            var tmp = Mods
                      .Select(
                          (m, idx) => new {
                                              id = m,
                                              Index = idx
                                          });

            // partition by install status
            var resolution = ModConfigUtil.TryResolveModsList(tmp, 
                                                              mmd => mmd.FolderName,
                                                              t => t.id,
                                                              (mmd, t) => new { Mod = mmd, Index = t.Index},
                                                              (_, t) => t.id);

            foreach (var x in resolution.Resolved) {   
                Log.Message($"{x.Mod.Name} - {x.Index}");
            }


            string[] notInstalled =resolution.Unresolved;
            ModMetaData[] installedMods = resolution.Resolved
                                                    .OrderBy(t => t.Index)
                                                    .Select(t => t.Mod)
                                                    .ToArray();

            if (notInstalled.Length != 0) {
                var missing = notInstalled
                    .Select(
                        s => new {
                                     Key = s,
                                     IsSteam = rgxSteamModId.IsMatch(s)
                                 })
                    .OrderBy(t => t.Key)
                    .ToArray();

                StringBuilder sb = new StringBuilder(LanguageKeys.keyed.ModSwitch_MissingMods.Translate(Name));
                sb.AppendLine();
                sb.AppendLine();
                foreach (var item in missing)
                    sb.AppendLine(item.IsSteam ? $" - [Steam] {item.Key}" : $" - {item.Key}");

                Find.WindowStack.Add(
                    new Dialog_MissingMods(
                        sb.ToString(),
                        () => ApplyMods(installedMods, owner),
                        () => {
                            // dont know how to open multiple tabs in steam overlay right now - just pop urls to browser ;)
                            foreach (var mod in missing) {
                                string url = Util.BuildWorkshopUrl(mod.Key, mod.Key);
                                Process.Start(url);
                            }

                        },
                        () => {
                            Mods.RemoveAll(s => notInstalled.Any(ni => ni == s));
                            _owner.Mod.WriteSettings();
                            ApplyMods(installedMods, owner);
                        }
                    ));
            } else {
                ApplyMods(installedMods, owner);
            }
        }

        private static void ApplyMods(IEnumerable<ModMetaData> mods, Page_ModsConfig owner) {
            var packageIds = mods.Select(mmd => mmd.PackageId).ToList();

#if DEBUG

            Log.Message(packageIds.Combine(
                            id => $" - {id}", 
                            "\r\n", 
                            "Applying ModSet:\r\n"));
            
            Log.Message(ModLister.AllInstalledMods
                                 .OrderBy(mmd => mmd.PackageId)
                                 .Combine(mmd => $" - {mmd.PackageId}: '{mmd.Name}' [{Util.Combine(mmd.SupportedVersionsReadOnly, v => v.ToString(2))}]",
                                     "\r\n",
                                     "Locally installed mods:\r\n"));
#endif

            ModsConfig.SetActiveToList(packageIds);
            InvalidateCache(owner);
        }


        public static void InvalidateCache(Page_ModsConfig page)  {
            Settings.RecacheSelectedModRequirements(page, Settings.Empty);

            var newCache = ModsConfig.GetModWarnings();

#if DEBUG
            Log.Message(Settings.Page_ModsConfig_GetModWarningsCached(null).Combine(seed: "Old Warnings:"));

            Log.Message(newCache.Combine(seed: "New Warnings:"));
#endif

            Settings.Page_ModsConfig_SetModWarningsCached(null, newCache);
        }


        private string Colorize(string modId, string text = null) {
            var result = text ?? modId;
            Color? color = _owner.Attributes[modId].Color;
            return color != null
                ? result.Colorize(color.Value)
                : result;
        }

        public void Delete() {
            if (_owner.Sets.Remove(this))
                _owner.Mod.WriteSettings();
        }

        public void DoWindowContents(Rect rect, int reorderableGroup) {
            const float padding = 2f;
            float height = rect.height;
            float buttonSize = height - 2 * padding;

            int numButtons = 3;


            ReorderableWidget.Reorderable(reorderableGroup, rect);

            float leftColumnsWidth = rect.width - (4 * numButtons)* padding - numButtons * buttonSize;

            // name + contents (60/40)

            Rect left = new Rect(rect.x, rect.y + padding, leftColumnsWidth * 0.6f - padding, buttonSize);
            Widgets.Label(left, Name);

            Rect right = new Rect(rect.x + leftColumnsWidth * 0.6f + 3 * padding, rect.y + padding, leftColumnsWidth * 0.4f, buttonSize);

            Widgets.Label(right, LanguageKeys.keyed.ModSwitch_ModSet_Mods.Translate(Mods.Count));
            TooltipHandler.TipRegion(right, Tip);

            // actions

            Rect rctRename = new Rect(rect.x + leftColumnsWidth + 5 * padding, rect.y + padding, buttonSize, buttonSize);

            if (ExtraWidgets.ButtonImage(rctRename, Assets.Edit, false, TipRename, rctRename.ContractedBy(4)))
                Find.WindowStack.Add(
                    new Dialog_SetText(
                        s => {
                            Name = s;
                            _owner.Mod.WriteSettings();
                        },
                        Name)
                );

            Rect rctExport = new Rect(rect.x + leftColumnsWidth + 7 * padding + buttonSize, rect.y + padding, buttonSize, buttonSize);

            if (ExtraWidgets.ButtonImage(rctExport, Assets.Extract, false, TipExport, rctExport.ContractedBy(4)))
                Find.WindowStack.Add(
                    new Dialog_SetText(
                        s => {
                            try {
                                ExportModSet(s);
                            } catch (Exception e) {
                                Util.DisplayError(e);                                
                            }
                        },
                        Name,
                        s => MS_GenFilePaths.AllExports
                                            .Select(fi => Path.GetFileNameWithoutExtension(fi.FullName))
                                            .Any(fileName => fileName == s)
                            ? LanguageKeys.keyed.ModSwitch_Error_TargetExists.Translate()
                            : null
                    )
                );

            Rect rctDelete = new Rect(rect.x + leftColumnsWidth + 9 * padding + 2 * buttonSize, rect.y + padding, buttonSize, buttonSize);

            if (ExtraWidgets.ButtonImage(rctDelete, Assets.Delete, false, TipDelete, rctDelete.ContractedBy(4)))
                Find.WindowStack.Add(
                    Dialog_MessageBox.CreateConfirmation(
                        LanguageKeys.keyed.ModSwitch_ModSet_ConfirmDelete.Translate(Name),
                        Delete,
                        true,
                        LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate()));

        }

        private void ExportModSet(string name) {
            var target = MS_GenFilePaths.FilePathForModSetExport(name);
            if (File.Exists(target))
                throw new ArgumentException(LanguageKeys.keyed.ModSwitch_Error_TargetExists_Detailed.Translate(new object[] { target}));
            Scribe.saver.InitSaving(target, "ModSwitch.Export");
            try {
                Scribe.EnterNode(Export_ElementName);
                try {
                    this.ExposeData();
                } finally {
                    Scribe.ExitNode();
                }
            } finally {
                Scribe.saver.FinalizeSaving();
            }
        }

        public const string Export_ElementName = "ModSet";

        public static ModSet FromCurrent(string name, Settings owner) {
            object modsConfigData = fiModsConfig_data.GetValue(null);

            return new ModSet(owner) {
                                         Name = name,
                                         BuildNumber = VersionControl.BuildFromVersionString((string)fiModsConfigData_version.GetValue(modsConfigData)),
                                         Mods = ModsConfig.ActiveModsInLoadOrder.Select(mmd => mmd.FolderName).ToList()
            };
        }


        public override string ToString() {
            var mapModsOntoLocalInstalls = ModConfigUtil.LetOuterJoin(
                Mods,
                mmd => mmd.FolderName,
                s => s);

            return mapModsOntoLocalInstalls.Combine(
                t => Colorize(
                    t.Key,
                    t.MetaData?.OnSteamWorkshop == true
                        ? $"[S] {t.MetaData.Name}"
                        : $"{t.MetaData?.Name ?? t.Key}"));

        }
    }
}