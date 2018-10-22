using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
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
                public static void Postfix(Rect rect) {
                    const float bottomHeight = 52f;
                    LoadedModManager.GetMod<ModSwitch>()?.DoModsConfigWindowContents(new Rect(0, rect.height - bottomHeight + 8f, 350f, bottomHeight - 8f));
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
                    return ModsConfigUI.Search.MatchCriteria(mod.Name) || ModsConfigUI.Search.MatchCriteria(mod.TargetVersion);
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
            // yeah, it's effectively a detour - sue me....
            public static bool Prefix(Page_ModsConfig __instance) {
                ModsConfig.Save();
                if ((int) ModsConfigUI.fiPage_ModsConfig_ActiveModsWhenOpenedHash.GetValue(__instance) != ModLister.InstalledModsListHash(true))
                    ModsConfigUI.OnModsChanged();
                return false;
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
            private static readonly FieldInfo fiModSwitch_IsRestartDefered = AccessTools.Field(typeof(ModSwitch), nameof(ModSwitch.IsRestartDefered));
            private static readonly FieldInfo fiModSwitchUI_RestartRequiredHandler = AccessTools.Field(typeof(ModsConfigUI), nameof(ModsConfigUI.restartRequiredHandler));

            /// <summary>
            ///     Transforms the closest following sequence after <see cref="OpCodes.Ldstr" /> <paramref name="anchor" /> of
            ///     <pre>
            ///         ldsfld       [dontcare]
            ///         [dontcare]
            ///         ...
            ///         newobj       instance void Verse.ListableOption::.ctor(string, class [System.Core]System.Action, string)
            ///     </pre>
            ///     into
            ///     <pre>
            ///         ldsfld       [dontcare]
            ///         ldsfld       ModSwitch.IsRestartDefered                 ; new
            ///         brfalse      *lblPostCheck*                             ; new
            ///         pop                                                     ; new, hacky but simple way to kill the loaded custom
            ///         action from stack
            ///         ldsfld       ModsConfigUI.restartRequiredHandler        ; replace with 'restart required' action
            ///         lblPostCheck:                                           ; new, injected branch label
            ///         [dontcare]
            ///         ...
            ///         newobj       instance void Verse.ListableOption::.ctor(string, class [System.Core]System.Action, string)
            ///     </pre>
            /// </summary>
            private static bool InjectDeferedRestartHint(List<CodeInstruction> instructions, ILGenerator ilGen, string anchor) {
                int idxNewColony = instructions.FirstIndexOf(ci => ci.opcode == OpCodes.Ldstr && ci.operand as string == anchor);
                if (idxNewColony == -1) {
                    Util.Warning($"Could not find DoMainMenuControls {anchor} anchor - not injecting code");
                    return false;
                }

                int idxAddNewColonyOption = instructions.FindIndex(idxNewColony, ci => ci.opcode == OpCodes.Newobj && ci.operand == ciNewListableOption);
                if (idxAddNewColonyOption == -1) {
                    Util.Warning($"Could not find DoMainMenuControls {anchor} ListOption constructor - not injecting code");
                    return false;
                }

                int idxNewColonyAction = instructions.FindLastIndex(idxAddNewColonyOption, idxAddNewColonyOption - idxNewColony, ci => ci.opcode == OpCodes.Ldsfld);
                if (idxNewColonyAction == -1) {
                    Util.Warning($"Could not find DoMainMenuControls {anchor} action field - not injecting code");
                    return false;
                }

                Label lblPostCheck = ilGen.DefineLabel();

                CodeInstruction ciPostAction = instructions[idxNewColonyAction + 1];
                if (ciPostAction.labels == null)
                    ciPostAction.labels = new List<Label>();

                ciPostAction.labels.Add(lblPostCheck);

                instructions.InsertRange(
                    idxNewColonyAction + 1,
                    new[] {
                              new CodeInstruction(OpCodes.Ldsfld, fiModSwitch_IsRestartDefered),
                              new CodeInstruction(OpCodes.Brfalse, lblPostCheck),
                              new CodeInstruction(OpCodes.Pop),
                              new CodeInstruction(OpCodes.Ldsfld, fiModSwitchUI_RestartRequiredHandler)
                          }
                );

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

        /// <summary>
        /// Bunch of patches providing Tree View of mods, grouped by version
        /// 
        /// Add tree view to mod list in Page_ModsConfig.
        /// The expected layout should look like:
        ///     - RimWorld 1.0
        ///         Core
        ///         HugsLib
        ///     + RimWorld B19
        /// </summary>
        public static class VersionTreeViewPatches
        {
            /// <summary>
            /// Surround original iteration of DoModRow with version tree nodes.
            /// </summary>
            [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
            public static class VersionIterationLoop
            {
                /* ===Code Instruction Lookup===
                 * 
                 * ==Original fragment==
                 * foreach (ModMetaData current in this.ModsInListOrder())
                 * {
                 *      this.DoModRow(listing_Standard, current, num2, reorderableGroup);
                 *      num2++;
                 * }
                 * 
                 * ==Decomplied ILCode==
                 * ldarg.0 |  | no labels
                 * call | IEnumerable`1 ModsInListOrder() | no labels
                 * callvirt | IEnumerator`1 GetEnumerator() | no labels
                 * stloc.s | System.Collections.Generic.IEnumerator`1[Verse.ModMetaData] (12) | no labels
                 * br | Label 4 | no labels
                 * ldloc.s | System.Collections.Generic.IEnumerator`1[Verse.ModMetaData] (12) | Label 5
                 * callvirt | Verse.ModMetaData get_Current() | no labels
                 * stloc.s | Verse.ModMetaData (11) | no labels
                 * 
                 * ldarg.0 |  | no labels
                 * ldloc.s | Verse.Listing_Standard (8) | no labels
                 * ldloc.s | Verse.ModMetaData (11) | no labels
                 * ldloc.s | System.Int32 (10) | no labels
                 * ldloc.s | System.Int32 (9) | no labels
                 * call | Void DoModRow(Verse.Listing_Standard, Verse.ModMetaData, Int32, Int32) | no labels
                 * ldloc.s | System.Int32 (10) | no labels
                 * ldc.i4.1 |  | no labels
                 * add |  | no labels
                 * stloc.s | System.Int32 (10) | no labels
                 * 
                 * ldloc.s | System.Collections.Generic.IEnumerator`1[Verse.ModMetaData] (12) | Label 4 
                 * callvirt | Boolean MoveNext() | no labels
                 * brtrue | Label 5 | no labels
                 * leave | Label 6 | no labels
                 * ldloc.s | System.Collections.Generic.IEnumerator`1[Verse.ModMetaData] (12) | no labels
                 * brfalse | Label 7 | no labels
                 * ldloc.s | System.Collections.Generic.IEnumerator`1[Verse.ModMetaData] (12) | no labels
                 * callvirt | Void Dispose() | no labels
                 * endfinally |  | Label 7
                 * 
                 * call | Int32 get_DownloadingItemsCount() | Label 6 //<- next code instruction with label to branch
                 */

                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr, ILGenerator ilGen)
                {
                    List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                    int dummy;
                    int startIndex, endIndex;
                    int doModRowIndex;

                    #region Find start of "foreach (ModMetaData current in this.ModsInListOrder()"
                    OpCode[] opCodes1 =
                    {
                        OpCodes.Ldarg_0,
                        OpCodes.Call,
                        OpCodes.Callvirt,
                        OpCodes.Stloc_S,
                        OpCodes.Br,
                    };
                    string[] operands1 =
                    {
                        "",
                        "IEnumerable`1 ModsInListOrder()",
                        "IEnumerator`1 GetEnumerator()",
                        "System.Collections.Generic.IEnumerator`1[Verse.ModMetaData]",
                        "System.Reflection.Emit.Label",
                    };
                    if (!HarmonyHelper.FindFragment(instructions, opCodes1, operands1, out startIndex, out dummy))
                        throw new Exception("Didn't find \"foreach (ModMetaData current in this.ModsInListOrder())\" fragment");
                    #endregion

                    #region Find end of foreach
                    OpCode[] opCodes2 =
                    {
                        OpCodes.Ldloc_S,
                        OpCodes.Callvirt,
                        OpCodes.Brtrue,
                        OpCodes.Leave,
                        OpCodes.Ldloc_S,
                        OpCodes.Brfalse,
                        OpCodes.Ldloc_S,
                        OpCodes.Callvirt,
                        OpCodes.Endfinally,
                    };
                    string[] operands2 =
                    {
                        "System.Collections.Generic.IEnumerator`1[Verse.ModMetaData",
                        "Boolean MoveNext()",
                        "System.Reflection.Emit.Label",
                        "System.Reflection.Emit.Label",
                        "System.Collections.Generic.IEnumerator`1[Verse.ModMetaData]",
                        "System.Reflection.Emit.Label",
                        "System.Collections.Generic.IEnumerator`1[Verse.ModMetaData]",
                        "Void Dispose()",
                        "",
                    };
                    if (!HarmonyHelper.FindFragment(instructions, opCodes2, operands2, out dummy, out endIndex))
                        throw new Exception("Didn't find \"foreach (ModMetaData current in this.ModsInListOrder())\" fragment");
                    #endregion

                    #region Find "this.DoModRow(listing_Standard, current, num2, reorderableGroup);"
                    OpCode[] opCodes3 =
                    {
                        OpCodes.Ldarg_0,
                        OpCodes.Ldloc_S,
                        OpCodes.Ldloc_S,
                        OpCodes.Ldloc_S,
                        OpCodes.Ldloc_S,
                        OpCodes.Call,
                    };
                    string[] operands3 =
                    {
                        "",
                        "Verse.Listing_Standard",
                        "Verse.ModMetaData",
                        "System.Int32",
                        "System.Int32",
                        "Void DoModRow(Verse.Listing_Standard, Verse.ModMetaData, Int32, Int32)",
                    };
                    if (!HarmonyHelper.FindFragment(instructions, opCodes3, operands3, out dummy, out doModRowIndex))
                        throw new Exception("Didn't find \"this.DoModRow(listing_Standard, current, num2, reorderableGroup);\" fragment");
                    #endregion

                    var listing_standard = instructions[doModRowIndex - 4].operand;
                    var num2 = instructions[doModRowIndex - 2].operand;
                    var reorderableGroup = instructions[doModRowIndex - 1].operand;

                    for (int i = startIndex; i <= endIndex; i++)
                        instructions.RemoveAt(startIndex);

                    int index = startIndex;
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Ldarg_0));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Ldloc_S, listing_standard));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Ldloc_S, num2));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Ldloc_S, reorderableGroup));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Call, typeof(VersionIterationLoop).GetMethod(nameof(NewFragment))));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Stloc_S, num2));

                    return instructions;
                }

                /// <summary>
                /// New fragment, replacing old one (foreach loop)
                /// </summary>
                /// <returns>new num2 value</returns>
                public static int NewFragment(Page_ModsConfig page, Listing_Standard listing_Standard, int num2, int reorderableGroup)
                {
                    if (string.IsNullOrEmpty(ModsConfigUI.Search.searchTerm))
                    {
                        InitVersionContainers(page);

                        #region Loaded Mods
                        if (VersionContainerMatchingSearchCriteria("ModSwitch.TreeView.Active".Translate(), LoadedModsContainer))
                        {
                            var rect = listing_Standard.GetRect(26f);
                            DrawVersionContainer(rect, "ModSwitch.TreeView.Active".Translate(), LoadedModsContainer);
                            if (!LoadedModsContainer.Collapsed)
                            {
                                DrawModsEntries(listing_Standard, LoadedModsContainer.Mods, page, reorderableGroup);
                            }
                        }
                        #endregion

                        #region Other Mods
                        var versionInOrder = new List<string>(VersionDictonary.Keys);
                        versionInOrder.Sort(new Comparison<string>((x, y) => CompareVersion(y, x)));
                        foreach (var version in versionInOrder)
                        {
                            if (!VersionContainerMatchingSearchCriteria(version, VersionDictonary[version]))
                                continue;
                            var rect = listing_Standard.GetRect(26f);
                            DrawVersionContainer(rect, version, VersionDictonary[version]);
                            if (!VersionDictonary[version].Collapsed)
                            {
                                DrawModsEntries(listing_Standard, VersionDictonary[version].Mods, page, reorderableGroup);
                            }
                        }
                        #endregion
                    }
                    else
                    {
                        var modsInListOrder = typeof(Page_ModsConfig).GetMethod("ModsInListOrder", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(page, new object[] { }) as IEnumerable<ModMetaData>;
                        foreach (var current in modsInListOrder)
                        {
                            var doModRow = typeof(Page_ModsConfig).GetMethod("DoModRow", BindingFlags.NonPublic | BindingFlags.Instance);
                            doModRow.Invoke(page, new object[] { listing_Standard, current, num2, reorderableGroup });
                            num2++;
                        }
                    }

                    return num2;
                }

                private static void DrawVersionContainer(Rect bounds, string label, VersionContainer container)
                {
                    float height = Text.CurFontStyle.lineHeight;
                    Rect textureRect = new Rect(bounds.xMin, bounds.yMin + 2, height, height);
                    Rect labelRect = new Rect(bounds.xMin + height + 5, bounds.yMin, bounds.width - height, bounds.height);

                    Texture texture;
                    if (container.Collapsed)
                        texture = Assets.Collapsed;
                    else
                        texture = Assets.Expanded;

                    //Widgets.DrawHighlight(rect);
                    Widgets.Label(labelRect, label);
                    Widgets.DrawTextureFitted(textureRect, texture, 1.0f);

                    if (Widgets.ButtonInvisible(bounds, false))
                        container.Collapsed = !container.Collapsed;
                }

                private static void DrawModsEntries(Listing_Standard listing_Standard, List<ModMetaData> mods, Page_ModsConfig page, int reorderableGroup)
                {
                    int gap = 30;
                    GUI.BeginGroup(new Rect(gap, 0, listing_Standard.ColumnWidth, listing_Standard.CurHeight + 26f * mods.Count));
                    listing_Standard.ColumnWidth -= gap;
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var doModRow = typeof(Page_ModsConfig).GetMethod("DoModRow", BindingFlags.NonPublic | BindingFlags.Instance);
                        doModRow.Invoke(page, new object[] { listing_Standard, mods[i], i, reorderableGroup });
                    }
                    listing_Standard.ColumnWidth += gap;
                    GUI.EndGroup();
                }

                private class VersionContainer
                {
                    public List<ModMetaData> Mods { get; set; }
                    public bool Collapsed { get; set; }
                    public VersionContainer() { Mods = new List<ModMetaData>(); Collapsed = true; }
                }

                private static void InitVersionContainers(Page_ModsConfig page)
                {
                    if (LoadedModsContainer == null)
                        LoadedModsContainer = new VersionContainer() { Collapsed = false };
                    else
                        LoadedModsContainer.Mods.Clear();
                    if (VersionDictonary == null)
                        VersionDictonary = new Dictionary<string, VersionContainer>();
                    else
                        foreach (var version in VersionDictonary.Keys)
                            VersionDictonary[version].Mods.Clear();


                    var modsInListOrder = typeof(Page_ModsConfig).GetMethod("ModsInListOrder", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(page, new object[] { }) as IEnumerable<ModMetaData>;

                    foreach (ModMetaData current in modsInListOrder)
                    {
                        if (current.Active)
                        {
                            LoadedModsContainer.Mods.Add(current);
                        }
                        else
                        {
                            string version = HarmonyHelper.ShortVerisonString(current.TargetVersion);
                            if (!VersionDictonary.ContainsKey(version))
                                VersionDictonary.Add(version, new VersionContainer());
                            VersionDictonary[version].Mods.Add(current);
                        }
                    }
                }

                private static bool VersionContainerMatchingSearchCriteria(string name, VersionContainer container)
                {
                    if (ModsConfigUI.Search.searchTerm != null && !ModsConfigUI.Search.MatchCriteria(name))
                    {
                        // Find at least one matching item
                        foreach (var mod in container.Mods)
                            // It can match custom version like 1.0.13
                            if (ModsConfigUI.Search.MatchCriteria(mod.Name) || ModsConfigUI.Search.MatchCriteria(mod.TargetVersion))
                                return true;

                        return false;
                    }
                    return true;
                }

                private static int CompareVersion(string x, string y)
                {
                    var xFragments = x.Split('.');
                    var yFragments = y.Split('.');

                    for (int i = 0; i < xFragments.Length || i < yFragments.Length; i++)
                    {
                        if (i == xFragments.Length)
                            return -1;
                        if (i == yFragments.Length)
                            return 1;

                        try
                        {
                            int xValue = int.Parse(xFragments[i]);
                            int yValue = int.Parse(yFragments[i]);
                            if (xValue != yValue)
                                return xValue - yValue;
                        }
                        catch (FormatException e) { }
                    }
                    return 0;
                }

                private static VersionContainer LoadedModsContainer { get; set; }

                private static Dictionary<string, VersionContainer> VersionDictonary { get; set; }
            }

            /// <summary>
            /// This patch extend scroll view height, due to additional VersionContainers labels
            /// It need to add 26 pixels for every VersionContainer label
            /// </summary>
            [HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents))]
            public static class ExpandScrollViewHeight
            {
                /* ===Code Instruction Lookup===
                 * 
                 * ==Original fragment==
                 * float height = (float)ModLister.AllInstalledMods.Count<ModMetaData>() * 26f + 8f;
                 * 
                 * ==Decompiled ILCode==
                 * call | IEnumerable`1 get_AllInstalledMods() | no labels
                 * call | Int32 Count[ModMetaData](IEnumerable`1) | no labels
                 * conv.r4 |  | no labels
                 * ldc.r4 | 26 | no labels
                 * mul |  | no labels
                 * ldc.r4 | 8 | no labels
                 * add |  | no labels
                 * stloc.s | System.Single (5) | no labels
                 * 
                 */

                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr, ILGenerator ilGen)
                {
                    List<CodeInstruction> instructions = new List<CodeInstruction>(instr);

                    int dummy, endIndex;

                    #region Find end of "float height = (float)ModLister.AllInstalledMods.Count<ModMetaData>() * 26f + 8f;"
                    OpCode[] opCodes1 =
                    {
                        OpCodes.Call,
                        OpCodes.Call,
                        OpCodes.Conv_R4,
                        OpCodes.Ldc_R4,
                        OpCodes.Mul,
                        OpCodes.Ldc_R4,
                        OpCodes.Add,
                        OpCodes.Stloc_S,
                    };
                    string[] operands1 =
                    {
                        "IEnumerable`1 get_AllInstalledMods()",
                        "Int32 Count[ModMetaData](IEnumerable`1)",
                        "",
                        "26",
                        "",
                        "8",
                        "",
                        "System.Single",
                    };
                    if (!HarmonyHelper.FindFragment(instructions, opCodes1, operands1, out dummy, out endIndex))
                        throw new Exception("Didn't find \"foreach (ModMetaData current in this.ModsInListOrder())\" fragment");
                    #endregion

                    var height = instructions[endIndex].operand;

                    int index = endIndex + 1;
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Ldloc_S, height));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Call, typeof(ExpandScrollViewHeight).GetMethod(nameof(GetHeightToAdd))));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Add));
                    instructions.Insert(index++, new CodeInstruction(OpCodes.Stloc_S, height));

                    return instructions;
                }

                public static float GetHeightToAdd()
                {
                    var versions = new List<string>();
                    foreach (var mod in ModLister.AllInstalledMods)
                    {
                        string version = HarmonyHelper.ShortVerisonString(mod.TargetVersion);
                        if (!versions.Contains(version))
                            versions.Add(version);
                    }

                    return 26f * versions.Count;
                }
            }

            public static class HarmonyHelper
            {
                public static bool FindFragment(List<CodeInstruction> instructions, OpCode[] opCodes, String[] operands, out int startIndex, out int endIndex)
                {
                    if (opCodes.Length != operands.Length)
                        throw new Exception("Arguments does not match requirments");

                    int step = 0;
                    int finalStep = opCodes.Length;
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        var instr = instructions[i];

                        bool matchingOpCodes = instr.opcode == opCodes[step];
                        bool noOperands = instr.operand == null || string.IsNullOrEmpty(operands[step]);
                        bool matchingOperands = instr.operand != null && instr.operand.ToString().Contains(operands[step]);

                        if (matchingOpCodes && (noOperands || matchingOperands))
                            step++;
                        else
                            step = 0;

                        if (step == finalStep)
                        {
                            startIndex = i - step + 1;
                            endIndex = i;
                            return true;
                        }
                    }
                    startIndex = -1;
                    endIndex = -1;
                    return false;
                }

                public static string ShortVerisonString(string targetVersion)
                {
                    return VersionControl.MajorFromVersionString(targetVersion).ToString() + "." + VersionControl.MinorFromVersionString(targetVersion).ToString();
                }
            }
        }
    }
}