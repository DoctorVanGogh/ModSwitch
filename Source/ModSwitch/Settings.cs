using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DoctorVanGogh.ModSwitch {
    class Settings : ModSettings {
        private static TipSignal? _tipCreateNew;
        private static TipSignal? _tipSettings;
        private static TipSignal? _tipApply;
        private static TipSignal? _tipUndo;
        private IDictionary<string, ModAttributes> _lookup;

        private Vector2 _scrollPosition;

        private ModSet _undo;

        public List<ModAttributes> Attributes = new List<ModAttributes>();
        public List<ModSet> Sets = new List<ModSet>();

        public static TipSignal TipSettings => (_tipSettings ?? (_tipSettings = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Settings.Translate()))).Value;

        public static TipSignal TipCreateNew => (_tipCreateNew ?? (_tipCreateNew = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Create.Translate()))).Value;

        public static TipSignal TipApply => (_tipApply ?? (_tipApply = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Apply.Translate()))).Value;

        public static TipSignal TipUndo => (_tipUndo ?? (_tipUndo = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Undo.Translate()))).Value;

        public Settings() {
            _lookup = Attributes.ToDictionary(ma => ma.Key);
        }

        public override void ExposeData() {
            Scribe_Collections.Look(ref Sets, false, @"sets", LookMode.Undefined, this);
            Scribe_Collections.Look(ref Attributes, false, @"attributes");

            if (Scribe.mode == LoadSaveMode.LoadingVars) InitLookup();
        }

        private void InitLookup() {
            _lookup = Attributes.ToDictionary(ma => ma.Key);
        }

        internal ModAttributes GetOrInsertAttributes(string key) {
            ModAttributes result;
            if (!_lookup.TryGetValue(key, out result)) {
                result = new ModAttributes { Key = key };
                Attributes.Add(result);
                _lookup[key] = result;
            }
            return result;
        }

        public void DoWindowContents(Rect rect) {
            Listing_Standard list = new Listing_Standard(GameFont.Small) {
                                        ColumnWidth = rect.width
                                    };
            list.Begin(rect);

            if (list.ButtonTextLabeled(LanguageKeys.keyed.ModSwitch_Import.Translate(), "ModListBackup"))
                Find.WindowStack.Add(new Dialog_MessageBox(
                                         LanguageKeys.keyed.ModSwitch_Import_Text.Translate(),
                                         LanguageKeys.keyed.ModSwitch_Import_Choice_Replace.Translate(),
                                         () => ImportModListBackup(true),
                                         LanguageKeys.keyed.ModSwitch_Import_Choice_Append.Translate(),
                                         () => ImportModListBackup(false),
                                         LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                         true
                                     ) {
                                         absorbInputAroundWindow = true,
                                         closeOnEscapeKey = true,
                                         doCloseX = true
                                     });

#if DEBUG
            if (list.ButtonTextLabeled("Debug", "ListExisting")) {
                foreach (var modSet in Sets) {
                    Log.Message($"ModSet '{modSet.Name}': {modSet}");
                }
            }
#endif

            const float scrollbarSize = 16f;
            const float lineHeight = 30f;
            const float gapSize = 4f;

            var r = list.GetRect(rect.height - list.CurHeight);

            int count = Sets.Count;

            Widgets.BeginScrollView(r, ref _scrollPosition, new Rect(0, 0, r.width - scrollbarSize, count*(lineHeight + gapSize)));
            Vector2 position = new Vector2();

            int reorderableGroup = ReorderableWidget.NewGroup((@from, to) => {
                                                                  ReorderModSet(@from, to);
                                                                  SoundDefOf.TickHigh.PlayOneShotOnCamera(null);
                                                              });

            // render each row
            foreach (var entry in Sets) {
                position.y = position.y + gapSize;

                Rect line = new Rect(0, position.y, r.width - scrollbarSize, lineHeight);

                entry.DoWindowContents(line, reorderableGroup);
                position.y += lineHeight;
            }

            Widgets.EndScrollView();

            list.End();
        }

        private void ReorderModSet(int @from, int to) {
            if (@from == to) {
                return;
            }

            var item = Sets[@from];
            Sets.RemoveAt(@from);
            Sets.Insert(to, item);
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
                    try {
                        var set = new ModSet(this) {
                                      Name = $"MLB '{Path.GetFileNameWithoutExtension(mlbSet)}'",
                                      // ReSharper disable PossibleNullReferenceException
                                      // ReSharper disable AssignNullToNotNullAttribute
                                      Mods = doc.DocumentElement.SelectNodes(@"//activeMods/li/text()").Cast<XmlNode>().Select(n => n.Value).ToList(),
                                      BuildNumber = Int32.Parse(doc.DocumentElement.SelectSingleNode(@"//buildNumber/text()").Value, CultureInfo.InvariantCulture)
                                      // ReSharper restore AssignNullToNotNullAttribute
                                      // ReSharper restore PossibleNullReferenceException
                                  };
                        Util.Trace($"Imported {set.Name}: {set}");
                        Sets.Add(set);
                    }
                    catch (Exception e) {
                        Util.Error(e);
                    }
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
                        if (!_lookup.TryGetValue(key, out attr)) _lookup[key] = attr = new ModAttributes { Key = key };
                        var textColor = doc.DocumentElement.SelectSingleNode(@"//textColor");
                        try {
                            var mlb = new MLBAttributes {
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
                                      };
                            attr.attributes.Add(mlb);
                            attr.Color = mlb.color;
                        }
                        catch (Exception e) {
                            Util.Error(e);
                        }
                    }
                }

                Attributes.AddRange(_lookup.Values);
            }

            Mod.WriteSettings();
        }


        public void DoModsConfigWindowContents(Rect target) {
            target.x += 30f;

            var rctApply = new Rect(target.x, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctApply, Assets.Apply, false, TipApply, rctApply.ContractedBy(4)))
                if (Sets.Count != 0)
                    Find.WindowStack.Add(new FloatMenu(Sets.Select(ms => new FloatMenuOption(ms.Name, () => {
                                                                                                          _undo = ModSet.FromCurrent("undo", this);
                                                                                                          ms.Apply();
                                                                                                      })).ToList()));
            var rctNew = new Rect(target.x + 30f + 8f, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctNew, Assets.Extract, false, TipCreateNew, rctNew.ContractedBy(4))) {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                                                                                 new FloatMenuOption(
                                                                                     LanguageKeys.keyed.ModSwitch_CreateNew.Translate(),
                                                                                     () => Find.WindowStack.Add(
                                                                                         new Dialog_SetText(
                                                                                             s => {
                                                                                                 Sets.Add(ModSet.FromCurrent(s, this));
                                                                                                 Mod.WriteSettings();
                                                                                             },
                                                                                             LanguageKeys.keyed.ModSwitch_Create_DefaultName.Translate()
                                                                                         ))),
                                                                                 new FloatMenuOption(
                                                                                     LanguageKeys.keyed.ModSwitch_OverwritExisting.Translate(),
                                                                                     () => Find.WindowStack.Add(
                                                                                         new FloatMenu(Sets.Select(
                                                                                                           ms => new FloatMenuOption(
                                                                                                               ms.Name,
                                                                                                               () => {
                                                                                                                   if (Input.GetKey(KeyCode.LeftShift) ||
                                                                                                                       Input.GetKey(KeyCode.RightShift)
                                                                                                                   ) {
                                                                                                                       OverwriteMod(ms);
                                                                                                                   }
                                                                                                                   else {
                                                                                                                       Find.WindowStack.Add(
                                                                                                                           Dialog_MessageBox.CreateConfirmation(
                                                                                                                               LanguageKeys
                                                                                                                                   .keyed.ModSwitch_OverwritExisting_Confirm
                                                                                                                                   .Translate(ms.Name),
                                                                                                                               () => OverwriteMod(ms),
                                                                                                                               true,
                                                                                                                               LanguageKeys.keyed.ModSwitch_Confirmation_Title
                                                                                                                                           .Translate()
                                                                                                                           ));
                                                                                                                   }
                                                                                                               })

                                                                                                       ).ToList()))
                                                                                 )
                                                                             }));
            }
            var rctUndo = new Rect(target.x + 2 * (30f + 8f), target.y, 30f, 30f);
            if (_undo != null)
                if (ExtraWidgets.ButtonImage(rctUndo, Assets.Undo, false, TipUndo, rctUndo.ContractedBy(4))) {
                    _undo.Apply();
                    _undo = null;
                }

            var rctSettings = new Rect(350f - 30f, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctSettings, Assets.Settings, false, TipSettings, rctSettings.ContractedBy(4))) {
                var settings = new Dialog_ModSettings();
                AccessTools.Field(typeof(Dialog_ModSettings), @"selMod").SetValue(settings, Mod);
                Find.WindowStack.Add(settings);
            }
        }

        private void OverwriteMod(ModSet ms) {
            var idx = Sets.IndexOf(ms);
            Sets[idx] = ModSet.FromCurrent(ms.Name, this);
            Mod.WriteSettings();
        }
    }
}
