using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace DoctorVanGogh.ModSwitch {
    class Settings : ModSettings {
        public List<ModSet> Sets = new List<ModSet>();

        public List<ModAttributes> Attributes = new List<ModAttributes>();

        private Vector2 _scrollPosition = new Vector2();
        private IDictionary<string, ModAttributes> _lookup;
        private static TipSignal? _tipCreateNew;
        private static TipSignal? _tipSettings;


        public override void ExposeData() {
            Scribe_Collections.Look(ref Sets, false, "sets");
            Scribe_Collections.Look(ref Attributes, false, "attributes");

            if (Scribe.mode == LoadSaveMode.PostLoadInit) {
                InitLookup();
            }
        }

        private void InitLookup() {
            _lookup = Attributes.ToDictionary(ma => ma.Key);
        }

        public void DoWindowContents(Rect rect) {
            Listing_Standard list = new Listing_Standard(GameFont.Small) {
                                        ColumnWidth = rect.width
                                    };
            list.Begin(rect);

            if (list.ButtonTextLabeled("Import", "ModListBackup")) {
                Find.WindowStack.Add(new Dialog_MessageBox(
                                         "Do you want to append the imported sets or replace all existing sets?",
                                         "Replace",
                                         () => ImportModListBackup(true),
                                         "Append",
                                         () => ImportModListBackup(false),
                                         "Import confirmation",
                                         true
                                     ) {
                                         absorbInputAroundWindow = true,
                                     });
            }

#if DEBUG
            if (list.ButtonTextLabeled("Debug", "ListExisting")) {
                foreach (var modSet in Sets) {
                    Log.Message($"ModSet '{modSet.Name}': {modSet}");
                }
            }
#endif

            const float scrollbarSize = 16f;
            const float sliderHeight = 30f;
            const float gapSize = 4f;

            var r = list.GetRect(rect.height - list.CurHeight);

            int count = Sets.Count;

            Widgets.BeginScrollView(r, ref _scrollPosition, new Rect(0, 0, r.width - scrollbarSize, count*(sliderHeight + gapSize)));
            Vector2 position = new Vector2();

            // render each row
            foreach (var entry in Sets) {
                position.y = position.y + gapSize;

                Rect line = new Rect(0, position.y, r.width - scrollbarSize, sliderHeight);

                entry.DoWindowContents(line);
                position.y += sliderHeight;
            }

            Widgets.EndScrollView();

            list.End();
        }

        private void ImportModListBackup(bool overwrite = false) {
            if (overwrite) {
                Attributes.Clear();
                Sets.Clear();                
            }
            InitLookup();

            string parent = Path.Combine(GenFilePaths.SaveDataFolderPath, "ModListBackup");
            Util.Trace($"Looking at {parent}");
            if (Directory.Exists(parent)) {
                // import configs
                var existing = Directory.GetFiles(parent, "*.rws");

                foreach (var mlbSet in existing) {
                    Util.Trace($"Reading {mlbSet}");

                    XmlDocument doc = new XmlDocument();
                    doc.Load(mlbSet);

                    var set = new ModSet {
                                  Name = $"MLB '{Path.GetFileNameWithoutExtension(mlbSet)}'",
                                  Mods = doc.DocumentElement.SelectNodes(@"//activeMods/li/text()").Cast<XmlNode>().Select(n => n.Value).ToList(),
                                  BuildNumber = Int32.Parse(doc.DocumentElement.SelectSingleNode(@"//buildNumber/text()").Value, CultureInfo.InvariantCulture)
                              };
                    Util.Trace($"Imported {set.Name}: {set}");
                    Sets.Add(set);
                }

                Util.Trace($"Importing settings");

                // import custom settings
                string mods = Path.Combine(parent, "Mod");
                foreach (var mod in Directory.GetDirectories(mods)) {
                    Util.Trace($"Settings {mod}");
                    var settings = Path.Combine(mod, "Settings.xml");
                    if (File.Exists(settings)) {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(settings);

                        ModAttributes attr;
                        var key = Path.GetFileName(mod);
                        if (!_lookup.TryGetValue(key, out attr)) {
                            _lookup[key] = attr = new ModAttributes { Key = key };
                        }
                        var textColor = doc.DocumentElement.SelectSingleNode(@"//textColor");
                        attr.attributes.Add(new MLBAttributes {
                                                altName = doc.DocumentElement.SelectSingleNode(@"//altName/text()")?.Value,
                                                installName = doc.DocumentElement.SelectSingleNode(@"//installName/text()")?.Value,
                                                color = textColor != null
                                                    ? new Color(
                                                          float.Parse(textColor.SelectSingleNode("r/text()")?.Value ?? "1", CultureInfo.InvariantCulture),
                                                          float.Parse(textColor.SelectSingleNode("g/text()")?.Value ?? "1", CultureInfo.InvariantCulture),
                                                          float.Parse(textColor.SelectSingleNode("b/text()")?.Value ?? "1", CultureInfo.InvariantCulture),
                                                          float.Parse(textColor.SelectSingleNode("a/text()")?.Value ?? "1", CultureInfo.InvariantCulture)
                                                      )
                                                    : Color.white
                                            });
                    }
                }

                Attributes.AddRange(_lookup.Values);
            }

            LoadedModManager.GetMod<ModSwitch>().WriteSettings();
        }



        public void DoModsConfigWindowContents(Rect target) {
            if (Widgets.ButtonText(new Rect(target.x, target.y, 120f, 30f), "[MS] Load set")) {
                Find.WindowStack.Add(new FloatMenu(Sets.Select(ms => new FloatMenuOption(ms.Name, ms.Apply)).ToList()));
            }
            var rctNew = new Rect(target.x + 120f + 8f, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctNew, Assets.Document, false, TipCreateNew, rctNew.ContractedBy(4))) {
                Find.WindowStack.Add(
                        new Dialog_SetText(
                            s => {
                                Sets.Add(ModSet.FromCurrent(s));
                                LoadedModManager.GetMod<ModSwitch>().WriteSettings();
                            },
                            "New mod set"
                        ));
            }
            var rctSettings = new Rect(350f - 30f, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctSettings, Assets.Settings, false, TipSettings, rctSettings.ContractedBy(4))) {
                var settings = new Dialog_ModSettings();
                AccessTools.Field(typeof(Dialog_ModSettings), "selMod").SetValue(settings, LoadedModManager.GetMod<ModSwitch>());
                Find.WindowStack.Add(settings);
            }

        }

        public static TipSignal TipSettings => (_tipSettings ?? (_tipSettings = new TipSignal("Settings"))).Value;

        public static TipSignal TipCreateNew => (_tipCreateNew ?? (_tipCreateNew = new TipSignal("Create new"))).Value;
    }
}
