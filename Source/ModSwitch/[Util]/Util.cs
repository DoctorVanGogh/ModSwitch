using System;
using System.Diagnostics;
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

        public static void Error(string s) {
            Log.Error($"[ModSwitch]: {s}");
        }

        public static void Error(Exception e) {
            Error(e.ToString());
        }
    }
}
