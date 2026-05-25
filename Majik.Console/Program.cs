using DotNetEnv;

namespace Majik.Console;

/// <summary>
/// Diagnostic CLI shell. The Scryfall import + keyword-analysis
/// commands have been removed along with the SQLite cards.db backend
/// (replaced by the embedded modern-cards.json.gz resource loaded
/// in-process by <c>Majik.Core.CardData.EmbeddedCardRepository</c>).
///
/// What's left is a triggers playground that exercises the engine's
/// triggered-ability pipeline without spinning up a server — useful
/// for ad-hoc engine smoke checks during local development.
/// </summary>
class Program
{
    static Task Main(string[] args)
    {
        // Load .env if present (legacy hook; nothing in this shell reads
        // it any more, but keep the side-effect so future commands can
        // pull API keys from a parent-dir .env without re-implementing).
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            var envPath = Path.Combine(currentDir.FullName, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                break;
            }
            currentDir = currentDir.Parent;
        }

        if (args.Length > 0 &&
            args[0].Equals("play-triggers", StringComparison.OrdinalIgnoreCase))
        {
            var scenario = args.Length > 1 ? args[1] : "all";
            TriggerPlayground.Run(scenario);
            return Task.CompletedTask;
        }

        System.Console.WriteLine("Usage:");
        System.Console.WriteLine("  Majik.Console play-triggers [etb|apnap|intervening-if|delayed|all]");
        System.Console.WriteLine();
        System.Console.WriteLine(
            "Card-data import / keyword-analysis / coverage commands have " +
            "been retired; the embedded modern-cards.json.gz resource in " +
            "Majik.Core ships the full Modern-legal pool in-process.");
        System.Console.WriteLine();
        TriggerPlayground.PrintScenarios();
        return Task.CompletedTask;
    }
}
