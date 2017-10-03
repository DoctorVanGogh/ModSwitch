using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {

    class ModAttributes : IExposable {
        public List<IExposable> attributes = new List<IExposable>();
        public string Key = String.Empty;

        public Color? Color;

        /// <summary>
        /// For mods copied from steam: original identifier
        /// </summary>
        public string SteamOrigin;
        /// <summary>
        /// For mods copied from a steam source: last uploaded TS at time of copy
        /// </summary>
        // HACK: shoulds be <c>uint</c>, but RW can't deserialize uints....
        public long? SteamOriginTS;

        /// <summary>
        /// For steam mods: last uploaded TS
        /// </summary>
        /// <remarks>NOT SERIALIZED</remarks>
        // HACK: shoulds be <c>uint</c>, but RW can't deserialize uints....
        public long? LastUpdateTS;

        public void ExposeData() {
            Scribe_Values.Look(ref Key, @"key");
            Scribe_Collections.Look(ref attributes, false, @"attributes");
            Scribe_Values.Look(ref Color, "color", null);
            Scribe_Values.Look(ref SteamOrigin, "origin", null);
            Scribe_Values.Look(ref SteamOriginTS, "originTS", null);

            if (Scribe.mode == LoadSaveMode.LoadingVars) {
                if (Color == null)
                    Color = attributes.OfType<MLBAttributes>().FirstOrDefault()?.color;
            }
        }

    }

    class MLBAttributes : IExposable {
        public string altName = String.Empty;

        public Color color = Color.white;
        public string installName = String.Empty;

        public void ExposeData() {
            Scribe_Values.Look(ref color, @"color");
            Scribe_Values.Look(ref altName, @"altName");
            Scribe_Values.Look(ref installName, @"installName");
        }
    }
}
