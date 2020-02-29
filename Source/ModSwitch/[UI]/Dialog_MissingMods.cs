﻿using System;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    public enum ModSetAction {
        Apply = 0,
        Import
    }

    public class Dialog_MissingMods : Window {
        private const float TitleHeight = 42f;
        private const float ButtonHeight = 35f;

        private const float buttonCount = 3f;
        private const float buttonSpacing = 20f;
        private readonly Action _ignore;
        private readonly Action _remove;
        private readonly Action _workshop;
        private float creationRealTime = -1f;
        public Action defaultAction;

        private Vector2 scrollPosition = Vector2.zero;

        public string text;

        public ModSetAction Trigger { get; set; } = ModSetAction.Apply;

        public Dialog_MissingMods(string text, Action ignore, Action workshop, Action remove) {
            this.text = text;
            defaultAction = ignore;
            _ignore = ignore;
            _workshop = workshop;
            _remove = remove;

            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            creationRealTime = RealTime.LastRealTime;
            onlyOneOfTypeAllowed = false;
        }

        public override Vector2 InitialSize => new Vector2(640f, 460f);

        private void AddButton(Rect inRect, int index, string label, Action action, string tooltip = null, bool? dangerState = null) {
            GUI.color = dangerState == null
                ? Color.white
                : dangerState.Value
                    ? new Color(1f, 0.3f, 0.35f)
                    : new Color(0.35f, 1f, 0.3f);
            float buttonWidth = (inRect.width - (buttonCount - 1) * buttonSpacing) / buttonCount;
            Rect rect = new Rect((index - 1) * (buttonWidth + buttonSpacing), inRect.height - ButtonHeight, buttonWidth, ButtonHeight);
            if (tooltip != null)
                TooltipHandler.TipRegion(rect, new TipSignal(tooltip));
            if (Widgets.ButtonText(rect, label, true, false, true)) {
                action();
                Close(true);
            }
        }

        public override void DoWindowContents(Rect inRect) {
            float verticalPos = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, verticalPos, inRect.width, TitleHeight), LanguageKeys.keyed.ModSwitch_MissingMods_Title.Translate());
            verticalPos += TitleHeight;

            Text.Font = GameFont.Small;
            Rect outRect = new Rect(inRect.x, verticalPos, inRect.width, inRect.height - ButtonHeight - 5f - verticalPos);
            float width = outRect.width - 16f;
            Rect viewRect = new Rect(0f, 0f, width, Text.CalcHeight(text, width));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), text);
            Widgets.EndScrollView();

            AddButton(
                inRect,
                1,
                LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Ignore.Translate(),
                _ignore,
                LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Ignore_Tip.Translate());
            AddButton(
                inRect,
                2,
                LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Workshop.Translate(),
                _workshop,
                Trigger == ModSetAction.Apply
                    ? LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Workshop_Apply_Tip.Translate()
                    : LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Workshop_Import_Tip.Translate(),
                false);
            if (_remove != null)
                AddButton(
                    inRect,
                    3,
                    LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Remove.Translate(),
                    _remove,
                    LanguageKeys.keyed.ModSwitch_MissingMods_Choice_Remove_Tip.Translate(),
                    true);
        }
    }
}