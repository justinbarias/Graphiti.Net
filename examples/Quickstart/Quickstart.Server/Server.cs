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

    private const string Instructions = """
        You answer questions about the Online Safety Act 2021 (Cth).
        Answer only from the results of the tool search_legislation. Call the tool before you answer, and call it again with other words if the first results do not help.
        The result has facts and the text of the sections that state them. Answer from the facts and from the section texts. Quote or restate the words of the section.
        Cite the section id (for example "Online Safety Act 2021 s 36") after each statement. Cite only a section id that is in the tool results of this conversation. A statement with no source in the tool results is not allowed.
        If the graph has no answer, say so. Do not use what you know from other sources.
        Keep each answer short and plain. Each fact has validity dates. Mention them only if they matter.
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
                Instructions = Instructions,
                Tools = [AIFunctionFactory.Create(search.SearchLegislationAsync, name: "search_legislation")],
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });

        app.UseCors();
        app.MapAGUIServer("/", agent);
        await app.RunAsync();
    }
}
