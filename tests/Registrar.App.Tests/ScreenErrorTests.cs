using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Registrar.App.Components;
using Xunit;

namespace Registrar.App.Tests;

// The app-wide net the owner ruled in after GUI-8. An exception escaping a Blazor event
// handler in this WPF host is rethrown onto the WPF dispatcher and takes the process down,
// and the roadmap closes with no later GUI unit to inherit the fix.
//
// What is covered here is the half bUnit can reach: an ErrorBoundary wired exactly as
// Routes.razor wires it turns a throwing screen into readable fallback content instead of
// letting the exception escape. The other half, the DispatcherUnhandledException handler in
// App.OnStartup, is WPF application startup that bUnit has no host for, exactly like the
// startup config guard in MainWindow, and it is deliberately left uncovered rather than
// contorted into testability.
public class ScreenErrorTests : TestContext
{
    // A screen that fails the way a screen actually fails: on first render, before the
    // operator can do anything about it.
    private sealed class Exploding : ComponentBase
    {
        protected override void OnInitialized()
            => throw new InvalidOperationException("the section schema could not be read");
    }

    private static RenderFragment ChildThatThrows()
        => builder =>
        {
            builder.OpenComponent<Exploding>(0);
            builder.CloseComponent();
        };

    private static RenderFragment<Exception> Fallback()
        => error => builder =>
        {
            builder.OpenComponent<ScreenError>(0);
            builder.AddAttribute(1, nameof(ScreenError.Error), error);
            builder.CloseComponent();
        };

    [Fact]
    public void A_screen_that_throws_renders_the_fallback_rather_than_escaping()
    {
        var cut = RenderComponent<ErrorBoundary>(p => p
            .Add(c => c.ChildContent, ChildThatThrows())
            .Add(c => c.ErrorContent, Fallback()));

        // Rule 7: the operator is told what failed, in words, not shown a stack trace and
        // not shown nothing at all.
        Assert.Contains("the section schema could not be read",
            cut.Find(".screen-error .what").TextContent, StringComparison.Ordinal);

        // And there is a way out of it that does not involve restarting the app.
        Assert.NotNull(cut.Find("button.recover"));
    }

    // A silent catch would be worse than the crash, because at least a crash is noticed.
    // This pins the message as content, so a future edit cannot reduce the fallback to a
    // bare apology that names nothing.
    [Fact]
    public void The_fallback_never_reduces_to_a_message_that_names_nothing()
    {
        var cut = RenderComponent<ScreenError>(p => p
            .Add(c => c.Error, new InvalidOperationException("registry.csv is locked by another process")));

        Assert.Contains("registry.csv is locked by another process",
            cut.Markup, StringComparison.Ordinal);
    }
}
