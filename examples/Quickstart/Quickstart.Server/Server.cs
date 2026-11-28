namespace Quickstart;

using Graphiti;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

/// <summary>The AG-UI HTTP server: one agent that uses the graph search as its tool.</summary>
internal static class Server
{
    private const string WebOrigin = "http://localhost:3000";
    private const string DefaultUrl = "http://localhost:8000";

    private const string BaseInstructions = """
        You are the Australian Online Safety Legislative Agent. You answer questions about the Online Safety Act 2021 (Cth) and about the Online Safety Amendment (Digital Duty of Care) Bill 2026, which amends the Act.
        Answer only from the results of the tool search_legislation. Call the tool before you answer, and call it again with other words if the first results do not help.
        The result has facts and the text of the sections that state them. Answer from the facts and from the section texts. Quote or restate the words of the section.
        Cite the section id (for example "Online Safety Act 2021 s 36") after each statement. Cite it as the tool gives it, with its note in brackets if it has one (for example "Online Safety Act 2021 s 26 (inserted by the Digital Duty of Care Bill 2026, Sch 2 item 8)"). Cite only a section id that is in the tool results of this conversation. A statement with no source in the tool results is not allowed.
        The law changes over time. Each fact has the dates valid_at and invalid_at. A fact with an invalid_at date stops on that date: say so, give the date, and say what replaces it if the results show it. A fact of the Bill starts on its valid_at date. If the question gives no date, give the rule of the Act and then the change that the Bill makes, with its date.
        If the graph has no answer, say so. Do not use what you know from other sources.
        Keep each answer short and plain.
        """;

    // The agent gets this note only while the day of the Royal Assent is the placeholder of the example.
    private const string PlaceholderNote = """

        The Bill passed both Houses of the Parliament, but it had no Royal Assent when the graph was made. Each date of the Bill counts from a placeholder for the day of assent. Say this one time in an answer that uses a date of the Bill.
        """;

    /// <summary>Starts the server and runs until the process stops.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="args">The arguments of the command.</param>
    /// <returns>A task that ends when the server stops.</returns>
    public static async Task RunAsync(Settings settings, string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // The host filter of ASP.NET Core: the server answers only for these host names.
        builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1";
        builder.Configuration["urls"] = Environment.GetEnvironmentVariable("QUICKSTART_URL") ?? DefaultUrl;
        builder.Services.AddQuickstartGraphiti(settings);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<SearchTool>();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(WebOrigin).AllowAnyHeader().AllowAnyMethod()));
        builder.Services.AddAGUIServer();

        var app = builder.Build();
        await app.Services.GetRequiredService<IGraphiti>().BuildIndicesAndConstraintsAsync();

        var search = app.Services.GetRequiredService<SearchTool>();
        // The model gpt-6-luna does not accept function tools together with reasoning in the Chat Completions API, so the
        // agent turns the reasoning effort off.
        AIAgent agent = GraphitiSetup.CreateChatClient(settings).AsAIAgent(new ChatClientAgentOptions
        {
            Name = "legislation_agent",
            ChatOptions = new ChatOptions
            {
                Instructions = BaseInstructions + (settings.BillAssentIsPlaceholder ? PlaceholderNote : string.Empty),
                Tools = [AIFunctionFactory.Create(search.SearchLegislationAsync, name: "search_legislation")],
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });

        app.UseCors();
        app.MapAGUIServer("/", agent);
        await app.RunAsync();
    }
}
