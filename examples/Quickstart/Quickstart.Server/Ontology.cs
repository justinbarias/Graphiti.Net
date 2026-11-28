namespace Quickstart;

using Graphiti;

/// <summary>
/// The ontology of the example: the entity types and the relationship types of the domain that the Online Safety Act
/// 2021 (Cth) regulates. It models what the rules are about (authorities, parties, services, material, instruments,
/// duties, and enforcement measures). It does not model the document: a section, a Part, a Division, and the Act are
/// not entities, and a cross-reference is not a fact. The place of a rule is provenance, and the episode holds it.
/// </summary>
internal static class Ontology
{
    /// <summary>The name of the default entity type of Graphiti. The example excludes it, so only typed entities stay.</summary>
    public const string DefaultEntityType = "Entity";

    private const string DefinitionField = "definition";
    private const string DefinitionText = "The meaning that the Act gives, in one or two sentences, in the words of the Act.";

    /// <summary>Gets the entity types. The order reaches the prompt text.</summary>
    public static IReadOnlyList<GraphTypeDefinition> EntityTypes { get; } =
    [
        GraphTypeDefinition.Create(
            "Authority",
            "An office, a body, or a court that holds a power or a function under the Act. Examples: the eSafety Commissioner, the Minister, the ACMA, the Federal Court."),
        GraphTypeDefinition.Create(
            "Party",
            "A kind of person that the Act protects or regulates. Examples: an Australian child, an Australian adult, an end-user, a parent or guardian, the provider of a social media service, a hosting service provider."),
        GraphTypeDefinition.Create(
            "OnlineService",
            "A kind of service that the Act regulates. Examples: a social media service, a relevant electronic service, a designated internet service, a hosting service, an internet search engine service, an app distribution service.")
            .AddString(DefinitionField, DefinitionText),
        GraphTypeDefinition.Create(
            "Material",
            "A kind of content that the Act regulates. Examples: cyber-bullying material targeted at an Australian child, cyber-abuse material targeted at an Australian adult, an intimate image, class 1 material, class 2 material.")
            .AddString(DefinitionField, DefinitionText),
        GraphTypeDefinition.Create(
            "Instrument",
            "A notice, a request, a code, a standard, a determination, or a set of rules that a person makes or gives under the Act. Examples: a removal notice, a blocking notice, an end-user notice, a link deletion notice, an industry code, an industry standard, the basic online safety expectations.")
            .AddString(DefinitionField, DefinitionText),
        GraphTypeDefinition.Create(
            "Duty",
            "Something that a party or an authority must do or must not do under the Act. Name it by its content in a few words. Examples: comply with a removal notice, not post an intimate image without consent, give a report on compliance with the basic online safety expectations."),
        GraphTypeDefinition.Create(
            "EnforcementMeasure",
            "A penalty, or another measure that follows a contravention. Examples: a civil penalty, a formal warning, an infringement notice, an enforceable undertaking, an injunction."),
    ];

    /// <summary>Gets the relationship types. The order reaches the prompt text.</summary>
    public static IReadOnlyList<GraphTypeDefinition> EdgeTypes { get; } =
    [
        GraphTypeDefinition.Create("MAY_ISSUE", "The authority has the power to make or give the instrument."),
        GraphTypeDefinition.Create("ISSUED_TO", "The instrument is given to, or applies to, this kind of party."),
        GraphTypeDefinition.Create("CONCERNS", "The instrument acts on this kind of material."),
        GraphTypeDefinition.Create("IMPOSES", "The instrument creates the duty.")
            .AddString("time_limit", "The time in which the party must do the duty, for example '24 hours'."),
        GraphTypeDefinition.Create("HAS_DUTY", "The party or the authority must do, or must not do, what the duty states."),
        GraphTypeDefinition.Create("ENFORCED_BY", "The enforcement measure follows a contravention of the duty.")
            .AddInteger("penalty_units", "The civil penalty in penalty units, if the Act states one."),
        GraphTypeDefinition.Create("MAY_APPLY", "The authority can use the enforcement measure."),
        GraphTypeDefinition.Create("APPLIES_TO", "The enforcement measure can be used against this kind of party."),
        GraphTypeDefinition.Create("PROVIDES", "The party supplies the online service."),
        GraphTypeDefinition.Create("PROVIDED_ON", "The material counts under the Act if it is provided on this kind of online service."),
        GraphTypeDefinition.Create("TARGETED_AT", "The material harms, or is aimed at, this kind of party."),
        GraphTypeDefinition.Create("MAY_COMPLAIN_ABOUT", "The party can make a complaint or an objection about the material."),
        GraphTypeDefinition.Create("HAS_POWER_OVER", "One authority appoints, directs, delegates to, reviews a decision of, or terminates the appointment of the other authority.")
            .AddString("power", "One of: appoints, directs, delegates to, reviews, terminates."),
    ];

    /// <summary>Gets the allowed relationship types for each pair of entity types.</summary>
    public static IReadOnlyList<EdgeTypeRule> EdgeTypeMap { get; } =
    [
        Rule("Authority", "Instrument", "MAY_ISSUE"),
        Rule("Instrument", "Party", "ISSUED_TO"),
        Rule("Instrument", "Material", "CONCERNS"),
        Rule("Instrument", "Duty", "IMPOSES"),
        Rule("Party", "Duty", "HAS_DUTY"),
        Rule("Authority", "Duty", "HAS_DUTY"),
        Rule("Duty", "EnforcementMeasure", "ENFORCED_BY"),
        Rule("Authority", "EnforcementMeasure", "MAY_APPLY"),
        Rule("EnforcementMeasure", "Party", "APPLIES_TO"),
        Rule("Party", "OnlineService", "PROVIDES"),
        Rule("Material", "OnlineService", "PROVIDED_ON"),
        Rule("Material", "Party", "TARGETED_AT"),
        Rule("Party", "Material", "MAY_COMPLAIN_ABOUT"),
        Rule("Authority", "Authority", "HAS_POWER_OVER"),
    ];

    /// <summary>Gets the extraction instructions: model the domain, and keep the document structure out of the graph.</summary>
    public static string ExtractionInstructions { get; } = """
        The text is one section of an Act of the Australian Parliament about online safety. Extract what the rule is about: who holds a power, who has a duty, which service or material the rule covers, which notice or other instrument it creates, and what follows a contravention.
        A section number is where a rule lives. It is not what the rule is about. Never extract a section, a subsection, a paragraph, a Part, a Division, a Subdivision, a Schedule, or the Act itself as an entity. Never extract a fact that only states where a provision is, or that one provision refers to another provision.
        If the text says "a notice under section 88" or the same in other words, use the name of the thing (for example "removal notice"), and not the section number.
        The Act has more than one scheme, and each scheme has notices of its own. The first line of the text, "Topic:", gives the scheme of the section. Name an instrument or a duty with its scheme, for example "removal notice for cyber-abuse material" and "comply with a removal notice for cyber-abuse material". Never use the bare name "removal notice" if the topic or the text gives the kind of material.
        Name each entity by its general kind in the singular, in the words of the Act (for example "removal notice", "social media service", "eSafety Commissioner"). Use one name for one thing: "the Commissioner" is "eSafety Commissioner".
        A text can start with a line "Status:". "Status: new law" means that the rule starts on the date in that line: write each fact with that start date. "Status: changed" means that the old words end on the date and the new words start on it: write the new rule as a fact with the start date, between the same two entities and with the same entity names that the old rule uses, and say what it replaces. For example: "From 26 November 2026 the civil penalty for not complying with a removal notice for cyber-abuse material is 6,000 penalty units, and no longer 500 penalty units".
        Write each fact as a full sentence that a reader understands without the section, with each condition, time limit, and penalty that the text states.
        """;

    /// <summary>Makes the options of one <c>AddEpisodeAsync</c> call with the ontology.</summary>
    /// <param name="groupId">The group id.</param>
    /// <returns>The options.</returns>
    public static AddEpisodeOptions Options(string groupId) => new()
    {
        GroupId = groupId,
        EntityTypes = EntityTypes,
        EdgeTypes = EdgeTypes,
        EdgeTypeMap = EdgeTypeMap,
        ExcludedEntityTypes = [DefaultEntityType],
        CustomExtractionInstructions = ExtractionInstructions,
    };

    private static EdgeTypeRule Rule(string source, string target, params string[] edgeTypes) =>
        new() { SourceType = source, TargetType = target, EdgeTypes = edgeTypes };
}
