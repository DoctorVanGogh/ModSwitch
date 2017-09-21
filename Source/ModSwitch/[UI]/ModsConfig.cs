using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Harmony;
using Steamworks;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    public static class ModsConfig {
        public static MethodInfo miCheckboxLabeledSelectable = AccessTools.Method(typeof(Widgets), nameof(Widgets.CheckboxLabeledSelectable));
        public static MethodInfo miGuiSetContentColor = AccessTools.Property(typeof(GUI), nameof(GUI.color)).GetSetMethod(true);
        private static MethodInfo miGetModWithIdentifier = AccessTools.Method(typeof(ModLister), "GetModWithIdentifier");

        private static IDictionary<string, Color> _colorMap;


        public static void DoContextMenu(ModMetaData mod) {
            var options = new List<FloatMenuOption>();

            if (mod.OnSteamWorkshop) {
                if (SteamAPI.IsSteamRunning())
                    options.Add(
                        new FloatMenuOption(
                            LanguageKeys.keyed.ModSwitch_CopyLocal.Translate(),
                            () => {
                                Find.WindowStack.Add(new Dialog_SetText(
                                                         name => {
                                                             CopyModLocal(mod, name);
                                                             UpdateSteamAttributes(name, mod);
                                                         },
                                                         $"{mod.Name}",
                                                         name => {
                                                             var targetDirectory = Path.Combine(GenFilePaths.CoreModsFolderPath, name);
                                                             if (Path.GetInvalidPathChars().Any(name.Contains)) {
                                                                 return LanguageKeys.keyed.ModSwitch_Error_InvalidChars.Translate();
                                                             }
                                                             if (Directory.Exists(targetDirectory))
                                                                 return LanguageKeys.keyed.ModSwitch_Error_TargetExists.Translate();
                                                             return null;
                                                         }
                                                     ));
                            }));
                else {
                    options.Add(
                        new FloatMenuOption(
                            $"{LanguageKeys.keyed.ModSwitch_CopyLocal.Translate()}: *{LanguageKeys.keyed.ModSwitch_Error_SteamNotRunning.Translate()}*",
                            null));
                }
            } else {
                options.Add(new FloatMenuOption(
                                "Open folder",
                                () => Process.Start(mod.RootDir.FullName)
                            ));
                var ms = LoadedModManager.GetMod<ModSwitch>();
                var localAttributes = ms[mod];
                var tsCopy = localAttributes.SteamOriginTS;
                var tsSteam = ms[localAttributes.SteamOrigin].LastUpdateTS;
                if (localAttributes.SteamOrigin != null && tsCopy != null) {
                    var text = new StringBuilder("Update from Steam Original");

                    var option = new FloatMenuOption();
                    if (tsCopy == tsSteam) {
                        option.Label = text.Append(" *identical*").ToString();
                    } else if (tsSteam == null) {
                        option.Label = text.Append(" *last steam update unknown*").ToString();
                    } else {
                        option.Label = text.ToString();
                        option.action = () => {
                                            Find.WindowStack.Add(
                                                new Dialog_MessageBox(
                                                    $"The mod {mod.Identifier} will be updated to the current Steam version.\r\n" + 
                                                    $"  (Last) copied from Steam at an upload date of {Util.UnixTimeStampToDateTime(tsCopy.Value):g}\r\n" +
                                                    $"  Current upload date on Steam: {Util.UnixTimeStampToDateTime(tsSteam.Value):g}\r\n" +
                                                    "\r\n" +
                                                    "Do you want to keep your existing settings from the local copy or replace them with the version of Steam?",
                                                    "Keep existing",
                                                    () => {
                                                        ModMetaData mdOriginal = GetMetadata(localAttributes.SteamOrigin);
                                                        CopyModLocal(mdOriginal, mod.Identifier, false, true);
                                                        UpdateSteamAttributes(mod.Identifier, mdOriginal);
                                                    },
                                                    "Replace with Steam",
                                                    () => {
                                                        ModMetaData mdOriginal = GetMetadata(localAttributes.SteamOrigin);
                                                        CopyModLocal(mdOriginal, mod.Identifier, true, true);
                                                        UpdateSteamAttributes(mod.Identifier, mdOriginal);
                                                    },
                                                    LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                                    false) {
                                                               doCloseX = true,
                                                               closeOnEscapeKey = true
                                                           });
                                            // TODO: do we actually want to *empty* the existing directory before we copy over the new version? might have old artefacts otherwise
                                        };
                    }
                    options.Add(option);
                }

            }

            /*options.Add(new FloatMenuOption(
                                LanguageKeys.keyed.ModSwitch_MoveTo.Translate(),
                                () => {
                                    Find.WindowStack.Add(
                                        new FloatMenu(new List<FloatMenuOption> {
                                                                                    new FloatMenuOption(
                                                                                        LanguageKeys.keyed.ModSwitch_MoveTo_Top.Translate(),
                                                                                        () => {
                                                                                            LoadedModManager.GetMod<ModSwitch>().MovePosition(mod, Position.Top);
                                                                                        }),
                                                                                    new FloatMenuOption(
                                                                                        LanguageKeys.keyed.ModSwitch_MoveTo_Bottom.Translate(),
                                                                                        () => {
                                                                                            LoadedModManager.GetMod<ModSwitch>().MovePosition(mod, Position.Bottom);
                                                                                        })
                                                                                }));
                                }
                            ));*/

            options.Add(
                new FloatMenuOption(
                    LanguageKeys.keyed.ModSwitch_Color.Translate(),
                    () => {
                        Find.WindowStack.Add(new FloatMenu(CreateColorizationOptions(mod)));
                    }));


            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static ModMetaData GetMetadata(string identifier) {
            return (ModMetaData) miGetModWithIdentifier.Invoke(null, new[] {identifier});
        }

        private static void CopyModLocal(ModMetaData mod, string name, bool? forceCopySettings = null, bool deleteExisting = false) {
            var targetDirectory = Path.Combine(GenFilePaths.CoreModsFolderPath, name);

            if (deleteExisting && Directory.Exists(targetDirectory)) {
                Directory.Delete(targetDirectory, true);
                Directory.CreateDirectory(targetDirectory);
            }

            // copy mod
            Util.DirectoryCopy(mod.RootDir.FullName, targetDirectory, true);
            StringBuilder log = new StringBuilder();
            log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Copy.Translate(mod.Name, targetDirectory));
            log.AppendLine();

            // copy mod settings
            var settings = Directory.GetFiles(GenFilePaths.ConfigFolderPath);
            var pattern = $@"^Mod_{mod.Identifier}_([^\.]+).xml$";
            Util.Trace(pattern);
            var rgxSettings = new Regex(pattern);
            var matching = settings
                .Select(s => rgxSettings.Match(Path.GetFileName(s)))
                .Where(m => m.Success)
                .Select(m => new {
                                     source = Path.Combine(GenFilePaths.ConfigFolderPath, m.Value),
                                     destination = Path.Combine(GenFilePaths.ConfigFolderPath,
                                                                String.Format(
                                                                    "Mod_{0}_{1}.xml",
                                                                    name,
                                                                    m.Groups[1].Value))
                                 }).ToArray();

            Action<bool> copySettings = b => {
                                            foreach (var element in matching) {
                                                File.Copy(element.source, element.destination, b);
                                            }
                                            log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Settings.Translate(matching.Length));
                                        };

            switch (forceCopySettings) {
                case true:
                    copySettings(true);
                    break;
                case null:
                    if (matching.Any(t => File.Exists(t.destination))) {
                        Find.WindowStack.Add(
                            new Dialog_MessageBox(
                                LanguageKeys.keyed.ModSwitch_ExistingSettings.Translate(name),
                                LanguageKeys.keyed.ModSwitch_ExistingSettings_Choice_Overwrite.Translate(),
                                () => copySettings(true),
                                LanguageKeys.keyed.ModSwitch_ExistingSettings_Choice_Skip.Translate(),
                                () => {
                                    log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Skipped.Translate());
                                },
                                LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                true));
                    }
                    else {
                        copySettings(false);
                    }
                    break;
                case false:
                    log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Skipped.Translate());                   
                    break;
            }

            Find.WindowStack.Add(new Dialog_MessageBox(log.ToString()) {
                                                                          title = LanguageKeys.keyed.ModSwitch_CopyLocal.Translate()
                                                                      });
        }

        internal static void UpdateSteamTS(PublishedFileId_t pfid, UInt32 ts) {
            LoadedModManager.GetMod<ModSwitch>()[pfid.ToString()].LastUpdateTS = ts;
        }

        private static void UpdateSteamAttributes(string name, ModMetaData original) {
            if (!original.OnSteamWorkshop)
                throw new ArgumentException();

            ModSwitch ms = LoadedModManager.GetMod<ModSwitch>();
            ModAttributes attributes = ms[name];
            attributes.SteamOrigin = original.Identifier;
            attributes.SteamOriginTS = ms[original].LastUpdateTS;
        }

        private static List<FloatMenuOption> CreateColorizationOptions(ModMetaData mod) {
            return Enumerable.Select<KeyValuePair<string, Color>, FloatMenuOption>(ColorMap, kvp => new FloatMenuOption(
                    $"{kvp.Key.Colorize(kvp.Value)} ({kvp.Key})",
                    () => LoadedModManager.GetMod<ModSwitch>()[mod].Color = kvp.Value)
            ).ToList();
        }

        public static IDictionary<string, Color> ColorMap => _colorMap ?? (_colorMap = new Dictionary<string, Color> {
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_white.Translate(), Color.white},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_black.Translate(), Color.black},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_gray.Translate(), Color.gray},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_red.Translate(), Color.red},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_green.Translate(), Color.green},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_blue.Translate(), Color.blue},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_magenta.Translate(), Color.magenta},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_cyan.Translate(), Color.cyan},
                                                                                                                         {LanguageKeys.keyed.ModSwitch_Color_yellow.Translate(), Color.yellow}
                                                                                                                     });

        public static Color SetGUIColorMod(ModMetaData mod) {
            var current = GUI.contentColor;

            GUI.color = LoadedModManager.GetMod<ModSwitch>()[mod].Color ?? Color.white;

            return current;
        }
    }
}
