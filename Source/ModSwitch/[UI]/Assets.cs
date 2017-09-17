using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    [StaticConstructorOnStartup]
    public class Assets {

        public static readonly Texture2D Edit;
        public static readonly Texture2D Delete;
        public static readonly Texture2D Settings;
        public static readonly Texture2D Document;

        static Assets() {

            Edit = ContentFinder<Texture2D>.Get("UI/Edit", true);
            Delete = ContentFinder<Texture2D>.Get("UI/Delete", true);
            Settings = ContentFinder<Texture2D>.Get("UI/Settings", true);
            Document = ContentFinder<Texture2D>.Get("UI/Document", true);
        }
    }
}
