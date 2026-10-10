using Registrar.Cli;
using Xunit;

namespace Registrar.Tests.Cli;

public class EmitTokensCommandTests
{
    // Path.GetDirectoryName("justafile.css") returns "" and Directory.CreateDirectory("")
    // throws, so a bare filename used to fail with an unrelated ArgumentException instead
    // of writing to the current directory, which is what a user typing one expects. Run
    // in a temporary directory, set as the current directory for the duration, so the
    // test cannot leave an untracked file in the repo or race another test's cwd.
    [Fact]
    public void A_bare_filename_writes_to_the_current_directory()
    {
        var previousDirectory = Environment.CurrentDirectory;
        var tempDir = TempDir.Create();

        try
        {
            Environment.CurrentDirectory = tempDir;

            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = EmitTokensCommand.Run("justafile.css", stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(tempDir, "justafile.css")));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
