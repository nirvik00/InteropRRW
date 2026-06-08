using System;
using Rhino;

namespace InteropRhino
{
    public class InteropRhinoPlugin : Rhino.PlugIns.PlugIn
    {
        public InteropRhinoPlugin()
        {
            Instance = this;
        }

        public static InteropRhinoPlugin Instance { get; private set; }

    }
}