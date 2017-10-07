using System;
using System.Collections.Generic;
using System.Xml;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    public static class Scribe_Custom {
        public static void Look<TCollection, TItem>(ref TCollection collection, bool saveDestroyedThings, string label, object[] ctorArgsCollection = null, params object[] ctorArgsItem)
            where TCollection : ICollection<TItem>
            where TItem : IExposable {
            if (Scribe.EnterNode(label))
                try {
                    if (Scribe.mode == LoadSaveMode.Saving) {
                        if (collection == null)
                            Scribe.saver.WriteAttribute("IsNull", "True");
                        else
                            foreach (TItem current in collection) {
                                TItem t2 = current;
                                Scribe_Deep.Look(ref t2, saveDestroyedThings, "li", ctorArgsItem);
                            }
                    } else if (Scribe.mode == LoadSaveMode.LoadingVars) {
                        XmlNode curXmlParent = Scribe.loader.curXmlParent;
                        XmlAttribute xmlAttribute = curXmlParent.Attributes["IsNull"];
                        if (xmlAttribute != null && xmlAttribute.Value.ToLower() == "true") {
                            collection = default(TCollection);
                        } else {
                            collection = (TCollection) Activator.CreateInstance(typeof(TCollection), ctorArgsCollection);
                            foreach (XmlNode subNode2 in curXmlParent.ChildNodes) {
                                TItem item2 = ScribeExtractor.SaveableFromNode<TItem>(subNode2, ctorArgsItem);
                                collection.Add(item2);
                            }
                        }
                    }
                } finally {
                    Scribe.ExitNode();
                }
        }
    }
}