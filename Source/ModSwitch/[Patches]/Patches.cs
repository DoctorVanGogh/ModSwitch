using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using Harmony;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "HarmonyPatch container")]
    class Patches {
        [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
        public class ModsConfig_DoWindowContents {
            public static void Postfix(Rect rect) {
                const float bottomHeight = 52f;
                LoadedModManager.GetMod<ModSwitch>()?.DoModsConfigWindowContents(new Rect(0, rect.height - bottomHeight + 8f, 350f, bottomHeight - 8f));
            }
        }

        [HarmonyPatch(typeof(Page_ModsConfig), "DoModRow", new Type[] { typeof(Listing_Standard), typeof(ModMetaData), typeof(int), typeof(int) })]
        public class Page_ModsConfig_DoModRow {

            public static MethodInfo miCheckboxLabeledSelectable = AccessTools.Method(typeof(Widgets), nameof(Widgets.CheckboxLabeledSelectable));
            public static MethodInfo miGuiSetContentColor = AccessTools.Property(typeof(GUI), nameof(GUI.color)).GetSetMethod(true);

            private static IDictionary<string, Color> _colorMap;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGen) {
                var instr = new List<CodeInstruction>(instructions);

                int idxCheckboxLabeledSelectable = instr.FirstIndexOf(ci => ci.opcode == OpCodes.Call && ci.operand == miCheckboxLabeledSelectable);
                if (idxCheckboxLabeledSelectable == -1) {
                    Util.Warning("Could not find anchor for ModRow transpiler - not modifying code");
                    return instr;
                }
                Label lblBlockEnd = (Label) instr[idxCheckboxLabeledSelectable + 1].operand;
                int idxBlockEnd = instr.FindIndex(idxCheckboxLabeledSelectable + 1, ci => ci.labels.Contains(lblBlockEnd));
                if (idxBlockEnd == -1) {
                    Util.Warning("Could not find end Label for ModRow transpiler change - not modifying code");
                    return instr;
                }

                /*  
                 *  Turn
                 *  <code>
                 * 		if (Widgets.CheckboxLabeledSelectable(rect2, label, ref flag, ref active)) {
			     *          ...
                 *      }
                 *  <code>
                 *  into
                 *  <code>
                 *      Color color = Page_ModsConfig_DoModRow.SetGUIColorMod(mod); 
                 * 		if (Widgets.CheckboxLabeledSelectable(rect2, label, ref flag, ref active)) {
                 * 		    GUI.contentColor = color;
                 * 		    if (Input.GetMouseButtonUp(1)) {
                 *              // do right click stuff
                 * 		    } else {
			     *              ...
                 *          }
                 *      } else {
                 *          GUI.contentColor = color;
                 *      }
                 *      
                 *  <code>
                 */

                LocalBuilder localColor = ilGen.DeclareLocal(typeof(Color));
                Label lblExistingClickCode = ilGen.DefineLabel();
                Label lblNoClick = ilGen.DefineLabel();

                // insert changes in REVERSE order to preserve indices


                // insert <code>else { GUI.contentColor = color; }</code>
                instr.InsertRange(idxBlockEnd, new[] {
                                                   new CodeInstruction(OpCodes.Ldloc, localColor) {labels = new List<Label> {lblNoClick}},
                                                   new CodeInstruction(OpCodes.Call, miGuiSetContentColor),
                                               });

                // setup <code>else { ... }</code> branch label
                instr[idxCheckboxLabeledSelectable + 2].labels.Add(lblExistingClickCode);

                // insert <code>GUI.contentColor = color; if (Input.GetMouseButtonUp(1)) { DoContextMenu(mod); }</code>
                instr.InsertRange(idxCheckboxLabeledSelectable + 2, new[] {
                                                                        new CodeInstruction(OpCodes.Ldloc, localColor),
                                                                        new CodeInstruction(OpCodes.Call, miGuiSetContentColor),
                                                                        new CodeInstruction(OpCodes.Ldc_I4_1),
                                                                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp))),
                                                                        new CodeInstruction(OpCodes.Brfalse, lblExistingClickCode),
                                                                        new CodeInstruction(OpCodes.Ldarg_2),
                                                                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Page_ModsConfig_DoModRow), nameof(DoContextMenu))),
                                                                        new CodeInstruction(OpCodes.Br, lblBlockEnd),
                                                                    });

                // setup modified jump
                instr[idxCheckboxLabeledSelectable + 1].operand = lblNoClick;

                // insert <code>Color color = Page_ModsConfig_DoModRow.SetGUIColorMod(mod);</code>
                instr.InsertRange(idxCheckboxLabeledSelectable - 4, new[] {
                                                                        new CodeInstruction(OpCodes.Ldarg_2),
                                                                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Page_ModsConfig_DoModRow), nameof(SetGUIColorMod))),
                                                                        new CodeInstruction(OpCodes.Stloc, localColor),
                                                                    });


                return instr;
            }

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
                                                                 var targetDirectory = Path.Combine(GenFilePaths.CoreModsFolderPath, name);

                                                                 // copy mod
                                                                 Util.DirectoryCopy(mod.RootDir.FullName, targetDirectory, true);
                                                                 StringBuilder sb = new StringBuilder();
                                                                 sb.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Copy.Translate(mod.Name, targetDirectory));
                                                                 sb.AppendLine();

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
                                                                                                                     string.Format(
                                                                                                                         "Mod_{0}_{1}.xml",
                                                                                                                         name,
                                                                                                                         m.Groups[1].Value))
                                                                                      }).ToArray();

                                                                 Action<bool> copySettings = b => {
                                                                                                 foreach (var element in matching) {
                                                                                                     File.Copy(element.source, element.destination, b);
                                                                                                 }
                                                                                                 sb.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Settings.Translate(matching.Length));

                                                                                                 Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()) {
                                                                                                                                                               title = LanguageKeys
                                                                                                                                                                   .keyed.ModSwitch_CopyLocal
                                                                                                                                                                   .Translate()
                                                                                                                                                           });
                                                                                             };

                                                                 if (matching.Any(t => File.Exists(t.destination))) {
                                                                     Find.WindowStack.Add(
                                                                         new Dialog_MessageBox(
                                                                             LanguageKeys.keyed.ModSwitch_ExistingSettings.Translate(name),
                                                                             LanguageKeys.keyed.ModSwitch_ExistingSettings_Choice_Overwrite.Translate(),
                                                                             () => copySettings(true),
                                                                             LanguageKeys.keyed.ModSwitch_ExistingSettings_Choice_Skip.Translate(),
                                                                             () => {
                                                                                 sb.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Skipped.Translate());
                                                                             },
                                                                             LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                                                             true));
                                                                 }
                                                                 else {
                                                                     copySettings(false);
                                                                 }
                                                             },
                                                             $"{mod.Name}",
                                                             name => {
                                                                 var targetDirectory = Path.Combine(GenFilePaths.CoreModsFolderPath, name);
                                                                 if (Path.GetInvalidPathChars().Any(name.Contains)) {
                                                                     return LanguageKeys.keyed.ModSwitch_Error_InvalidChars.Translate();
                                                                 }
                                                                 if (Directory.Exists(targetDirectory))
                                                                     return LanguageKeys.keyed.ModSwitch_Error_TargetExists.Translate();
                                                                 
                                                                 // walk target path up to root, check we are under 'CoreModsFolderPath' - no '..\..\' shenanigans to break out of mods jail
                                                                 var modsRoot = new DirectoryInfo(GenFilePaths.CoreModsFolderPath);
                                                                 for (DirectoryInfo current = new DirectoryInfo(targetDirectory);
                                                                      current?.FullName != current?.Root.FullName;
                                                                      current = current.Parent) {
                                                                     if (current.FullName == modsRoot.FullName)
                                                                         return null;
                                                                 }

                                                                 return LanguageKeys.keyed.ModSwitch_Error_NotValid.Translate();                                                                 
                                                             }
                                                         ));
                                }));
                    else {
                        options.Add(
                            new FloatMenuOption(
                                $"{LanguageKeys.keyed.ModSwitch_CopyLocal.Translate()}: *{LanguageKeys.keyed.ModSwitch_Error_SteamNotRunning.Translate()}*",
                                null));
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

            private static List<FloatMenuOption> CreateColorizationOptions(ModMetaData mod) {
                return ColorMap.Select(
                    kvp => new FloatMenuOption(
                        $"{kvp.Key.Colorize(kvp.Value)} ({kvp.Key})",
                        () => LoadedModManager.GetMod<ModSwitch>()
                                              .SetModColor(mod, kvp.Value)
                    )
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

                GUI.color = LoadedModManager.GetMod<ModSwitch>().GetModColor(mod);

                return current;
            }
        }
    }
}