using System.Runtime.InteropServices;
using Rhino.PlugIns;

namespace SixCharis.RhinoReviewInterop;

[Guid("348A688E-A1FD-4BD5-9A45-48B3E5364333")]
public sealed class RhinoReviewInteropPlugin : PlugIn
{
    public RhinoReviewInteropPlugin()
    {
        Instance = this;
    }

    public static RhinoReviewInteropPlugin? Instance { get; private set; }
}
