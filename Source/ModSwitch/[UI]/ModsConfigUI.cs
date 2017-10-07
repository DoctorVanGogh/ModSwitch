using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Harmony;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace DoctorVanGogh.ModSwitch {
    public static class ModsConfigUI {
        public enum ModsChangeAction {
            Restart,
            Ignore,
            Query
        }

        public static readonly MethodInfo miCheckboxLabeledSelectable = AccessTools.Method(typeof(Widgets), nameof(Widgets.CheckboxLabeledSelectable));
        public static readonly MethodInfo miGuiSetContentColor = AccessTools.Property(typeof(GUI), nameof(GUI.color)).GetSetMethod(true);

        public static readonly FieldInfo fiPage_ModsConfig_ActiveModsWhenOpenedHash = AccessTools.Field(typeof(Page_ModsConfig), "activeModsWhenOpenedHash");
        private static readonly MethodInfo miGetModWithIdentifier = AccessTools.Method(typeof(ModLister), "GetModWithIdentifier");

        private static IDictionary<string, Color> _colorMap;

        public static Action restartRequiredHandler = RestartRequiredHandler;

        public static IDictionary<string, Color> ColorMap => _colorMap
                                                             ?? (_colorMap = new Dictionary<string, Color> {
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_white.Translate(), Color.white},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_black.Translate(), Color.black},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_gray.Translate(), Color.gray},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_red.Translate(), Color.red},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_green.Translate(), Color.green},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_blue.Translate(), Color.blue},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_magenta.Translate(), Color.magenta},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_cyan.Translate(), Color.cyan},
                                                                                                               {LanguageKeys.keyed.ModSwitch_Color_yellow.Translate(), Color.yellow}
                                                                                                           });

        public static ModsChangeAction ChangeAction { get; set; } = ModsChangeAction.Query;

        private static void CopyModLocal(ModMetaData mod, string name, StringBuilder log, bool? forceCopySettings = null, bool deleteExisting = false) {
            string targetDirectory = Path.Combine(GenFilePaths.CoreModsFolderPath, name);

            if (deleteExisting && Directory.Exists(targetDirectory)) {
                Directory.Delete(targetDirectory, true);
                Directory.CreateDirectory(targetDirectory);
            }

            // copy mod
            Util.DirectoryCopy(mod.RootDir.FullName, targetDirectory, true);
            log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Copy.Translate(mod.Name, targetDirectory));
            log.AppendLine();

            // copy mod settings
            string[] settings = Directory.GetFiles(GenFilePaths.ConfigFolderPath);
            string pattern = $@"^Mod_{mod.Identifier}_([^\.]+).xml$";
            Util.Trace(pattern);
            Regex rgxSettings = new Regex(pattern);
            var matching = settings
                .Select(s => rgxSettings.Match(Path.GetFileName(s)))
                .Where(m => m.Success)
                .Select(
                    m => new {
                                 source = Path.Combine(GenFilePaths.ConfigFolderPath, m.Value),
                                 destination = Path.Combine(
                                     GenFilePaths.ConfigFolderPath,
                                     string.Format(
                                         "Mod_{0}_{1}.xml",
                                         name,
                                         m.Groups[1].Value))
                             })
                .ToArray();

            Action<bool> copySettings = b => {
                                            foreach (var element in matching)
                                                File.Copy(element.source, element.destination, b);
                                            log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Settings.Translate(matching.Length));
                                        };

            switch (forceCopySettings) {
                case true:
                    copySettings(true);
                    break;
                case null:
                    if (matching.Any(t => File.Exists(t.destination)))
                        Find.WindowStack.Add(
                            new Dialog_MessageBox(
                                LanguageKeys.keyed.ModSwitch_ExistingSettings.Translate(name),
                                LanguageKeys.keyed.ModSwitch_ExistingSettings_Choice_Overwrite.Translate(),
                                () => copySettings(true),
                                LanguageKeys.keyed.ModSwitch_ExistingSettings_Choice_Skip.Translate(),
                                () => { log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Skipped.Translate()); },
                                LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                true));
                    else
                        copySettings(false);
                    break;
                case false:
                    log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_Skipped.Translate());
                    break;
            }
        }

        private static List<FloatMenuOption> CreateColorizationOptions(ModMetaData mod) {
            return ColorMap.Select(
                               kvp => new FloatMenuOption(
                                   $@"{kvp.Key.Colorize(kvp.Value)} ({kvp.Key})",
                                   () => LoadedModManager.GetMod<ModSwitch>().CustomSettings.Attributes[mod].Color = kvp.Value)
                           )
                           .ToList();
        }

        private static void DeferRestart() {
            ModSwitch.IsRestartDefered = true;
        }

        public static void DoContextMenu(ModMetaData mod) {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (mod.OnSteamWorkshop) {
                if (SteamAPI.IsSteamRunning())
                    options.Add(
                        new FloatMenuOption(
                            LanguageKeys.keyed.ModSwitch_CopyLocal.Translate(),
                            () => {
                                Find.WindowStack.Add(
                                    new Dialog_SetText(
                                        name => {
                                            StringBuilder log = new StringBuilder();
                                            CopyModLocal(mod, name, log);
                                            UpdateSteamAttributes(name, mod, log);
                                            Helpers.RebuildModsList();
                                            ShowLog(log, LanguageKeys.keyed.ModSwitch_CopyLocal.Translate());
                                        },
                                        $"{mod.Name}",
                                        name => {
                                            string targetDirectory = Path.Combine(GenFilePaths.CoreModsFolderPath, name);
                                            if (Path.GetInvalidPathChars().Any(name.Contains))
                                                return LanguageKeys.keyed.ModSwitch_Error_InvalidChars.Translate();
                                            if (Directory.Exists(targetDirectory))
                                                return LanguageKeys.keyed.ModSwitch_Error_TargetExists.Translate();

                                            // walk target path up to root, check we are under 'CoreModsFolderPath' - no '..\..\' shenanigans to break out of mods jail
                                            DirectoryInfo modsRoot = new DirectoryInfo(GenFilePaths.CoreModsFolderPath);
                                            for (DirectoryInfo current = new DirectoryInfo(targetDirectory);
                                                 current?.FullName != current?.Root.FullName;
                                                 current = current.Parent)
                                                if (current.FullName == modsRoot.FullName)
                                                    return null;

                                            return LanguageKeys.keyed.ModSwitch_Error_NotValid.Translate();
                                        }
                                    ));
                            }));
                else
                    options.Add(
                        new FloatMenuOption(
                            Helpers.ExplainError(
                                LanguageKeys.keyed.ModSwitch_CopyLocal.Translate(),
                                LanguageKeys.keyed.ModSwitch_Error_SteamNotRunning.Translate()),
                            null));
            } else {
                options.Add(
                    new FloatMenuOption(
                        LanguageKeys.keyed.ModSwitch_OpenFolder.Translate(),
                        () => Process.Start(mod.RootDir.FullName)
                    ));
                ModSwitch ms = LoadedModManager.GetMod<ModSwitch>();
                ModAttributes localAttributes = ms.CustomSettings.Attributes[mod];

                if (localAttributes.SteamOrigin != null) {
                    long? tsSteam = ms.CustomSettings.Attributes[localAttributes.SteamOrigin].LastUpdateTS;
                    long? tsCopy = localAttributes.SteamOriginTS;

                    string label = LanguageKeys.keyed.ModSwitch_Sync.Translate();

                    FloatMenuOption option = new FloatMenuOption();
                    if (tsCopy != null && tsCopy == tsSteam) {
                        option.Label = Helpers.ExplainError(label, LanguageKeys.keyed.ModSwitch_Sync_Identical.Translate());
                    } else {
                        option.Label = label;
                        option.action = () => {
                                            Find.WindowStack.Add(
                                                new Dialog_MessageBox(
                                                    LanguageKeys.keyed.ModSwitch_Sync_Message.Translate(
                                                        mod.Identifier,
                                                        Helpers.WrapTimestamp(tsCopy),
                                                        Helpers.WrapTimestamp(tsSteam)
                                                    ),
                                                    LanguageKeys.keyed.ModSwitch_Sync_Choice_KeepSettings.Translate(),
                                                    () => { SyncSteam(mod, localAttributes.SteamOrigin, false); },
                                                    LanguageKeys.keyed.ModSwitch_Sync_Choice_CopySteam.Translate(),
                                                    () => { SyncSteam(mod, localAttributes.SteamOrigin, true); },
                                                    LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate(),
                                                    false) {
                                                               doCloseX = true,
                                                               closeOnEscapeKey = true
                                                           });
                                        };
                    }
                    options.Add(option);
                } else {
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        if (SteamAPI.IsSteamRunning()) {
                            ModMetaData[] installed = ModLister.AllInstalledMods.ToArray();

                            if (installed.Length > 0)
                                options.Add(
                                    new FloatMenuOption(
                                        LanguageKeys.keyed.ModSwitch_SetOrigin.Translate(),
                                        () => Find.WindowStack.Add(
                                            new FloatMenu(
                                                installed
                                                    .Where(mmd => mmd.OnSteamWorkshop)
                                                    .Select(
                                                        mmd => new FloatMenuOption(
                                                            mmd.Name,
                                                            () => Find.WindowStack.Add(
                                                                Dialog_MessageBox.CreateConfirmation(
                                                                    LanguageKeys.keyed.ModSwitch_SetOrigin_Confirm.Translate(mod.Name, mmd.Name),
                                                                    () => {
                                                                        ModAttributes attributes = ms.CustomSettings.Attributes[mod.Identifier];
                                                                        attributes.SteamOrigin = mmd.Identifier;
                                                                        Helpers.RebuildModsList();
                                                                    },
                                                                    true,
                                                                    LanguageKeys.keyed.ModSwitch_Confirmation_Title.Translate()
                                                                )
                                                            )))
                                                    .ToList()
                                            ))
                                    ));
                        } else {
                            options.Add(
                                new FloatMenuOption(
                                    Helpers.ExplainError(
                                        LanguageKeys.keyed.ModSwitch_SetOrigin.Translate(),
                                        LanguageKeys.keyed.ModSwitch_Error_SteamNotRunning.Translate()
                                    ),
                                    null));
                        }
                }
            }

            options.Add(
                new FloatMenuOption(
                    LanguageKeys.keyed.ModSwitch_Color.Translate(),
                    () => { Find.WindowStack.Add(new FloatMenu(CreateColorizationOptions(mod))); }));


            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void DrawContentSource(Rect r, ContentSource source, Action clickAction, ModMetaData mod) {
            if (string.IsNullOrEmpty(LoadedModManager.GetMod<ModSwitch>().CustomSettings.Attributes[mod].SteamOrigin)) {
                ContentSourceUtility.DrawContentSource(
                    r,
                    source,
                    source == ContentSource.LocalFolder
                        ? (clickAction ?? (() => Process.Start(mod.RootDir.FullName)))
                        : clickAction);
            } else {
                Rect rect = new Rect(r.x, r.y + r.height / 2f - 12f, 24f, 24f);
                GUI.DrawTexture(rect, Assets.SteamCopy);
                Widgets.DrawHighlightIfMouseover(rect);

                TooltipHandler.TipRegion(rect, () => "Source".Translate() + ": " + LanguageKeys.keyed.ModSwitch_Source_SteamCopy.Translate(), (int) (r.x + r.y * 56161f));
                if (Widgets.ButtonInvisible(rect, false))
                    Process.Start(mod.RootDir.FullName);
            }
            if (!mod.VersionCompatible) {
                Rect r2 = new Rect(r.x + 4f, r.y + r.height / 2f - 12f + 4f, 20f, 20f);
                GUI.DrawTexture(r2, Assets.WarningSmall);
            }
        }

        public static void OnModsChanged() {
            switch (ChangeAction) {
                case ModsChangeAction.Restart:
                    Find.WindowStack.Add(new Dialog_MessageBox("ModsChanged".Translate(), null, GenCommandLine.Restart, null, null, null, false));
                    break;
                default:
                case ModsChangeAction.Ignore:
                    break;
                case ModsChangeAction.Query:
                    if (!ModSwitch.IsRestartDefered)
                        Find.WindowStack.Add(
                            new Dialog_MessageBox(
                                "ModsChanged".Translate(),
                                LanguageKeys.keyed.ModSwitch_RestartRequired_Restart.Translate(),
                                GenCommandLine.Restart,
                                LanguageKeys.keyed.ModSwitch_RestartRequired_Defer.Translate(),
                                DeferRestart,
                                null,
                                true));
                    break;
            }
        }

        private static void RestartRequiredHandler() {
            Find.WindowStack.Add(
                new Dialog_MessageBox(
                    "ModsChanged".Translate(),
                    null,
                    GenCommandLine.Restart,
                    null,
                    null,
                    null,
                    false) {
                               doCloseX = true
                           }
            );
        }

        private static void ShowLog(StringBuilder log, string title) {
            Find.WindowStack.Add(
                new Dialog_MessageBox(log.ToString()) {
                                                          title = title
                                                      });
        }

        private static void SyncSteam(ModMetaData mod, string steamId, bool forceCopySettings) {
            StringBuilder log = new StringBuilder();
            ModMetaData mdOriginal = Helpers.GetMetadata(steamId);
            CopyModLocal(mdOriginal, mod.Identifier, log, forceCopySettings, true);
            UpdateSteamAttributes(mod.Identifier, mdOriginal, log);
            Helpers.RebuildModsList();
            ShowLog(log, LanguageKeys.keyed.ModSwitch_Sync.Translate());
        }

        private static void UpdateSteamAttributes(string name, ModMetaData original, StringBuilder log) {
            if (!original.OnSteamWorkshop)
                throw new ArgumentException();

            ModSwitch ms = LoadedModManager.GetMod<ModSwitch>();
            ModAttributes attributes = ms.CustomSettings.Attributes[name];
            attributes.SteamOrigin = original.Identifier;
            attributes.SteamOriginTS = ms.CustomSettings.Attributes[original].LastUpdateTS;

            if (attributes.SteamOriginTS == null) {
                log.AppendLine();
                log.AppendLine(LanguageKeys.keyed.ModSwitch_CopyLocal_Result_TimestampUnknown.Translate());
            }

            ms.WriteSettings();
        }


        public static class Helpers {
            public static string ExplainError(string label, string error) {
                return $"{label} *{error}*";
            }

            public static void ForceSteamWorkshopRequery() {
                AccessTools.Method(typeof(WorkshopItems), @"RebuildItemsList").Invoke(null, null);
            }

            public static ModMetaData GetMetadata(string identifier) {
                return (ModMetaData) miGetModWithIdentifier.Invoke(null, new[] {identifier});
            }

            public static void RebuildModsList() {
                AccessTools.Method(typeof(ModLister), @"RebuildModList").Invoke(null, null);
            }

            public static Color SetGUIColorMod(ModMetaData mod) {
                Color current = GUI.contentColor;

                GUI.color = LoadedModManager.GetMod<ModSwitch>().CustomSettings.Attributes[mod].Color ?? Color.white;

                return current;
            }

            public static void UpdateSteamTS(PublishedFileId_t pfid, uint ts) {
                LoadedModManager.GetMod<ModSwitch>().CustomSettings.Attributes[pfid.ToString()].LastUpdateTS = ts;
            }

            public static string WrapTimestamp(long? timestamp) {
                return timestamp != null ? Util.UnixTimeStampToDateTime(timestamp.Value).ToString("g") : $"<i>{LanguageKeys.keyed.ModSwitch_Sync_UnknownTimestamp.Translate()}</i>";
            }
        }

        public static class Search {
            public const float buttonSize = 24f;
            public const float buttonsInset = 2f;
            private const float SearchDefaultHeight = 29f;
            private const float SearchClearDefaultSize = 12f;
            private const string searchControlName = @"msSearch";
            public static string searchTerm = string.Empty;
            public static bool searchFocused;
            public static readonly GUIStyle DefaultSearchBoxStyle;

            static Search() {
                Text.Font = GameFont.Small;
                DefaultSearchBoxStyle = new GUIStyle(Text.CurTextFieldStyle);
            }

            public static void DoSearchBlock(Rect area, string weatermark, GUIStyle style = null) {
                float scale = area.height / SearchDefaultHeight;
                float clearSize = SearchClearDefaultSize * Math.Min(1, scale);

                Rect clearSearchRect = new Rect(area.xMax - 4f - clearSize, area.y + (area.height - clearSize) / 2, clearSize, clearSize);
                bool shouldClearSearch = Widgets.ButtonImage(clearSearchRect, Widgets.CheckboxOffTex);

                Rect searchRect = area;
                string watermark = searchTerm != string.Empty || searchFocused ? searchTerm : weatermark;

                bool escPressed = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
                bool clickedOutside = !Mouse.IsOver(searchRect) && Event.current.type == EventType.MouseDown;

                if (!searchFocused)
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);

                GUI.SetNextControlName(searchControlName);
                string searchInput = GUI.TextField(searchRect, watermark, style ?? DefaultSearchBoxStyle);
                GUI.color = Color.white;

                if (searchFocused)
                    searchTerm = searchInput;

                if ((GUI.GetNameOfFocusedControl() == searchControlName || searchFocused) && (escPressed || clickedOutside)) {
                    GUIUtility.keyboardControl = 0;
                    searchFocused = false;
                } else if (GUI.GetNameOfFocusedControl() == searchControlName && !searchFocused) {
                    searchFocused = true;
                }

                if (shouldClearSearch)
                    searchTerm = string.Empty;
            }
        }
    }
}