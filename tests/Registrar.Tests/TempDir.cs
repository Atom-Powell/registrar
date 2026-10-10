// Spelled out rather than left to ImplicitUsings. This file is compiled into both test
// projects, and the WPF one (net8.0-windows, UseWPF) does not implicitly import System.IO,
// so relying on the implicit set builds in one project and fails in the other.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Registrar.TestSupport;

// Every temp directory a test creates goes through here, for two reasons.
//
// The first is that the suite leaked them. A measured 18,450 directories and 10,591
// rendered DOCX files accumulated under the OS temp path in three days, because most
// call sites created a directory and nothing ever removed it. The correct pattern was
// already in the codebase: a few of the newer GUI tests cleaned up in try/finally. It
// simply was not applied everywhere, and an inconsistently applied convention is the
// kind of thing that only gets fixed once someone counts. See issue #69.
//
// The second is identification. Directory.CreateTempSubdirectory() with no prefix
// produces an opaque 8.3 name, indistinguishable from any other application's scratch
// space, so past leftovers could only be found by sniffing their contents and guessing.
// Everything created here carries Prefix, which makes both the sweep below and
// scripts/clean-test-temp.ps1 name-based and certain about what they are deleting.
internal static class TempDir
{
    // scripts/clean-test-temp.ps1 matches on this exact string. Changing it here without
    // changing it there leaves that script deleting nothing, silently.
    public const string Prefix = "registrar-test-";

    // A leftover younger than this may belong to a suite running right now, in another
    // process, on the same machine. Deleting a live directory out from under a parallel
    // run would be a worse failure than leaving one on disk for an hour, so the sweep is
    // deliberately conservative about what it considers abandoned.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private static readonly ConcurrentBag<string> Created = new();
    private static int swept;

    static TempDir() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteThisRun();

    // A directory that will be removed when this run ends.
    public static string Create()
    {
        SweepOnce();

        var path = Directory.CreateTempSubdirectory(Prefix).FullName;
        Created.Add(path);
        return path;
    }

    // A file path inside a managed directory, for the tests that want a path to a file
    // rather than a directory. Those previously built one straight under the temp root
    // from a GUID, which left the file itself behind on every run; putting it inside a
    // managed directory means the existing cleanup covers it with no separate file
    // tracking.
    public static string File(string fileName) => Path.Combine(Create(), fileName);

    // Best-effort throughout. A DOCX still held open by a reader, or a directory the OS
    // has not released yet, must never turn a passing test into a failing one: that would
    // trade a disk-space problem for a flakiness problem, which is a bad trade. Anything
    // missed here is caught by a later run's sweep.
    private static void DeleteThisRun()
    {
        foreach (var path in Created)
            TryDelete(path);
    }

    // Runs once per process, before the first directory is handed out. This is the real
    // guarantee: the ProcessExit handler above does not run if the test host is killed,
    // and .NET caps how long it may take anyway, so cleanup cannot depend on it alone.
    // Sweeping at startup means a run that dies badly is cleaned up by the next one.
    private static void SweepOnce()
    {
        if (Interlocked.Exchange(ref swept, 1) != 0)
            return;

        try
        {
            var cutoff = DateTime.UtcNow - StaleAfter;

            foreach (var path in Directory.EnumerateDirectories(Path.GetTempPath(), Prefix + "*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(path) < cutoff)
                        TryDelete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
