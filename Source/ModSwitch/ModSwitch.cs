﻿using System.Reflection;

using HarmonyLib;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    internal class ModSwitch : Mod {
        private static readonly FieldInfo fiModLister_mods;

        public static bool IsRestartDefered;

        static ModSwitch() {
            fiModLister_mods = AccessTools.Field(typeof(ModLister), "mods");
        }

        public ModSwitch(ModContentPack content) : base(content) {
            Assembly assembly = typeof(ModSwitch).Assembly;

            Log.Message($"ModSwitch {assembly.GetName().Version} - loading");

            Harmony harmony = new Harmony("DoctorVanGogh.ModSwitch");

            harmony.PatchAll(assembly);

            Log.Message($"ModSwitch {assembly.GetName().Version} - initialized patches...");

            CustomSettings = GetSettings<Settings>();
        }

        public Settings CustomSettings { get; }

        public void DoModsConfigWindowContents(Rect bottom) {
            CustomSettings.DoModsConfigWindowContents(bottom);
        }

        public override void DoSettingsWindowContents(Rect inRect) {
            CustomSettings.DoWindowContents(inRect);
        }

        public override string SettingsCategory() {
            return LanguageKeys.keyed.ModSwitch.Translate();
        }
    }
}