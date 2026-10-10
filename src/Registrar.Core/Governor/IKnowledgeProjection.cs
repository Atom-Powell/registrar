namespace Registrar.Core.Governor;

// One generated knowledge file for one Copilot Studio agent role. Implementations must
// be pure: no IO, no clock, no ambient state. The staleness guard in the test suite
// regenerates every projection and byte-compares it against the committed file, so any
// nondeterminism fails the build.
public interface IKnowledgeProjection
{
    string FileName { get; }
    string Render(KnowledgeSource source);
}
