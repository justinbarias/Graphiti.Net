// This file is in Core/ by the decision of the lead, but its class is a unit class: Bindings.Load calls Register only
// for a class in the namespace of the unit files.
namespace Graphiti.JudgeHost.Units;

using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// The <c>$model</c> paths <c>numpy:array</c> and <c>numpy:zeros</c>, which the cases of <c>ingestion/delta.py</c> pass
/// as a float32 matrix. The port holds a float32 matrix as a <c>float[,]</c> (RULES.md R18).
/// </summary>
internal static class NumpyBindings
{
    internal static void Register(BindingTable table)
    {
        // np.array(object=rows, dtype='float32') for a two-dimensional list of numbers.
        table.Registry.AddModel("numpy:array", data =>
        {
            if (!data.TryGetValue("dtype", out var dtype) || dtype is not "float32")
            {
                throw new JudgeHostErrorException("numpy:array: the host builds only a float32 matrix.");
            }

            var rows = Py.AsList(data["object"]).Select(Py.AsList).ToList();
            var width = rows.Count == 0 ? 0 : rows[0].Count;
            if (rows.Count == 0 || rows.Any(row => row.Count != width))
            {
                throw new JudgeHostErrorException("numpy:array: the host builds only a two-dimensional matrix.");
            }

            var matrix = new float[rows.Count, width];
            for (var row = 0; row < rows.Count; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    matrix[row, column] = (float)(rows[row][column] is long l ? l : Py.AsDouble(rows[row][column]));
                }
            }

            return matrix;
        });

        // np.zeros(shape=[rows, columns]). numpy gives float64 zeros. Each value is 0, so a float[,] holds them.
        table.Registry.AddModel("numpy:zeros", data =>
        {
            var shape = Py.AsList(data["shape"]);
            return shape.Count == 2
                ? new float[Py.AsLong(shape[0]), Py.AsLong(shape[1])]
                : throw new JudgeHostErrorException("numpy:zeros: the host builds only a two-dimensional matrix.");
        });
    }
}
