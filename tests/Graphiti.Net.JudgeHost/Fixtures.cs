namespace Graphiti.JudgeHost.Units;

using Graphiti.Internal;
using Graphiti.JudgeHost.Core;

/// <summary>
/// The five custom types of <c>port/judge/host-python/fixtures.py</c>, which flow cases name with
/// <c>{"$ref": "fixtures:Name"}</c> (RULES.md R22). Each one is built through the public
/// <see cref="GraphTypeDefinition"/> with the field names, descriptions, and docstrings of the Python class, because
/// each of them reaches prompt text and schema text.
/// </summary>
internal static class FixturesBindings
{
    internal static readonly GraphTypeDescriptor Person = GraphTypeDefinition.Create("Person", "A human being who is named in the text.")
        .AddString("occupation", "The job or profession of the person")
        .AddString("home_city", "The city where the person lives")
        .ToDescriptor();

    internal static readonly GraphTypeDescriptor Product = GraphTypeDefinition.Create("Product", "A commercial product or a product line that can be bought.")
        .AddString("brand", "The brand that makes the product")
        .AddString("category", "The kind of product, for example shoes")
        .ToDescriptor();

    internal static readonly GraphTypeDescriptor Company = GraphTypeDefinition.Create("Company", "A business organization.")
        .AddString("industry", "The industry of the company")
        .ToDescriptor();

    internal static readonly GraphTypeDescriptor Likes = GraphTypeDefinition.Create("Likes", "A person likes or prefers a product.")
        .AddString("strength", "How strong the preference is: weak, medium or strong")
        .ToDescriptor();

    internal static readonly GraphTypeDescriptor WorksAt = GraphTypeDefinition.Create("WorksAt", "A person is employed by a company.")
        .AddString("role", "The job title of the person at the company")
        .ToDescriptor();

    internal static void Register(BindingTable table) =>
        table.Registry
            .AddRef("fixtures:Person", Person)
            .AddRef("fixtures:Product", Product)
            .AddRef("fixtures:Company", Company)
            .AddRef("fixtures:Likes", Likes)
            .AddRef("fixtures:WorksAt", WorksAt);
}
