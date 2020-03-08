using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace DoctorVanGogh.ModSwitch {
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "HarmonyPatch container")]
    internal class Patches {
        public class ModsConfig_DoWindowContents {
            [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
            public class InjectSearchBox {
                public static Rect AllocateAndDrawSearchboxRect(Rect r) {
                    const float offset = ModsConfigUI.Search.buttonSize + 2 * ModsConfigUI.Search.buttonsInset;

                    ModsConfigUI.Search.DoSearchBlock(
                        new Rect(
                            r.x + ModsConfigUI.Search.buttonsInset,
                            r.y + ModsConfigUI.Search.buttonsInset,
                            r.width - 2 * ModsConfigUI.Search.buttonsInset,
                            ModsConfigUI.Search.buttonSize),
                        LanguageKeys.keyed.ModSwitch_Search_Watermark.Translate());

                    return new Rect(r.x, r.y + offset, r.width, r.height - offset);
                }

                // inject searchBox on top and shrink scroll list by required space
                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr) {
                    List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                    int idxAnchor = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Call && ci.operand == AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView)));
                    if (idxAnchor == -1) {
                        Util.Error("Could not find Page_ModsConfig.DoWindowContents transpiler anchor - not injecting code");
                        return instructions;
                    }

                    instructions.Insert(
                        idxAnchor - 4,
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InjectSearchBox), nameof(AllocateAndDrawSearchboxRect)))
                    );

                    return instructions;
                }
            }

            [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
            public class DrawOperationButtons {
                // draw bottom buttons
                public static void Postfix(Page_ModsConfig __instance, Rect rect) {
                    const float bottomHeight = 52f;
                    LoadedModManager.GetMod<ModSwitch>()?.DoModsConfigWindowContents(new Rect(0, rect.height - bottomHeight + 8f, 350f, bottomHeight - 8f), __instance);
                }
            }
        }

        [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.PreOpen))]
        public class Page_ModsConfig_PreOpen {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr) {
                List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                MethodInfo miTarget = AccessTools.Method(typeof(ModLister), @"RebuildModList");
                int idxAnchor = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Call && ci.operand == miTarget);
                if (idxAnchor == -1) {
                    Util.Warning("Could not find Page_ModsConfig.PreOpen transpiler anchor - not injecting code.");
                    return instructions;
                }
                instructions[idxAnchor].operand = AccessTools.Method(typeof(ModsConfigUI.Helpers), nameof(ModsConfigUI.Helpers.ForceSteamWorkshopRequery));
                return instructions;
            }
        }


        public class Page_ModsConfig_DoModRow {
            [HarmonyPatch(typeof(Page_ModsConfig), "DoModRow", new[] {typeof(Listing_Standard), typeof(ModMetaData), typeof(int), typeof(int)})]
            public class SupressNonMatchingFilteredRows {
                public static bool Prefix(ModMetaData mod) {
                    return ModsConfigUI.Search.MatchCriteria(mod?.Name) || true == mod?.SupportedVersionsReadOnly.Any(s => ModsConfigUI.Search.MatchCriteria(s.ToString()));
                }
            }

            [HarmonyPatch(typeof(Page_ModsConfig), "DoModRow", new[] {typeof(Listing_Standard), typeof(ModMetaData), typeof(int), typeof(int)})]
            public class InjectRightClickMenu {
                // inject right click menu on mod rows
                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGen) {
                    List<CodeInstruction> instr = new List<CodeInstruction>(instructions);

                    int idxCheckboxLabeledSelectable = instr.FirstIndexOf(ci => ci.opcode == OpCodes.Call && ci.operand == ModsConfigUI.miCheckboxLabeledSelectable);
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
                    instr.InsertRange(
                        idxBlockEnd,
                        new[] {
                                  new CodeInstruction(OpCodes.Ldloc, localColor) {labels = new List<Label> {lblNoClick}},
                                  new CodeInstruction(OpCodes.Call, ModsConfigUI.miGuiSetContentColor)
                              });

                    // setup <code>else { ... }</code> branch label
                    instr[idxCheckboxLabeledSelectable + 2].labels.Add(lblExistingClickCode);

                    // insert <code>GUI.contentColor = color; if (Input.GetMouseButtonUp(1)) { DoContextMenu(mod); }</code>
                    instr.InsertRange(
                        idxCheckboxLabeledSelectable + 2,
                        new[] {
                                  new CodeInstruction(OpCodes.Ldloc, localColor),
                                  new CodeInstruction(OpCodes.Call, ModsConfigUI.miGuiSetContentColor),
                                  new CodeInstruction(OpCodes.Ldc_I4_1),
                                  new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp))),
                                  new CodeInstruction(OpCodes.Brfalse, lblExistingClickCode),
                                  new CodeInstruction(OpCodes.Ldarg_2),
                                  new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModsConfigUI), nameof(ModsConfigUI.DoContextMenu))),
                                  new CodeInstruction(OpCodes.Br, lblBlockEnd)
                              });

                    // setup modified jump
                    instr[idxCheckboxLabeledSelectable + 1].operand = lblNoClick;

                    // insert <code>Color color = Page_ModsConfig_DoModRow.SetGUIColorMod(mod);</code>
                    instr.InsertRange(
                        idxCheckboxLabeledSelectable - 4,
                        new[] {
                                  new CodeInstruction(OpCodes.Ldarg_2),
                                  new CodeInstruction(
                                      OpCodes.Call,
                                      AccessTools.Method(typeof(ModsConfigUI.Helpers), nameof(ModsConfigUI.Helpers.SetGUIColorMod))),
                                  new CodeInstruction(OpCodes.Stloc, localColor)
                              });


                    return instr;
                }
            }

            [HarmonyPatch(typeof(Page_ModsConfig), "DoModRow", new[] {typeof(Listing_Standard), typeof(ModMetaData), typeof(int), typeof(int)})]
            public class InjectCustomContentSourceDraw {
                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr) {
                    List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                    MethodInfo miTarget = AccessTools.Method(typeof(ContentSourceUtility), nameof(ContentSourceUtility.DrawContentSource));

                    int idxAnchor = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Call && ci.operand == miTarget);
                    if (idxAnchor == -1) {
                        Util.Error("Could not find DrawContentSource transpiler anchor - not injecting code.");
                        return instructions;
                    }
                    /* replace
                     * 
                     * ContentSourceUtility.DrawContentSource(rect1, rowCAnonStorey428.mod.Source, clickAction);
                     * 
                     * with
                     * 
                     * ModsConfigUI.DrawContentSource(rect1, rowCAnonStorey428.mod.Source, clickAction, mod);
                     * 
                     */
                    instructions[idxAnchor].operand = AccessTools.Method(typeof(ModsConfigUI), nameof(ModsConfigUI.DrawContentSource));
                    instructions.Insert(idxAnchor, new CodeInstruction(OpCodes.Ldarg_2));

                    return instructions;
                }
            }
        }

        [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.PostClose))]
        public class Page_ModsConfig_PostClose {

            private static MethodInfo mi = typeof(ModsConfig).GetMethod(nameof(ModsConfig.RestartFromChangedMods));

            // redirect "auto-restart" to "maybe restart later?"
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
                foreach (CodeInstruction instruction in instructions) {
                    if (instruction.opcode == OpCodes.Call && instruction.operand == mi)
                        instruction.operand = typeof(ModsConfigUI).GetMethod(nameof(ModsConfigUI.OnModsChanged));

                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(ModsConfig), nameof(ModsConfig.Save))]
        public class ModsConfig_Save {
            public static void Postfix() {
                LoadedModManager.GetMod<ModSwitch>()?.WriteSettings();
            }
        }

        [HarmonyPatch(typeof(WorkshopItem), nameof(WorkshopItem.MakeFrom))]
        public class WorkshopItem_MakeFrom {
            // extract lastupdate timestamps from steam queries (vanilla just ignores values)
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr) {
                List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                ConstructorInfo ciTarget = AccessTools.Constructor(typeof(WorkshopItem_Mod));

                int idxAnchor = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Newobj && ci.operand == ciTarget);

                if (-1 == idxAnchor) {
                    Util.Warning("Could not find WorkshopItem.MakeFrom transpiler anchor - not injecting code");
                    return instructions;
                }

                /* Transform
                 * 
                 * 		if (workshopItem == null)
                 * 		{
                 * 			workshopItem = new WorkshopItem_Mod();
                 * 		}
                 * 
                 * into
                 * 
                 * 		if (workshopItem == null)
                 * 		{
                 * 		    ModsConfigUI.UpdateSteamTS(pfid, num2);
                 * 			workshopItem = new WorkshopItem_Mod();
                 * 		}
                 * 
                 */

                instructions.InsertRange(
                    idxAnchor,
                    new[] {
                              new CodeInstruction(OpCodes.Ldarg_0),
                              new CodeInstruction(OpCodes.Ldloc_2),
                              new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModsConfigUI.Helpers), nameof(ModsConfigUI.Helpers.UpdateSteamTS)))
                          }
                );

                return instructions;
            }
        }

        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
        public class MainMenuDrawer_DoMainMenuControls {
            private static readonly ConstructorInfo ciNewListableOption = AccessTools.Constructor(typeof(ListableOption), new[] {typeof(string), typeof(Action), typeof(string)});
            private static readonly MethodInfo miWrappedMenuOption = AccessTools.Method(typeof(ModsConfigUI), nameof(ModsConfigUI.WrapMainMenuOption));


            /// <summary>
            ///     Transforms the first sequence of
            ///     <pre>
            ///         ldstr       [anchor]
            ///         [dontcare]
            ///         ...
            ///         newobj      instance void Verse.ListableOption::.ctor(string, class [System.Core]System.Action, string)
            ///     </pre>
            ///     into
            ///     <pre>
            ///         ldstr       [anchor]
            ///         [dontcare]
            ///         ...
            ///         call        static Verse.ListableOption ModsConfigUI.WrapMainMenuOption(string, class [System.Core]System.Action, string)
            ///     </pre>
            /// </summary>
            private static bool InjectDeferedRestartHint(List<CodeInstruction> instructions, ILGenerator ilGen, string anchor) {
                int idxAnchor = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Ldstr && ci.operand as string == anchor);
                if (idxAnchor == -1) {
                    Util.Warning($"Could not find DoMainMenuControls {anchor} anchor - not injecting code");
                    return false;
                }

                int idxOption = instructions.FindIndex(idxAnchor, ci => ci.opcode == OpCodes.Newobj && ci.operand == ciNewListableOption);
                if (idxOption == -1) {
                    Util.Warning($"Could not find DoMainMenuControls {anchor} ListOption constructor - not injecting code");
                    return false;
                }

                instructions[idxOption].opcode = OpCodes.Call;
                instructions[idxOption].operand = miWrappedMenuOption;

                return true;
            }


            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr, ILGenerator ilGen) {
                List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                // yes, we are iterating the list three times... dont care
                if (!InjectDeferedRestartHint(instructions, ilGen, @"Tutorial"))
                    return instructions;
                if (!InjectDeferedRestartHint(instructions, ilGen, @"NewColony"))
                    return instructions;
                if (!InjectDeferedRestartHint(instructions, ilGen, @"LoadGame"))
                    return instructions;


                return instructions;
            }
        }


    }
}