using Rhino;
using Rhino.Commands;

namespace InteropRhino.Commands
{
public sealed class RhinoReviewPushCommand : Command
{
    public override string EnglishName => "RhinoReviewPush";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        return PushRhinoReviewDataToFirestoreCommand.PushInBackground(doc);
    }
}

}

