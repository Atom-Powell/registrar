using System.CommandLine;
using Registrar.Cli;
using Registrar.Core;

var agrArgument = new Argument<string>("agr.json")
{
    Description = "Path to the AGR JSON file to validate."
};

var registryOption = new Option<string?>("--registry")
{
    Description = "Path to a CSV export of the ID Registry (the PMC Registry sheet)."
};

var validate = new Command("validate", "Validate an AGR against agr.schema.json.");
validate.Arguments.Add(agrArgument);
validate.Options.Add(registryOption);
validate.SetAction(parseResult =>
{
    var agrPath = parseResult.GetValue(agrArgument)!;
    var registryPath = parseResult.GetValue(registryOption);
    return ValidateCommand.Run(agrPath, new SystemClock(), Console.Out, Console.Error, registryPath);
});

var buildAgrArgument = new Argument<string>("agr.json")
{
    Description = "Path to the AGR JSON file to build."
};

var buildModeOption = new Option<string?>("--mode")
{
    Description = "draft or promotion; must match the AGR mode."
};

var buildRegistryOption = new Option<string?>("--registry")
{
    Description = "Path to a CSV export of the ID Registry (the PMC Registry sheet)."
};

var build = new Command("build", "Validate an AGR and, on a pass, render the DOCX.");
build.Arguments.Add(buildAgrArgument);
build.Options.Add(buildModeOption);
build.Options.Add(buildRegistryOption);
build.SetAction(parseResult =>
{
    var agrPath = parseResult.GetValue(buildAgrArgument)!;
    var modeValue = parseResult.GetValue(buildModeOption);
    var registryPath = parseResult.GetValue(buildRegistryOption);
    return BuildCommand.Run(agrPath, new SystemClock(), Console.Out, Console.Error, modeValue, registryPath);
});

var menu = new Command("menu", "Interactive operator menu (validate/build with quality-of-life features).");
menu.SetAction(_ => MenuCommand.Run(
    new SystemConsole(), new OperatorShell(), new SystemClock(), SettingsStore.DefaultPath()));

var testFixtureOption = new Option<string?>("--fixture")
{
    Description = "Fixture basename to run (e.g. agr-sample-ds-draft)."
};
var testAllOption = new Option<bool>("--all")
{
    Description = "Run every fixture under /fixtures."
};
var testUpdateOption = new Option<bool>("--update")
{
    Description = "Regenerate the expected goldens instead of comparing against them."
};

var test = new Command("test", "Run fixtures through the engine and compare outputs to stored goldens.");
test.Options.Add(testFixtureOption);
test.Options.Add(testAllOption);
test.Options.Add(testUpdateOption);
test.SetAction(parseResult =>
{
    var fixture = parseResult.GetValue(testFixtureOption);
    var all = parseResult.GetValue(testAllOption);
    var update = parseResult.GetValue(testUpdateOption);
    return TestCommand.Run(fixture, all, update, Console.Out, Console.Error);
});

var doctorRegistryOption = new Option<string?>("--registry")
{
    Description = "Optional CSV export of the ID Registry to check for availability."
};

var doctor = new Command("doctor",
    "Run runtime self-diagnostics: config load, schema validity, registry, placeholder alignment, clause hashes.");
doctor.Options.Add(doctorRegistryOption);
doctor.SetAction(parseResult =>
    DoctorCommand.Run(Console.Out, Console.Error, parseResult.GetValue(doctorRegistryOption)));

var emitOutOption = new Option<string?>("--out")
{
    Description = "Directory to write the knowledge files to. Defaults to docs/governor."
};

var emitKnowledge = new Command("emit-knowledge",
    "Generate the Governor's per-role knowledge files from config.");
emitKnowledge.Options.Add(emitOutOption);
emitKnowledge.SetAction(parseResult =>
    EmitKnowledgeCommand.Run(parseResult.GetValue(emitOutOption), Console.Out, Console.Error));

var emitTokensOutOption = new Option<string?>("--out")
{
    Description = "File to write the stylesheet to. Defaults to config/generated/org-tokens.css."
};

var emitTokens = new Command("emit-tokens",
    "Generate the operator GUI's color tokens from config.");
emitTokens.Options.Add(emitTokensOutOption);
emitTokens.SetAction(parseResult =>
    EmitTokensCommand.Run(parseResult.GetValue(emitTokensOutOption), Console.Out, Console.Error));

var root = new RootCommand("The Registrar - governed artifact compiler")
    { validate, build, menu, test, doctor, emitKnowledge, emitTokens };
return root.Parse(args).Invoke();
