using Rhino;
using Rhino.Commands;

namespace SixCharis
{

    public class PingCommand : Command
    {
        public override string EnglishName => "PingCommand";

        protected override Result RunCommand(
            RhinoDoc doc,
            RunMode mode)
        {
            RhinoApp.WriteLine("PING WORKS");
            return Result.Success;
        }
    }
}
