using System.Text.Json;
using System.Text.Json.Serialization;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using System;
using System.Collections.Generic;


using InteropRhino.Extraction;
using InteropRhino.Schema;
using System.Linq;
namespace InteropRhino.Extraction
{

    internal static class JsonOptions
    {
        public static readonly JsonSerializerOptions Pretty = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
        };
    }
}