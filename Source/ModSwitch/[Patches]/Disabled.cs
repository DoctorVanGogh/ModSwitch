using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch
{
    class Disabled
    {

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

                private static MethodInfo miPage_ModsConfig_DoModRow = typeof(Page_ModsConfig).GetMethod("DoModRow", BindingFlags.NonPublic | BindingFlags.Instance);
                private static MethodInfo miModSwitch_NewDoModRow = typeof(VersionIterationLoop).GetMethod(nameof(NewFragment));

                private static FastInvokeHandler callPage_ModsConfig_DoModRowHandler = MethodInvoker.GetHandler(miPage_ModsConfig_DoModRow);


                // there is only a SINGLE call to DoModRow - just replace that
                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr, ILGenerator ilGen) => instr.MethodReplacer(miPage_ModsConfig_DoModRow, miModSwitch_NewDoModRow);

                /// <summary>
                /// New fragment, replacing old one (foreach loop)
                /// </summary>
                /// <returns>new num2 value</returns>
                public static int NewFragment(Page_ModsConfig page, Listing_Standard listing_Standard, ModMetaData md, int num2, int reorderableGroup)
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
                        callPage_ModsConfig_DoModRowHandler.Invoke(page, new object[] { listing_Standard, mods[i], i, reorderableGroup });
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

                    #region Find end of "float height = (float)ModLister.AllInstalledMods.Count<ModMetaData>() * 26f + 8f;"
                    var idxAnchor = instructions.FindIndex(ci => ci.opcode == OpCodes.Call && ci.operand == typeof(ModLister).GetProperty(nameof(ModLister.AllInstalledMods)).GetGetMethod());
                    if (idxAnchor == -1)
                        throw new Exception("Didn't find AllInstalledMods listing...");

                    var endIndex = instructions.FindIndex(idxAnchor, ci => ci.opcode == OpCodes.Stloc_S);
                    if (endIndex == -1)
                        throw new Exception("Didn't find post AllInstalledMods setter...");

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
                    return VersionControl.TryParseVersionString(targetVersion, out Version v)
                        ? v.ToString(2)
                        : targetVersion;
                }
            }
        }
    }
}
