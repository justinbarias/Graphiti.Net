// Ported from graphiti_core/utils/datetime_utils.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Utils;

using System.Diagnostics;
using Graphiti.Internal;
using Graphiti.PyCompat;

internal static class DatetimeUtils
{
    /// <summary>Returns the current UTC datetime with timezone information.</summary>
    internal static PyDateTime UtcNow() => PyDateTime.UtcNow(PortServices.Time);

    /// <summary>
    /// Ensures a datetime is timezone-aware and in UTC.
    /// If the datetime is naive (no timezone), assumes it's in UTC.
    /// If the datetime has a different timezone, converts it to UTC.
    /// Returns None if input is None.
    /// </summary>
    internal static PyDateTime? EnsureUtc(PyDateTime? dt)
    {
        if (dt is not { } value)
        {
            return null;
        }

        if (value.IsNaive)
        {
            // If datetime is naive, assume it's UTC
            return value.ReplaceUtc();
        }
        else if (!value.IsUtc)
        {
            // If datetime has a different timezone, convert it to UTC
            return value.ToUtc();
        }

        return value;
    }

    // PORT: TYPE_HINTS.md gives Unknown for the parameter and the result. The function walks open data (R6).
    internal static object? ConvertDatetimesToStrings(object? obj)
    {
        if (obj is OrderedDictionary<string, object?> stringKeyed)
        {
            OrderedDictionary<string, object?> result = new(StringComparer.Ordinal);
            foreach (var (k, v) in stringKeyed)
            {
                result[k] = ConvertDatetimesToStrings(v);
            }

            return result;
        }
        else if (obj is PyDict dict)
        {
            PyDict result = new();
            foreach (var (k, v) in dict)
            {
                result[k] = ConvertDatetimesToStrings(v);
            }

            return result;
        }
        else if (obj is List<object?> list)
        {
            return list.Select(ConvertDatetimesToStrings).ToList();
        }
        else if (obj is PyTuple tuple)
        {
            return PyTuple.From(tuple.Select(ConvertDatetimesToStrings));
        }
        else if (obj is PyDateTime dt)
        {
            // Normalize to UTC before serializing. The resulting ISO strings are
            // compared lexicographically by drivers that store datetimes as strings
            // (e.g. FalkorDB), which is only correct when all offsets are identical.
            var utcDt = EnsureUtc(dt);
            Debug.Assert(utcDt is not null, "utc_dt is not None");
            return utcDt?.IsoFormat();
        }
        else
        {
            return obj;
        }
    }
}
