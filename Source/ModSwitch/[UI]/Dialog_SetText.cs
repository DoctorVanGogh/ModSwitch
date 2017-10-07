using System;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    public class Dialog_SetText : Window {
        private readonly Func<string, string> _checkName;
        private readonly Action<string> _valueSetter;

        private string _inputText;

        public Dialog_SetText(Action<string> valueSetter, string value = null, Func<string, string> checkName = null) {
            _valueSetter = valueSetter;
            _checkName = checkName;

            forcePause = true;
            doCloseX = true;
            closeOnEscapeKey = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;

            _inputText = value ?? string.Empty;
        }

        protected virtual int MaxNameLength => 28;

        public override Vector2 InitialSize => new Vector2(280f, 175f);

        public override void DoWindowContents(Rect inRect) {
            Text.Font = GameFont.Small;
            bool flag = false;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return) {
                flag = true;
                Event.current.Use();
            }
            string text = Widgets.TextField(new Rect(0f, 15f, inRect.width, 35f), _inputText);
            if (text.Length < MaxNameLength)
                _inputText = text;

            AcceptanceReport acceptanceReport = NameIsValid(_inputText);
            if (!acceptanceReport.Accepted) {
                GUI.color = Color.red;
                Widgets.Label(new Rect(15f, inRect.y + 35f + 15f + 5f, inRect.width - 15f - 15f, inRect.height + (35f + 15f + 5f) * 2), acceptanceReport.Reason);
                GUI.color = Color.white;
            }

            if (Widgets.ButtonText(new Rect(15f, inRect.height - 35f - 15f, inRect.width - 15f - 15f, 35f), "OK", true, false, true) || flag)
                if (acceptanceReport.Accepted) {
                    _valueSetter(_inputText);
                    Find.WindowStack.TryRemove(this, true);
                }
        }

        protected virtual AcceptanceReport NameIsValid(string name) {
            if (string.IsNullOrEmpty(name))
                return false;
            string reason = _checkName?.Invoke(name);
            if (!string.IsNullOrEmpty(reason))
                return reason;
            return true;
        }
    }
}