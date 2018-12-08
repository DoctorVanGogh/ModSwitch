using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace DoctorVanGogh.ModSwitch
{
    class Dialog_Exception : Dialog_MessageBox {
        public Dialog_Exception(Exception e, string title = null) : base(e.Message, title: title ?? LanguageKeys.keyed.ModSwitch_Dialog_Error.Translate()) {
            
        }
    }
}
