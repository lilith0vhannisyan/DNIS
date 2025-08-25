// See https://aka.ms/new-console-template for more information
using System;
using System.Net.Http;
using ClocktowerDemo.Application;
using ClocktowerDemo.Configuration;
using ClocktowerDemo.Domain;
using ClocktowerDemo.Services.AI;
using ClocktowerDemo.Services.Ontology;
using ClocktowerDemo.Services.Politeness;

AppCfg.PrintResolved();

var http = new HttpClient { Timeout = AppCfg.HttpTimeout };
var state = new GameState();
var onto = new OntologyService(AppCfg.OntologyPath);

// Politeness
IPolitenessDetector polite = AppCfg.PolitenessMode switch
{
    "http" => new HttpPolitenessDetector(http, AppCfg.PolitenessUrl),
    "heuristic" => new HeuristicPolitenessDetector(),
    // Map any legacy "python" value to http (or heuristic) so it won’t crash
    "python" => new HttpPolitenessDetector(http, AppCfg.PolitenessUrl),
    _ => new HeuristicPolitenessDetector()
};
// AI provider (OpenRouter or local Mistral)
IAIProvider ai = AppCfg.AiMode == "mistral_local"
    ? new PythonMistralProvider()
    : new OpenRouterAIProvider(http, state.ChatHistory);

// Run
using var engine = new GameEngine(onto, ai, polite, state);
await engine.RunAsync();

// For checkin if paths work
ClocktowerDemo.Configuration.AppCfg.PrintResolved();