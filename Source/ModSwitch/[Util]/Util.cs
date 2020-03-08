﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    internal static class Util {
        public static void DisplayError(Exception e, string title = null) {
            Util.Warning(e.ToString());
            Find.WindowStack.Add(new Dialog_Exception(e, title));
        }

        public static void AddRange<TItem>(this ICollection<TItem> collection, IEnumerable<TItem> values) {
            foreach (TItem value in values)
                collection.Add(value);
        }

        public static string Colorize(this string text, Color color) {
            return $"<color=#{(byte) (color.r * 255):X2}{(byte) (color.g * 255):X2}{(byte) (color.b * 255):X2}{(byte) (color.a * 255):X2}>{text}</color>";
        }

        public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs) {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
                Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files) {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
                foreach (DirectoryInfo subdir in dirs) {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
        }

        public static void Error(string s) {
            Verse.Log.Error($"[ModSwitch]: {s}");
        }

        public static void Error(Exception e) {
            Error(e.ToString());
        }


        public static void Log(string s) {
            Verse.Log.Message($"[ModSwitch]: {s}");
        }

        [Conditional("TRACE")]
        public static void Trace(string s) {
            Log(s);
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp) {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static void Warning(string s) {
            Verse.Log.Warning($"[ModSwitch]: {s}");
        }

        private static string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        private static string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

        public static string SanitizeFileName(string fileName) {
            return System.Text.RegularExpressions.Regex.Replace(fileName, invalidRegStr, "_");
        }

        private static readonly Version RW_11 = new Version(1, 1);

        private static readonly Regex rgxSteamModId = new Regex(@"^\d+$", RegexOptions.Singleline | RegexOptions.Compiled);

        public static Func<ModMetaData, string> GetVersionSpecificIdMapping(Version version) {
            // 1.1+ uses packageid to identify a mod
            if (version >= RW_11)
                return  mmd => mmd.PackageId;
            // pre 1.1 uses folder name for id
            return mmd => mmd.FolderName;
        }

        public static string BuildWorkshopUrl(string name, string id) {
            return rgxSteamModId.IsMatch(id)
                ? $"http://steamcommunity.com/sharedfiles/filedetails/?id={id}"
                : $"https://steamcommunity.com/workshop/browse/?appid=294100&searchtext={name}&browsesort=textsearch&section=items";
        }

        public static string Concat<T>(this IEnumerable<T> items, Func<T, string> itemFormatter, string delimiter = ", ", string seed = "") {
            return items.Aggregate(
                new StringBuilder(seed),
                (sb, item) => sb.Length == 0
                    ? sb.Append(itemFormatter(item))
                    : sb.Append($"{delimiter}{itemFormatter(item)}"),
                sb => sb.ToString());
        }
    }
}