using Rhino;
using Rhino.Commands;

using InteropRhino.Firebase;

namespace InteropRhino.Commands
{
    public sealed class RhinoReviewSyncCommand : Command
    {
        public override string EnglishName => "RhinoReviewSync";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var status = FirestoreAutoSyncService.Toggle(doc);
            var state = status.Enabled ? "enabled" : "disabled";
            RhinoApp.WriteLine($"Rhino Review Firestore auto-sync {state}.");
            return Result.Success;
        }
    }

}

