using Registrar.Cli;

namespace Registrar.Tests.Cli;

public class AgrPickerTests
{
    [Fact]
    public void Candidates_lists_recent_first_then_folder_dropping_missing_and_dupes()
    {
        var dir = TempDir.Create();
        var a = Path.Combine(dir, "a.json"); File.WriteAllText(a, "{}");
        var b = Path.Combine(dir, "b.json"); File.WriteAllText(b, "{}");
        var missing = Path.Combine(dir, "gone.json"); // never created

        var candidates = AgrPicker.Candidates(dir, new[] { b, missing });

        Assert.Equal(b, candidates[0]);                 // recent, exists -> first
        Assert.Contains(a, candidates);                 // folder file included
        Assert.DoesNotContain(missing, candidates);     // missing recent dropped
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
