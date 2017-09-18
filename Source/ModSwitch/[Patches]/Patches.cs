using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
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
                /*Find.WindowStack.Add(
                        new FloatMenu(
                            new List<FloatMenuOption> {
                                new FloatMenuOption(
                                    "Color",
                                    () => {
                                        Find.WindowStack.Add(
                                                new FloatMenu(
                                                    ColorMap.Select(
                                                                kvp => new FloatMenuOption(
                                                                           kvp.Key,
                                                                           () => LoadedModManager.GetMod<ModSwitch>()
                                                                                                 .SetModColor(mod, kvp.Value),
                                                                           extraPartWidth: 16f,
                                                                           extraPartOnGUI: r => {
                                                                                               var color = GUI.backgroundColor;
                                                                                               GUI.backgroundColor = kvp.Value;
                                                                                               GUI.DrawTexture(r, Assets.White);
                                                                                               GUI.backgroundColor = color;
                                                                                               return false;
                                                                                           })
                                                            ).ToList()
                                                ));
                                    })
                            }));*/

                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("Color: *coming soon*", null)}));
            }

            public static IDictionary<string, Color> ColorMap {
                get { return _colorMap ?? (_colorMap = typeof(Color).GetProperties(BindingFlags.Public | BindingFlags.Static).ToDictionary(pi => pi.Name, pi => (Color) pi.GetValue(null, null))); }
            }

            public static Color SetGUIColorMod(ModMetaData mod) {
                var current = GUI.contentColor;

                GUI.color = LoadedModManager.GetMod<ModSwitch>().GetModColor(mod);

                return current;
            }
        }
    }
}