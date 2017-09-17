using System.Diagnostics.CodeAnalysis;
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
    }
}
