using System;
using System.Diagnostics;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    static class Util {
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

        public static string Colorize(this string text, Color color) {
            return $"<color=#{((byte)(color.r * 255)):X2}{((byte)(color.g * 255)):X2}{((byte)(color.b * 255)):X2}{((byte)(color.a * 255)):X2}>{text}</color>";
        }
    }
}
