using System;
using UnityEngine;
using Verse;

namespace DoctorVanGogh.ModSwitch {
   
        public class Dialog_SetText : Window {
            private readonly Action<string> _valueSetter;
            private readonly Predicate<string> _checkName;
            protected virtual int MaxNameLength => 28;

            public override Vector2 InitialSize => new Vector2(280f, 175f);

            private string _inputText;

            public Dialog_SetText(Action<string> valueSetter, string value = null, Predicate<string> checkName = null ) {
                _valueSetter = valueSetter;
                _checkName = checkName;

                this.forcePause = true;
                this.doCloseX = true;
                this.closeOnEscapeKey = true;
                this.absorbInputAroundWindow = true;
                this.closeOnClickedOutside = true;

                this._inputText = value ?? String.Empty;
            }

            protected virtual AcceptanceReport NameIsValid(string name) {
                return name.Length != 0 && _checkName?.Invoke(name) != false;
            }

            public override void DoWindowContents(Rect inRect) {
                Text.Font = GameFont.Small;
                bool flag = false;
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return) {
                    flag = true;
                    Event.current.Use();
                }
                string text = Widgets.TextField(new Rect(0f, 15f, inRect.width, 35f), this._inputText);
                if (text.Length < this.MaxNameLength) {
                    this._inputText = text;
                }
                if (Widgets.ButtonText(new Rect(15f, inRect.height - 35f - 15f, inRect.width - 15f - 15f, 35f), "OK", true, false, true) || flag) {
                    AcceptanceReport acceptanceReport = this.NameIsValid(this._inputText);
                    if (acceptanceReport.Accepted) {
                        _valueSetter(this._inputText);
                        Find.WindowStack.TryRemove(this, true);
                    }
                }
            }
        }
   
}
