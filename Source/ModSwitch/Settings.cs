using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DoctorVanGogh.ModSwitch {
    internal class Settings : ModSettings {
        private static TipSignal? _tipCreateNew;
        private static TipSignal? _tipSettings;
        private static TipSignal? _tipApply;
        private static TipSignal? _tipUndo;

        private Vector2 _scrollPosition;

        private ModSet _undo;

        public ModAttributesSet Attributes = new ModAttributesSet();
        public List<ModSet> Sets = new List<ModSet>();

        public static TipSignal TipSettings => (_tipSettings ?? (_tipSettings = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Settings.Translate()))).Value;

        public static TipSignal TipCreateNew => (_tipCreateNew ?? (_tipCreateNew = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Create.Translate()))).Value;

        public static TipSignal TipApply => (_tipApply ?? (_tipApply = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Apply.Translate()))).Value;

        public static TipSignal TipUndo => (_tipUndo ?? (_tipUndo = new TipSignal(LanguageKeys.keyed.ModSwitch_Tip_Undo.Translate()))).Value;


        public void DoModsConfigWindowContents(Rect target) {
            target.x += 30f;

            Rect rctApply = new Rect(target.x, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctApply, Assets.Apply, false, TipApply, rctApply.ContractedBy(4)))
                if (Sets.Count != 0)
                    Find.WindowStack.Add(
                        new FloatMenu(
                            Sets.Select(
                                    ms => new FloatMenuOption(
                                        ms.Name,
                                        () => {
                                            _undo = ModSet.FromCurrent("undo", this);
                                            ms.Apply();
                                        }))
                                .ToList()));
            Rect rctNew = new Rect(target.x + 30f + 8f, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctNew, Assets.Extract, false, TipCreateNew, rctNew.ContractedBy(4)))
                Find.WindowStack.Add(
                    new FloatMenu(
                        new List<FloatMenuOption> {
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
                                                          () => {
                                                              if (Sets.Count > 0)
                                                                  Find.WindowStack.Add(
                                                                      new FloatMenu(
                                                                          Sets.Select(
                                                                                  ms => new FloatMenuOption(
                                                                                      ms.Name,
                                                                                      () => {
                                                                                          if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                                                                                          )
                                                                                              OverwriteMod(ms);
                                                                                          else
                                                                                              Find.WindowStack.Add(
                                                                                                  Dialog_MessageBox.CreateConfirmation(
                                                                                                      LanguageKeys.keyed.ModSwitch_OverwritExisting_Confirm
                                                                                                                  .Translate(ms.Name),
                                                                                                      () => OverwriteMod(ms),
                                                                                                      true,
                                                                                                      LanguageKeys.keyed.ModSwitch_Confirmation_Title
                                                                                                                  .Translate()
                                                                                                  ));
                                                                                      })
                                                                              )
                                                                              .ToList()));
                                                          })
                                                  }));
            Rect rctUndo = new Rect(target.x + 2 * (30f + 8f), target.y, 30f, 30f);
            if (_undo != null)
                if (ExtraWidgets.ButtonImage(rctUndo, Assets.Undo, false, TipUndo, rctUndo.ContractedBy(4))) {
                    _undo.Apply();
                    _undo = null;
                }

            Rect rctSettings = new Rect(350f - 30f, target.y, 30f, 30f);
            if (ExtraWidgets.ButtonImage(rctSettings, Assets.Settings, false, TipSettings, rctSettings.ContractedBy(4)))
                Find.WindowStack.Add(new Dialog_ModsSettings_Custom(Mod));
        }

        public void DoWindowContents(Rect rect) {
            Listing_Standard list = new Listing_Standard(GameFont.Small) {
                                                                             ColumnWidth = rect.width
                                                                         };
            list.Begin(rect);

            Rect left = list.GetRect(30f).LeftHalf();

            if (Widgets.ButtonText(left, LanguageKeys.keyed.ModSwitch_Import.Translate(), true, false, true))
                Find.WindowStack.Add(
                    new FloatMenu(
                        new List<FloatMenuOption> {
                                                      new FloatMenuOption(
                                                          LanguageKeys.keyed.ModSwitch_Import_FromFile.Translate(),
                                                          () => {
                                                              var options = MS_GenFilePaths.AllExports
                                                                                           .Select(
                                                                                               fi => new FloatMenuOption(
                                                                                                   fi.Name,
                                                                                                   () => {
                                                                                                       try {
                                                                                                           ImportFromExport(fi);
                                                                                                       } catch (Exception e) {
                                                                                                           Util.DisplayError(e);
                                                                                                       }
                                                                                                   }))
                                                                                           .ToList();

                                                              if (options.Count != 0)
                                                                  Find.WindowStack.Add(new FloatMenu(options));
                                                          }),
                                                      new FloatMenuOption(
                                                          LanguageKeys.keyed.ModSwitch_Import_Savegame.Translate(),
                                                          () => Find.WindowStack.Add(
                                                              new FloatMenu(
                                                                  GenFilePaths.AllSavedGameFiles
                                                                              .Select(fi => new FloatMenuOption(fi.Name, () => ImportFromSave(fi)))
                                                                              .ToList())
                                                          )),
                                                      new FloatMenuOption(
                                                          @"ModListBackup",
                                                          () => Find.WindowStack.Add(
                                                              new Dialog_MessageBox(
                                                                  LanguageKeys.keyed.ModSwitch_Import_Text.Translate(),
                                                                  LanguageKeys.keyed.ModSwitch_Import_Choice_Replace.Translate(),
                                                                  () => ImportModListBackup(true),
                                                                  LanguageKeys.keyed.ModSwitch_Import_Choice_Append.Translate(),
                                                                  () => ImportModListBackup(false),
                                                                  LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                                                  true
                                                              ) {
                                                                    absorbInputAroundWindow = true,
                                                                    closeOnClickedOutside = true,
                                                                    doCloseX = true
                                                                }))
                                                  }));


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

            Rect r = list.GetRect(rect.height - list.CurHeight);

            int count = Sets.Count;

            Widgets.BeginScrollView(r, ref _scrollPosition, new Rect(0, 0, r.width - scrollbarSize, count * (lineHeight + gapSize)));
            Vector2 position = new Vector2();

            int reorderableGroup = ReorderableWidget.NewGroup(
                (from, to) => {
                    ReorderModSet(from, to);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                },
               ReorderableDirection.Vertical );

            // render each row
            foreach (ModSet entry in Sets) {
                position.y = position.y + gapSize;

                Rect line = new Rect(0, position.y, r.width - scrollbarSize, lineHeight);

                entry.DoWindowContents(line, reorderableGroup);
                position.y += lineHeight;
            }

            Widgets.EndScrollView();

            list.End();
        }

        public override void ExposeData() {
            Scribe_Collections.Look(ref Sets, false, @"sets", LookMode.Undefined, this);
            Scribe_Custom.Look<ModAttributesSet, ModAttributes>(ref Attributes, false, @"attributes");
        }

        private void ImportFromSave(FileInfo fi) {
            Scribe.loader.InitLoadingMetaHeaderOnly(fi.FullName);
            try {
                ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
                Scribe.loader.FinalizeLoading();

                int suffix = 0;
                string name = fi.Name;
                while (Sets.Any(ms => ms.Name == name))
                    name = $"{fi.Name}_{++suffix}";
                Sets.Add(
                    new ModSet(this) {
                                         Name = name,
                                         BuildNumber = new Version(VersionControl.VersionStringWithoutRev(ScribeMetaHeaderUtility.loadedGameVersion)).Build,
                                         Mods = new List<string>(ScribeMetaHeaderUtility.loadedModIdsList)
                                     });
                Mod.WriteSettings();
            } catch (Exception ex) {
                Log.Warning(string.Concat("Exception loading ", fi.FullName, ": ", ex));
                Scribe.ForceStop();

            }
        }
        
        private void ImportFromExport(FileInfo fi){
            if (File.Exists(fi.FullName)) {
                ModSet imported = null;

                Scribe.loader.InitLoading(fi.FullName);
                try {
                    Scribe_Deep.Look(ref imported, ModSet.Export_ElementName, this);
                } finally {
                    Scribe.loader.FinalizeLoading();
                }
                if (imported == null)
                    throw new InvalidOperationException("Error importing ModSet...");

                int suffix = 0;
                string name = imported.Name;
                while (Sets.Any(ms => ms.Name == name))
                    name = $"{imported.Name}_{++suffix}";
                imported.Name = name;
                Sets.Add(imported);

                Mod.WriteSettings();
            } else {
                throw new FileNotFoundException();
            }
        }


        private void ImportModListBackup(bool overwrite = false) {
            if (overwrite) {
                Attributes.Clear();
                Sets.Clear();
            }

            string parent = Path.Combine(GenFilePaths.SaveDataFolderPath, "ModListBackup");
            Util.Trace($"Looking at {parent}");
            IDictionary<int, string> names = null;
            if (Directory.Exists(parent)) {
                // grab hugslibs settings for MLB
                string hugs = Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib");
                if (Directory.Exists(hugs)) {
                    string settings = Path.Combine(hugs, "ModSettings.xml");
                    if (File.Exists(settings)) {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(settings);

                        names = doc.DocumentElement.SelectSingleNode(@"//ModListBackup/StateNames/text()")?.Value.Split('|').Select((v, i) => new {v, i}).ToDictionary(t => t.i + 1, t => t.v);
                    }
                }

                // import configs
                string[] existing = Directory.GetFiles(parent, "*.rws");

                if (names == null)
                    names = new Dictionary<int, string>();


                foreach (string mlbSet in existing) {
                    Util.Trace($"Reading {mlbSet}");

                    XmlDocument doc = new XmlDocument();
                    doc.Load(mlbSet);
                    try {
                        string name = Path.GetFileNameWithoutExtension(mlbSet);
                        int idx;
                        string backupName = null;

                        if (int.TryParse(Path.GetFileNameWithoutExtension(name), out idx))
                            names.TryGetValue(idx, out backupName);
                        if (string.IsNullOrEmpty(backupName))
                            backupName = $"MLB '{name}'";

                        ModSet set = new ModSet(this) {
                                                          Name = backupName,
                                                          // ReSharper disable PossibleNullReferenceException
                                                          // ReSharper disable AssignNullToNotNullAttribute
                                                          Mods = doc.DocumentElement.SelectNodes(@"//activeMods/li/text()").Cast<XmlNode>().Select(n => n.Value).ToList(),
                                                          BuildNumber = int.Parse(doc.DocumentElement.SelectSingleNode(@"//buildNumber/text()").Value, CultureInfo.InvariantCulture)
                                                          // ReSharper restore AssignNullToNotNullAttribute
                                                          // ReSharper restore PossibleNullReferenceException
                                                      };
                        Util.Trace($"Imported {set.Name}: {set}");
                        Sets.Add(set);
                    } catch (Exception e) {
                        Util.Error(e);
                    }
                }

                Util.Trace($"Importing settings");

                // import custom settings
                string mods = Path.Combine(parent, "Mod");
                if (Directory.Exists(mods))
                    foreach (string mod in Directory.GetDirectories(mods)) {
                        Util.Trace($"Settings {mod}");
                        string settings = Path.Combine(mod, "Settings.xml");
                        if (File.Exists(settings)) {
                            XmlDocument doc = new XmlDocument();
                            doc.Load(settings);

                            ModAttributes attr;
                            string key = Path.GetFileName(mod);
                            if (!Attributes.TryGetValue(key, out attr))
                                Attributes.Add(attr = new ModAttributes {Key = key});
                            XmlNode textColor = doc.DocumentElement.SelectSingleNode(@"//textColor");
                            try {
                                MLBAttributes mlb = new MLBAttributes {
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
                            } catch (Exception e) {
                                Util.Error(e);
                            }
                        }
                    }
            }

            Mod.WriteSettings();
        }

        private void OverwriteMod(ModSet ms) {
            int idx = Sets.IndexOf(ms);
            Sets[idx] = ModSet.FromCurrent(ms.Name, this);
            Mod.WriteSettings();
        }

        private void ReorderModSet(int from, int to) {
            if (from == to)
                return;

            ModSet item = Sets[from];
            Sets.RemoveAt(from);
            Sets.Insert(to, item);
        }
    }
}