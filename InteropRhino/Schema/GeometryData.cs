using System.Collections.Generic;

namespace InteropRhino.Schema
{

    public sealed class PointData
    {
        public double X { get; init; } = 0.0;
        public double Y { get; init; } = 0.0;
        public double Z { get; init; } = 0.0;
    }

    public sealed class LineData
    {
        public PointData Start { get; init;  } 
        public PointData End { get; init;  }
    }

    public sealed class XandyData
    {
        public double B { get; init;  }
        public double H { get; init;  }
    }
}