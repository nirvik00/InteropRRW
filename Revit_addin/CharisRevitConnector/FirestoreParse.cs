using System.Globalization;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace CharisRevitConnector;

/// <summary>
/// Helpers for reading Firestore document data (plain dictionaries/lists) into
/// the values the handlers need. Runs on the listener background thread; only
/// constructs plain data + XYZ (no Revit API context required).
/// </summary>
internal static class FirestoreParse
{
    public static object? Get(IReadOnlyDictionary<string, object> data, string key) =>
        data.TryGetValue(key, out object? v) ? v : null;

    /// <summary>Firestore numbers may be int64 or double; coerce both to double.</summary>
    public static double ToDouble(object? value) => value switch
    {
        null => 0.0,
        double d => d,
        long l => l,
        int i => i,
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    };

    public static XYZ? ReadVec3(object? value)
    {
        if (value is IReadOnlyDictionary<string, object> m)
            return new XYZ(ToDouble(Get(m, "x")), ToDouble(Get(m, "y")), ToDouble(Get(m, "z")));
        return null;
    }

    public static List<XYZ>? ReadPolyline(IReadOnlyDictionary<string, object> data, string key)
    {
        if (Get(data, key) is not IEnumerable<object> arr)
            return null;

        var points = new List<XYZ>();
        foreach (object element in arr)
        {
            if (ReadVec3(element) is XYZ p)
                points.Add(p);
        }
        return points;
    }

    public static (XYZ Start, XYZ End)? ReadLine(IReadOnlyDictionary<string, object> data, string key)
    {
        if (Get(data, key) is not IEnumerable<object> arr)
            return null;

        var points = new List<XYZ>();
        foreach (object element in arr)
        {
            if (ReadVec3(element) is XYZ p)
                points.Add(p);
        }
        return points.Count >= 2 ? (points[0], points[1]) : null;
    }
}
