using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    internal class ModSwitch : Mod {
        private static readonly FieldInfo fiModLister_mods;

        public static bool IsRestartDefered;

        public volatile static IDictionary<string, uint> TSUpdateCache = new ConcurrentDictionary<string, uint>();


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


            var cachedValues = Interlocked.Exchange(ref TSUpdateCache, new SteamUpdateAdapter(CustomSettings));
            if (cachedValues.Count != 0) {
                // yes, there may *still* be a race condition where we overwrite a later, but previously written, update timestamp with a cached, but earlier timestamp... not going to block threads here

                Log.Message($"ModSwitch - copying cached steam TS values.");
                foreach (KeyValuePair<string, uint> cachedTSValue in cachedValues) {
                    CustomSettings.Attributes[cachedTSValue.Key].LastUpdateTS = cachedTSValue.Value;
                }

            }
        }

        public Settings CustomSettings { get; }

        /// <summary>
        /// Call to draw the main mod config interaction buttons
        /// </summary>
        public void DoModsConfigWindowContents(Rect bottom, Page_ModsConfig owner) {
            CustomSettings.DoModsConfigWindowContents(bottom, owner);
        }

        public override void DoSettingsWindowContents(Rect inRect) {
            CustomSettings.DoWindowContents(inRect);
        }

        public override string SettingsCategory() {
            return LanguageKeys.keyed.ModSwitch.Translate();
        }

        /// <summary>
        /// <em>Severely</em> restricted dictionary adapter for writing steam TS updates to ModSwitch settings.
        /// <em>ONLY</em> the index setter <seealso cref="SteamUpdateAdapter.this"/> is actually implemented.
        /// All other calls will fail.
        /// </summary>
        private class SteamUpdateAdapter : IDictionary<string, uint> {
            private readonly Settings _owner;

            public SteamUpdateAdapter(Settings owner) {
                _owner = owner;
            }

            public IEnumerator<KeyValuePair<string, uint>> GetEnumerator() {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }

            public void Add(KeyValuePair<string, uint> item) {
                throw new NotImplementedException();
            }

            public void Clear() {
                throw new NotImplementedException();
            }

            public bool Contains(KeyValuePair<string, uint> item) {
                throw new NotImplementedException();
            }

            public void CopyTo(KeyValuePair<string, uint>[] array, int arrayIndex) {
                throw new NotImplementedException();
            }

            public bool Remove(KeyValuePair<string, uint> item) {
                throw new NotImplementedException();
            }

            public int Count { get; }
            public bool IsReadOnly { get; }
            public bool ContainsKey(string key) {
                throw new NotImplementedException();
            }

            public void Add(string key, uint value) {
                throw new NotImplementedException();
            }

            public bool Remove(string key) {
                throw new NotImplementedException();
            }

            public bool TryGetValue(string key, out uint value) {
                throw new NotImplementedException();
            }

            public uint this[string key] {
                get => throw new NotImplementedException();
                set => _owner.Attributes[key].LastUpdateTS = value;
            }

            public ICollection<string> Keys => throw new NotImplementedException();
            public ICollection<uint> Values => throw new NotImplementedException();
        }
    }
}