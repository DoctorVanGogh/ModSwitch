using UnityEngine;
using Verse;
// ReSharper disable RedundantArgumentDefaultValue

namespace DoctorVanGogh.ModSwitch {
    [StaticConstructorOnStartup]
    public class Assets {
        public static readonly Texture2D Edit;
        public static readonly Texture2D Delete;
        public static readonly Texture2D Settings;
        public static readonly Texture2D Document;
        public static readonly Texture2D Apply;
        public static readonly Texture2D Extract;
        public static readonly Texture2D Undo;
        public static readonly Texture2D White;

        static Assets() {
            Edit = ContentFinder<Texture2D>.Get("UI/Edit", true);
            Delete = ContentFinder<Texture2D>.Get("UI/Delete", true);
            Settings = ContentFinder<Texture2D>.Get("UI/Settings", true);
            Document = ContentFinder<Texture2D>.Get("UI/Document", true);
            Apply = ContentFinder<Texture2D>.Get("UI/Apply", true);
            Extract = ContentFinder<Texture2D>.Get("UI/Extract", true);
            Undo = ContentFinder<Texture2D>.Get("UI/Undo", true);
            White = ContentFinder<Texture2D>.Get("UI/White", true);
        }


    }
}
