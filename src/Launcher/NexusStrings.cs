using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AOLauncher.Localization;

namespace AOLauncher
{
    internal static class NexusStrings
    {
        private static readonly IDictionary<string, string> Danish = Texts.Translations;

        internal static void Register() { /* Resources are loaded by Texts. */ }

        internal static void Retranslate(Control control)
        {
            if (!(control is TextBoxBase) && !(control is TreeView) && !(control is ComboBox))
            {
                string key = Danish.ContainsKey(control.Text) ? control.Text : null;
                if (key == null)
                    foreach (var pair in Danish)
                        if (string.Equals(pair.Value, control.Text, StringComparison.Ordinal)) { key = pair.Key; break; }
                if (key != null) control.Text = Texts.T(key);
            }
            foreach (Control child in control.Controls) Retranslate(child);
        }
    }
}
