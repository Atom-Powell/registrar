namespace Registrar.Cli;

public interface IConsole
{
    void Write(string text);
    void WriteLine(string text);
    void WriteLine(string text, LineStyle style);
    string? ReadLine();
}

public sealed class SystemConsole : IConsole
{
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text) => Console.WriteLine(text);

    public void WriteLine(string text, LineStyle style)
    {
        var color = style switch
        {
            LineStyle.Pass => ConsoleColor.Green,
            LineStyle.Warn => ConsoleColor.Yellow,
            LineStyle.Fail => ConsoleColor.Red,
            _ => (ConsoleColor?)null
        };
        if (color is null) { Console.WriteLine(text); return; }
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color.Value;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    public string? ReadLine() => Console.ReadLine();
}
