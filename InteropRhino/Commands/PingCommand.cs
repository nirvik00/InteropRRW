using Rhino;
using Rhino.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InteropRhino.Commands
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
