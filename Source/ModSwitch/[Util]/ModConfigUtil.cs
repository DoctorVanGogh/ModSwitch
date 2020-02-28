using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    public static class ModConfigUtil {

        public static string GetConfigFilename(string foldername, string modClassName) => $"Mod_{foldername}_{modClassName}.xml";
        public static string GetConfigFilename(this ModMetaData md, string modClassName) => GetConfigFilename(md.FolderName, modClassName);
        
    }
}
