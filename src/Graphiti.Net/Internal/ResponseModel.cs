namespace Graphiti.Internal;

using Graphiti.PyCompat;

/// <summary>A response model that code passes as a value, where Python passes a <c>type[BaseModel]</c> (R7).</summary>
internal interface IResponseModel
{
    /// <summary>Gets the Python class name, which is <c>response_model.__name__</c>.</summary>
    string PythonName { get; }

    /// <summary>Gets <c>response_model.model_json_schema()</c>.</summary>
    PydanticSchema ModelJsonSchema { get; }
}

/// <summary>A built-in response model: one object for each Python class, so that <c>a is b</c> is a reference test.</summary>
internal sealed class BuiltinResponseModel(string pythonName, PydanticSchema modelJsonSchema) : IResponseModel
{
    /// <inheritdoc />
    public string PythonName { get; } = pythonName;

    /// <inheritdoc />
    public PydanticSchema ModelJsonSchema { get; } = modelJsonSchema;
}
