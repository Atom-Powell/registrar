using System.IO;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Pipeline;
using Xunit;

namespace Registrar.App.Tests;

// Rendered through Home, the call site, never through a child alone: a component test hands
// its parameters in through the test framework's builder, so it proves the component and
// never the markup that feeds it (GUI-7 shipped a permanent red error that way).
public class HomePageTests : TestContext
{
    private readonly FakeOperatorHost host = new();
    private readonly FakeAgrRunner fake = new();
    private readonly string settingsPath = Path.Combine(TempDir.Create(), "settings.json");

    private static string Fixture(string name)
        => Path.Combine(AppServicesTests.RepoRoot(), "fixtures", name);

    private static string FolderWith(params string[] fixtures)
    {
        var dir = TempDir.Create();
        foreach (var f in fixtures) File.Copy(Fixture(f), Path.Combine(dir, f));
        return dir;
    }

    private IRenderedComponent<Home> Render(string? folder = null, string? registry = null, bool useFake = false)
    {
        var store = new SettingsStore(settingsPath);
        var seed = new MenuSettings();
        if (folder is not null) seed = seed.WithFolder(folder);
        if (registry is not null) seed = seed.WithRegistry(registry);
        store.Save(seed);

        Services.AddSingleton(new AppState(AppServicesTests.LoadEngine(), AppServicesTests.RepoRoot()));
        Services.AddSingleton<IOperatorHost>(host);
        Services.AddSingleton(store);
        if (useFake) Services.AddSingleton<IAgrRunner>(fake);
        else Services.AddSingleton<IAgrRunner>(new AgrRunner(new SystemClock()));
        return RenderComponent<Home>();
    }

    [Fact]
    public void It_lists_the_json_files_in_the_remembered_folder_and_selects_the_first()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json", "agr-sample-aup-draft.json"));

        Assert.Equal(2, cut.FindAll("label.file").Count);
        Assert.Single(cut.FindAll("label.file input[checked]"));
    }

    [Fact]
    public void It_says_when_the_folder_has_no_files()
    {
        var cut = Render(TempDir.Create());

        Assert.Empty(cut.FindAll("label.file"));
        Assert.Contains("No .json files", cut.Find(".notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_and_Build_are_disabled_until_a_file_is_selected()
    {
        var cut = Render(TempDir.Create());

        Assert.True(cut.Find("button.check").HasAttribute("disabled"));
        Assert.True(cut.Find("button.build").HasAttribute("disabled"));
    }

    [Fact]
    public void It_shows_the_declared_mode_in_plain_words_and_offers_no_mode_control()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        Assert.Contains("Draft: a working copy", cut.Find(".mode").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("select"));
    }

    [Fact]
    public void Check_shows_a_pass_banner_and_no_document_button()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("PASS", cut.Find(".banner").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("button.open-document"));
        Assert.NotEmpty(cut.FindAll("button.open-report"));
    }

    [Fact]
    public void Build_produces_the_document_and_the_open_button_opens_it()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        var cut = Render(folder);

        cut.Find("button.build").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.open-document")));

        cut.Find("button.open-document").Click();

        // The click is dispatched on the renderer while the Run handler's tail may still be
        // draining, so the effect can land a moment after Click returns.
        cut.WaitForAssertion(() => Assert.Single(host.Opened));
        var opened = Assert.Single(host.Opened);
        Assert.EndsWith(".docx", opened, StringComparison.Ordinal);
        Assert.True(File.Exists(opened));
    }

    [Fact]
    public void A_failing_file_shows_the_fix_instructions_and_copies_them_for_the_Governor()
    {
        var cut = Render(FolderWith("agr-sample-ds-invalid-schema.json"));

        cut.Find("button.build").Click();
        cut.WaitForAssertion(() =>
            Assert.Contains("FAIL", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Contains("fix:", cut.Find(".report").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button.open-document"));

        cut.Find("button.copy-failures").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Validation failures for", host.CopiedText, StringComparison.Ordinal));
        cut.WaitForAssertion(() =>
            Assert.Contains("Copied", cut.Find(".notice").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void A_usage_error_shows_its_text_and_no_report_buttons()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: registry file not found: x.csv", folder);
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("registry file not found", cut.Find(".result .error").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".banner"));
        Assert.Empty(cut.FindAll("button.open-report"));
        Assert.Empty(cut.FindAll("button.copy-failures"));
    }

    [Fact]
    public void A_runner_that_throws_is_reported_beside_the_controls_not_allowed_to_escape()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Throws = new InvalidOperationException("boom");
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("boom", cut.Find(".problem").TextContent, StringComparison.Ordinal));
        Assert.False(cut.Find("button.check").HasAttribute("disabled"));
    }

    [Fact]
    public void A_locked_clipboard_is_reported_not_thrown()
    {
        var cut = Render(FolderWith("agr-sample-ds-invalid-schema.json"));
        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.copy-failures")));

        host.CopyFails = new InvalidOperationException("clipboard is in use");
        cut.Find("button.copy-failures").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("clipboard is in use", cut.Find(".problem").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Choosing_a_folder_lists_its_files_and_remembers_the_choice()
    {
        var cut = Render(TempDir.Create());
        var chosen = FolderWith("agr-sample-ds-draft.json");
        host.NextFolder = chosen;

        cut.Find("button.choose-folder").Click();

        Assert.Single(cut.FindAll("label.file"));
        Assert.Equal(chosen, new SettingsStore(settingsPath).Load().LastFolder);
    }

    [Fact]
    public void Cancelling_the_folder_picker_changes_nothing()
    {
        var original = FolderWith("agr-sample-ds-draft.json");
        var cut = Render(original);
        host.NextFolder = null;

        cut.Find("button.choose-folder").Click();

        Assert.Single(cut.FindAll("label.file"));
        Assert.Equal(original, new SettingsStore(settingsPath).Load().LastFolder);
    }

    [Fact]
    public void Adding_a_file_selects_it()
    {
        var cut = Render(TempDir.Create());
        var elsewhere = FolderWith("agr-sample-aup-draft.json");
        host.NextAgrFile = Path.Combine(elsewhere, "agr-sample-aup-draft.json");

        cut.Find("button.add-file").Click();

        Assert.Single(cut.FindAll("label.file"));
        Assert.False(cut.Find("button.check").HasAttribute("disabled"));
    }

    [Fact]
    public void The_registry_can_be_chosen_remembered_and_cleared()
    {
        var cut = Render(TempDir.Create());
        host.NextRegistryFile = Fixture("registry-sample.csv");

        cut.Find("button.choose-registry").Click();

        Assert.Equal(Fixture("registry-sample.csv"), new SettingsStore(settingsPath).Load().RegistryPath);
        Assert.Contains("registry-sample.csv", cut.Markup, StringComparison.Ordinal);

        cut.Find("button.clear-registry").Click();

        Assert.Null(new SettingsStore(settingsPath).Load().RegistryPath);
    }

    [Fact]
    public void No_registry_is_stated_plainly()
    {
        var cut = Render(TempDir.Create());

        Assert.Contains("No registry chosen", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Without one, the registry check is skipped in a draft and fails a promotion build.",
            cut.Find(".hint").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void After_a_build_the_tools_own_output_files_are_not_offered_as_AGRs()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        cut.Find("button.build").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.open-document")));

        var labels = cut.FindAll("label.file").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "agr-sample-ds-draft.json" }, labels);
    }

    [Fact]
    public void The_health_check_runs_and_shows_one_line_per_check_with_a_verdict()
    {
        var cut = Render(TempDir.Create());

        cut.Find("button.health").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".health-output .line")));
        var text = cut.Find(".health-output").TextContent;
        Assert.Contains("config load", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--registry", text, StringComparison.Ordinal);
        Assert.Matches("Healthy|Not healthy", text);
    }

    [Fact]
    public void The_chosen_registry_reaches_the_runner()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: x", folder);
        var cut = Render(folder, Fixture("registry-sample.csv"), useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() => Assert.Single(fake.Calls));
        Assert.EndsWith(":" + Fixture("registry-sample.csv"), fake.Calls[0], StringComparison.Ordinal);
    }

    [Fact]
    public void No_registry_reaches_the_runner_as_empty()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: x", folder);
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() => Assert.Single(fake.Calls));
        Assert.EndsWith(":", fake.Calls[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_inputs_are_locked_during_a_run_and_the_result_lands_under_the_selected_file()
    {
        var folder = FolderWith("agr-sample-ds-draft.json", "agr-sample-aup-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: held result", folder);
        fake.Gate = new System.Threading.ManualResetEventSlim(false);
        var cut = Render(folder, Fixture("registry-sample.csv"), useFake: true);
        var firstSelected = cut.Find("label.file input[checked]").ParentElement!.TextContent.Trim();

        cut.Find("button.check").Click();
        try
        {
            cut.WaitForAssertion(() => Assert.True(cut.Find("button.check").HasAttribute("disabled")));
            Assert.All(cut.FindAll("label.file input"), i => Assert.True(i.HasAttribute("disabled")));
            foreach (var sel in new[] { "button.choose-folder", "button.choose-registry", "button.clear-registry", "button.add-file", "button.build" })
                Assert.True(cut.Find(sel).HasAttribute("disabled"), sel);
        }
        finally
        {
            fake.Gate.Set();
        }

        cut.WaitForAssertion(() =>
            Assert.Contains("held result", cut.Find(".result .error").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.check").HasAttribute("disabled")));
        Assert.All(cut.FindAll("label.file input"), i => Assert.False(i.HasAttribute("disabled")));
        foreach (var sel in new[] { "button.choose-folder", "button.choose-registry", "button.clear-registry", "button.add-file" })
            Assert.False(cut.Find(sel).HasAttribute("disabled"), sel);
        Assert.Equal(firstSelected, cut.Find("label.file input[checked]").ParentElement!.TextContent.Trim());
    }

    [Fact]
    public void With_no_folder_the_empty_list_asks_for_a_folder()
    {
        var cut = Render();

        Assert.Contains("Choose a working folder to see its files, or add a file.",
            cut.Find(".notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_shows_the_ORG_mark_beside_the_title()
    {
        var cut = Render(TempDir.Create());

        var mark = cut.Find("header.brand img.brand-mark");
        Assert.Equal("ORG", mark.GetAttribute("alt"));
        Assert.Contains("org-logo.png", mark.GetAttribute("src"), StringComparison.Ordinal);
        Assert.Equal("The Registrar", cut.Find("header.brand h1").TextContent.Trim());
    }

    [Fact]
    public void A_pass_banner_carries_a_pass_icon_and_the_word()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.Contains("PASS", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Equal("pass", cut.Find(".banner").GetAttribute("data-status"));
        Assert.NotNull(cut.Find(".banner svg.icon[data-icon='pass']"));
        Assert.Equal("true", cut.Find(".banner svg.icon").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void A_warning_result_uses_a_different_icon_shape_than_pass_and_fail()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft-warn.json"));

        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.Contains("WARNINGS", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Equal("warn", cut.Find(".banner").GetAttribute("data-status"));
        Assert.NotNull(cut.Find(".banner svg.icon[data-icon='warn']"));
        Assert.Empty(cut.FindAll("button.copy-failures"));
    }

    [Fact]
    public void A_fail_banner_carries_a_fail_icon_and_the_copy_button_has_a_clipboard_icon()
    {
        var cut = Render(FolderWith("agr-sample-ds-invalid-schema.json"));

        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.Contains("FAIL", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Equal("fail", cut.Find(".banner").GetAttribute("data-status"));
        Assert.NotNull(cut.Find(".banner svg.icon[data-icon='fail']"));
        Assert.NotNull(cut.Find("button.copy-failures svg.icon[data-icon='clipboard']"));
        Assert.Contains("Copy failures for the Governor", cut.Find("button.copy-failures").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_start_with_an_alert_icon()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: registry file not found: x.csv", folder);
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".result .error svg.icon[data-icon='alert']")));
    }
}
