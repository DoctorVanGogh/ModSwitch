using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngineInternal;
using Verse;

namespace DoctorVanGogh.ModSwitch
{
    class MS_GenFilePaths {
        public static PropertyInfo piSavedFolderPath = AccessTools.Property(typeof(GenFilePaths), "SavedGamesFolderPath");

        public static string ModSwitchFolderPath = Path.Combine((string)piSavedFolderPath.GetValue(null, null), "ModSwitch");

        public static IEnumerable<FileInfo> AllExports {
            get {
                EnsureExportFolderExists();

                DirectoryInfo directoryInfo = new DirectoryInfo(ModSwitchFolderPath);

                return from f in directoryInfo.GetFiles()
                       where f.Extension == ExportExtension
                       orderby f.LastWriteTime descending
                       select f;
            }
        }

        public const string ExportExtension = ".rws";

        public static string FilePathForModSetExport(string setName) {
            EnsureExportFolderExists();

            return Path.Combine(ModSwitchFolderPath, Util.SanitizeFileName(setName) + ExportExtension);
        }

        public static void EnsureExportFolderExists() {
            if (!Directory.Exists(ModSwitchFolderPath))
                Directory.CreateDirectory(ModSwitchFolderPath);
        }
    }
}
 