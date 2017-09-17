using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {

    class ModAttributes : IExposable {
        public List<IExposable> attributes = new List<IExposable>();
        public string Key = String.Empty;

        public void ExposeData() {
            Scribe_Values.Look(ref Key, @"key");
            Scribe_Collections.Look(ref attributes, false, @"attributes");
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
