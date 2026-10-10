using Registrar.Cli;

namespace Registrar.Tests;

public class DoctorCommandTests
{
    [Fact]
    public void Healthy_repo_doctor_exits_0()
    {
        var sw = new StringWriter();
        var code = DoctorCommand.Run(sw, sw);
        Assert.Equal(0, code);
        Assert.Contains("doctor: healthy", sw.ToString());
    }

    [Fact]
    public void Bad_registry_makes_doctor_exit_1()
    {
        var sw = new StringWriter();
        var missing = Path.Combine(Path.GetTempPath(), "registrar-doctor-cli-no-such.csv");
        var code = DoctorCommand.Run(sw, sw, missing);
        Assert.Equal(1, code);
    }
}
