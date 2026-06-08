using Rhino;
using Rhino.PlugIns;
using System;
using System.Runtime.InteropServices;

namespace InteropRhino
{
    public class InteropRhinoPlugin : Rhino.PlugIns.PlugIn
    {
        public InteropRhinoPlugin()
        {
            Instance = this;
            RhinoApp.WriteLine("InteropRhino plugin constructor called.");
        }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            try
            {
                RhinoApp.WriteLine("InteropRhino OnLoad called.");
                return LoadReturnCode.Success;
            }
            catch (Exception ex)
            {
                errorMessage = ex.ToString();
                return LoadReturnCode.ErrorShowDialog;
            }
        }

        public static InteropRhinoPlugin Instance { get; private set; }
    }
}