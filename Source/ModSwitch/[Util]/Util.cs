using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    class Util {
        [Conditional("TRACE")]
        public static void Trace(string s) {
            Log.Message($"[ModSwitch]: {s}");
        }

        public static void Warning(string s) {
            Log.Warning($"[ModSwitch]: {s}");
        }
    }
}
