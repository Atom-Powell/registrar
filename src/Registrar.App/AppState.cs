using Registrar.Validation;

namespace Registrar.App;

// What the open window needs to remember. A bag of values with no rules: every decision
// about validity and output belongs to Registrar.Core and Registrar.Pipeline, and a
// condition that lived here would be one the fast suite cannot reach.
public sealed class AppState
{
    public AppState(EngineContext engine, string repoRoot)
    {
        Engine = engine;
        RepoRoot = repoRoot;
    }

    public EngineContext Engine { get; }

    // The folder that holds schemas/ and config/, resolved once at startup. The health
    // check needs it; nothing else in the window should.
    public string RepoRoot { get; }
}
