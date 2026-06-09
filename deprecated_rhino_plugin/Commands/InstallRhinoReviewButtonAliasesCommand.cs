using Rhino;
using Rhino.ApplicationSettings;
using Rhino.Commands;

namespace SixCharis.RhinoReviewInterop.Commands;

public sealed class InstallRhinoReviewButtonAliasesCommand : Command
{
    public override string EnglishName => "InstallRhinoReviewButtonAliases";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        CommandAliasList.SetMacro("RRSync", "! _RhinoReviewSync");
        CommandAliasList.SetMacro("RRPush", "! _RhinoReviewPush");

        RhinoApp.WriteLine("Installed Rhino Review aliases: RRSync -> RhinoReviewSync, RRPush -> RhinoReviewPush.");
        RhinoApp.WriteLine("Use these aliases or the same macros in custom Rhino toolbar buttons.");
        return Result.Success;
    }
}
