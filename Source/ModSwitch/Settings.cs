using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    class Settings : ModSettings{
        private List<ModSet> Sets = new List<ModSet>();

        private Vector2 _scrollPosition = new Vector2();


        public override void ExposeData() {
            Scribe_Collections.Look(ref Sets, false, "sets");
        }

        public void DoWindowContents(Rect rect) {
            Listing_Standard list = new Listing_Standard(GameFont.Small) {
                ColumnWidth = rect.width
            };
            list.Begin(rect);

            if (list.ButtonTextLabeled("Import", "ModListBackup")) {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Importing from ModListBackup will overwrite all existing sets.", ImportModListBackupo, true, "Confirm import"));
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

            Widgets.BeginScrollView(r, ref _scrollPosition, new Rect(0, 0, r.width - scrollbarSize, count * (sliderHeight + gapSize)));
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

        private void ImportModListBackupo() {
            Sets.Clear();

            string parent = Path.Combine(GenFilePaths.SaveDataFolderPath, "ModListBackup");
            Util.Trace($"Looking at {parent}");
            if (Directory.Exists(parent)) {
                var existing = Directory.GetFiles(parent, "*.rws");

                foreach (var mlbSet in existing) {
                    Util.Trace($"Reading {mlbSet}");

                    XmlDocument doc = new XmlDocument();
                    doc.Load(mlbSet);

                    var set = new ModSet {
                                  Name = $"MLB '{Path.GetFileNameWithoutExtension(mlbSet)}'",
                                  Mods = doc.DocumentElement.SelectNodes(@"//activeMods/li/text()").Cast<XmlNode>().Select(n => n.Value).ToList()
                              };
                    Util.Trace($"Imported {set.Name}: {set}");
                    Sets.Add(set);
                }
            }

            LoadedModManager.GetMod<ModSwitch>().WriteSettings();
        }



        public void DoModsConfigWindowContents(Rect target) {
            if (Widgets.ButtonText(new Rect(target.x, target.y, 120f, 30f), "[MS] Load set")) {
                Find.WindowStack.Add(new FloatMenu(Sets.Select(ms => new FloatMenuOption(ms.Name, ms.Apply)).ToList()));
            }
        }
    }
}
