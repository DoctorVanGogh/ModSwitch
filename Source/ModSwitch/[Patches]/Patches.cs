using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Verse.Steam;

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
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGen) {
                var instr = new List<CodeInstruction>(instructions);

                int idxCheckboxLabeledSelectable = instr.FirstIndexOf(ci => ci.opcode == OpCodes.Call && ci.operand == ModsConfig.miCheckboxLabeledSelectable);
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
                                                   new CodeInstruction(OpCodes.Call, ModsConfig.miGuiSetContentColor),
                                               });

                // setup <code>else { ... }</code> branch label
                instr[idxCheckboxLabeledSelectable + 2].labels.Add(lblExistingClickCode);

                // insert <code>GUI.contentColor = color; if (Input.GetMouseButtonUp(1)) { DoContextMenu(mod); }</code>
                instr.InsertRange(idxCheckboxLabeledSelectable + 2, new[] {
                                                                        new CodeInstruction(OpCodes.Ldloc, localColor),
                                                                        new CodeInstruction(OpCodes.Call, ModsConfig.miGuiSetContentColor),
                                                                        new CodeInstruction(OpCodes.Ldc_I4_1),
                                                                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp))),
                                                                        new CodeInstruction(OpCodes.Brfalse, lblExistingClickCode),
                                                                        new CodeInstruction(OpCodes.Ldarg_2),
                                                                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModsConfig), nameof(ModsConfig.DoContextMenu))),
                                                                        new CodeInstruction(OpCodes.Br, lblBlockEnd),
                                                                    });

                // setup modified jump
                instr[idxCheckboxLabeledSelectable + 1].operand = lblNoClick;

                // insert <code>Color color = Page_ModsConfig_DoModRow.SetGUIColorMod(mod);</code>
                instr.InsertRange(idxCheckboxLabeledSelectable - 4, new[] {
                                                                        new CodeInstruction(OpCodes.Ldarg_2),
                                                                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModsConfig), nameof(ModsConfig.SetGUIColorMod))),
                                                                        new CodeInstruction(OpCodes.Stloc, localColor),
                                                                    });


                return instr;
            }
        }



        [HarmonyPatch(typeof(WorkshopItem), nameof(WorkshopItem.MakeFrom))]
        public class WorkshopItem_MakeFrom {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr) {
                var instructions = new List<CodeInstruction>(instr);

                var ciTarget = AccessTools.Constructor(typeof(WorkshopItem_Mod));

                var idxAnchor = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Newobj && ci.operand == ciTarget);

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
                 * 		    ModsConfig.UpdateSteamTS(pfid, num2);
                 * 			workshopItem = new WorkshopItem_Mod();
                 * 		}
                 * 
                 */

                instructions.InsertRange(
                    idxAnchor,
                    new [] {
                               new CodeInstruction(OpCodes.Ldarg_0), 
                               new CodeInstruction(OpCodes.Ldloc_2), 
                               new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModsConfig), nameof(ModsConfig.UpdateSteamTS))), 
                           }
                    );

                return instructions;
            }
        }
    }
}