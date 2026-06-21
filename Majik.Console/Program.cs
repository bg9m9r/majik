using DotNetEnv;
using Majik.Console.Commands;

namespace Majik.Console;

/// <summary>
/// Diagnostic CLI shell. Hosts:
/// <list type="bullet">
/// <item><c>export-modern-cards</c> — regenerates the
///   <c>Majik.Core/CardData/Embedded/modern-cards.json.gz</c> seed from a
///   Scryfall bulk export. Replaces the one-shot SQLite dump from
///   PR #511 with a repeatable workflow.</item>
/// </list>
/// The SQLite + EF Core backed card import / keyword-analysis commands
/// were retired alongside the cards.db file in PR #511 — the engine now
/// reads its Modern-legal pool from the embedded gzipped JSON resource
/// in-process via <c>Majik.Core.CardData.EmbeddedCardRepository</c>.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
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
            args[0].Equals("export-modern-cards", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine(ExportModernCardsCommand.HelpText);
                return 1;
            }
            var output = args.Length >= 3 ? args[2] : null;
            return await ExportModernCardsCommand.RunAsync(args[1], output);
        }

        if (args.Length > 0 &&
            args[0].Equals("tune-bot-weights", StringComparison.OrdinalIgnoreCase))
        {
            return await Majik.Console.Commands.TuneBotWeightsCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 &&
            args[0].Equals("probe", StringComparison.OrdinalIgnoreCase))
        {
            return await ProbeCommand.RunAsync(args);
        }

        System.Console.WriteLine("Usage:");
        System.Console.WriteLine("  Majik.Console export-modern-cards <scryfall-all-cards.json> [output-path]");
        System.Console.WriteLine("  Majik.Console tune-bot-weights <archetype> [--games N] [--rounds R] [--strategy heuristic|mcts] [--step V] [--bad-start]");
        System.Console.WriteLine("  Majik.Console probe panel|<headName> [--n N] [--out DIR] [--concurrency C]");
        System.Console.WriteLine();
        System.Console.WriteLine(ExportModernCardsCommand.HelpText);
        System.Console.WriteLine();
        System.Console.WriteLine(Majik.Console.Commands.TuneBotWeightsCommand.HelpText);
        System.Console.WriteLine();
        System.Console.WriteLine(ProbeCommand.HelpText);
        return 0;
    }
}
