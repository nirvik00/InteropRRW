using Rhino;
using Rhino.UI;
using System;

namespace InteropRhino.Commands
{ 
    internal static class CommandUi
    {
        public static void WriteLine(string message)
        {
            RhinoApp.InvokeOnUiThread((Action)(() => RhinoApp.WriteLine(message.Replace("\n", " "))));
        }

        public static void ShowTextDialog(string message, string title)
        {
            RhinoApp.InvokeOnUiThread((Action)(() => Dialogs.ShowTextDialog(message, title)));
        }
    }
}

