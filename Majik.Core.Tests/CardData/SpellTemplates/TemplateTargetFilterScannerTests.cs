using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// CR 115.1 / 601.2c — a spell's targets are what its text says. Scans every
/// template-bound target request in the embedded pool (instants and
/// sorceries bound by <see cref="OracleSpellBinder"/>, the live path) and
/// compares the bound request against the oracle phrase it came from, for a
/// closed set of checkable properties:
/// <list type="bullet">
///   <item>Type: the card-type words of the target noun phrase
///   ("target artifact" against "target artifact or enchantment");</item>
///   <item>Controller: "you control" / "an opponent controls" /
///   "you don't control";</item>
///   <item>Opponent: "target opponent" bound as any player;</item>
///   <item>Flying: "with flying" / "without flying";</item>
///   <item>ManaValue: a mana value limit;</item>
///   <item>Spell: a restricted "target ... spell" bound as any spell.</item>
/// </list>
/// The bound side is the request's description, which is what the central
/// candidate service reads to build the pool. A request with its own gatherer
/// may filter more than its description says; such a request can show here
/// as a false mismatch. Requests are paired with the "target" phrases of
/// their mode (or of the whole text) in order; where the counts differ the
/// request is not compared (counted in the output).
///
/// Known mismatches are allow-listed (shrink-only, like the full-game gate):
/// the test fails on a mismatch that is not listed, and on a listed one that
/// no longer occurs, so the list has to be shrunk as the binder is fixed.
/// </summary>
public class TemplateTargetFilterScannerTests
{
    private readonly ITestOutputHelper _out;
    public TemplateTargetFilterScannerTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] TypeWords =
        { "creature", "artifact", "enchantment", "land", "planeswalker", "battle", "permanent", "spell", "player", "opponent" };

    private static readonly HashSet<string> NounPhraseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "creature", "creatures", "artifact", "artifacts", "enchantment", "enchantments", "land", "lands",
        "planeswalker", "planeswalkers", "battle", "battles", "permanent", "permanents", "spell", "spells",
        "player", "players", "opponent", "opponents", "card", "cards", "or", "and/or", "and",
        "instant", "sorcery", "nonland", "noncreature", "nonartifact", "nonenchantment", "nonlegendary",
        "legendary", "basic", "nonbasic", "tapped", "untapped", "attacking", "blocking", "nontoken", "token",
        "white", "blue", "black", "red", "green", "nonwhite", "nonblue", "nonblack", "nonred", "nongreen",
        "multicolored", "monocolored", "colorless", "activated", "triggered", "ability", "abilities",
    };

    private static readonly Regex TargetWord = new(@"\btarget\b", RegexOptions.IgnoreCase);
    private static readonly Regex Reminder = new(@"\([^)]*\)");

    internal sealed record Phrase(string Noun, string Qualifier);

    /// <summary>The "target ..." phrases of <paramref name="text"/> in order:
    /// the noun phrase right after "target", and the qualifier window that
    /// runs to the next "target" or sentence break.</summary>
    internal static List<Phrase> Phrases(string text)
    {
        text = Reminder.Replace(text, " ");
        var result = new List<Phrase>();
        var ms = TargetWord.Matches(text);
        for (var i = 0; i < ms.Count; i++)
        {
            var start = ms[i].Index + ms[i].Length;
            var end = i + 1 < ms.Count ? ms[i + 1].Index : text.Length;
            var window = text[start..end];
            var stop = window.IndexOfAny(new[] { '.', ';', '\n', '•' });
            if (stop >= 0) window = window[..stop];
            var tokens = window.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var noun = new List<string>();
            foreach (var tok in tokens)
            {
                if (!NounPhraseWords.Contains(tok)) break;
                noun.Add(tok.ToLowerInvariant());
            }
            result.Add(new Phrase(string.Join(' ', noun), window.ToLowerInvariant()));
        }
        return result;
    }

    private static string Singular(string w) => w.EndsWith("s") && w != "abilities" ? w[..^1] : w;

    internal static SortedSet<string> Types(string nounPhrase) => new(
        nounPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Singular).Where(TypeWords.Contains));

    // ---- the fixture board: what a bound request's live pool is read against ----

    private sealed class Board
    {
        public readonly Player You = new("You", 20);
        public readonly Player Opp = new("Opp", 20);
        public readonly Majik.Core.Stack.Stack Stack;
        public readonly Majik.Core.Game.GameContext Ctx;

        public Board()
        {
            var bus = new Majik.Core.Events.EventBus();
            Stack = new Majik.Core.Stack.Stack(bus);
            foreach (var p in new[] { You, Opp })
            {
                Put(p, new Creature($"{p.Name} Bear", "{1}{G}", 2, 2));
                var flier = new Creature($"{p.Name} Drake", "{3}{U}{U}", 3, 3);
                flier.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", flier, p));
                Put(p, flier);
                Put(p, new Artifact($"{p.Name} Rock", "{2}"));
                Put(p, new Enchantment($"{p.Name} Rune", "{2}{W}"));
                Put(p, new Land($"{p.Name} Plains"));
                Put(p, new Planeswalker($"{p.Name} Walker", "{4}", startingLoyalty: 3));
            }
            foreach (var spell in new Card[] { new Instant("Opp Bolt", "{R}"), new Creature("Opp Bear Spell", "{1}{G}", 2, 2) })
            {
                spell.SetOwner(Opp); spell.SetController(Opp); spell.SetZone(Majik.Core.Zones.ZoneType.Stack);
                Stack.Push(new Majik.Core.Spells.Spell(spell, Opp));
            }
            Ctx = new Majik.Core.Game.GameContext(You, new[] { You, Opp }, You, 1,
                Majik.Core.StateMachine.StepStateType.PreCombatMain, Stack);
        }

        private static void Put(Player p, Card c)
        {
            c.SetOwner(p); c.SetController(p);
            p.Zones.Battlefield.AddCard(c); c.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        }
    }

    private static ICard? AsCard(object o) => o switch
    {
        ICard c => c,
        Majik.Core.Spells.Spell s => s.Card,
        _ => null,
    };

    private static bool IsSpell(object o) =>
        o is Majik.Core.Spells.Spell || (o is ICard c && c.Zone == Majik.Core.Zones.ZoneType.Stack);

    private static bool HasCardType(ICard c, string type) => type switch
    {
        "creature" => c.HasType(CardType.Creature),
        "artifact" => c.HasType(CardType.Artifact),
        "enchantment" => c.HasType(CardType.Enchantment),
        "land" => c.HasType(CardType.Land),
        "planeswalker" => c.HasType(CardType.Planeswalker),
        _ => false,
    };

    /// <summary>Does <paramref name="o"/> fit the printed noun's type words?</summary>
    private static bool FitsTypes(object o, SortedSet<string> types, Player you)
    {
        if (o is Player pl)
            return types.Contains("player") || (types.Contains("opponent") && !ReferenceEquals(pl, you));
        if (IsSpell(o)) return types.Contains("spell");
        var c = AsCard(o);
        if (c is null) return true;
        if (types.Contains("permanent") && c.Zone == Majik.Core.Zones.ZoneType.Battlefield) return true;
        return types.Any(t => HasCardType(c, t));
    }

    private static string AfterNoun(Phrase ph)
    {
        var q = ph.Qualifier.TrimStart();
        foreach (var w in ph.Noun.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            q = q.TrimStart(' ', ',');
            if (q.StartsWith(w, StringComparison.OrdinalIgnoreCase)) q = q[w.Length..];
        }
        return q.TrimStart(' ', ',');
    }

    private static readonly Regex ManaValueLimit = new(@"^with mana value (\d+) or (less|greater)");

    /// <summary>Every mismatch, as "Category|Card|source|oracle phrase|bound description".</summary>
    internal static (List<string> Mismatches, int Compared, int Unaligned) Scan()
    {
        var repo = new EmbeddedCardRepository();
        var board = new Board();
        var you = board.You;
        var mismatches = new List<string>();
        int compared = 0, unaligned = 0;
        foreach (var card in repo.Search(null, false, 100000).OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            if (card.OracleText is null) continue;
            var tl = card.TypeLine ?? "";
            if (!(tl.Contains("Instant") || tl.Contains("Sorcery"))) continue;
            Majik.Core.Game.SpellDefinition? def;
            try { def = OracleSpellBinder.Bind(card, you, x => x, null); }
            catch { continue; }
            if (def is null || def.TargetRequests.Count == 0) continue;

            foreach (var g in def.TargetRequests.GroupBy(r => r.ModeIndex))
            {
                string source, label;
                if (g.Key is int m && m < def.Modes.Count) { source = def.Modes[m]; label = $"mode {m}"; }
                else if (g.Key is null && def.Modes.Count == 0) { source = card.OracleText; label = "spell"; }
                else continue;
                var phrases = Phrases(source.Replace(card.Name, "~"));
                var reqs = g.ToList();
                if (phrases.Count != reqs.Count) { unaligned += reqs.Count; continue; }
                for (var k = 0; k < reqs.Count; k++)
                {
                    var ph = phrases[k];
                    var types = Types(ph.Noun);
                    if (types.Count == 0) { unaligned++; continue; }
                    IReadOnlyList<object> pool;
                    try { pool = Majik.Core.Targeting.TargetCollection.ResolveLivePool(reqs[k], board.Ctx, synthesizeWhenEmpty: true); }
                    catch { unaligned++; continue; }
                    compared++;
                    var hits = new SortedSet<string>(StringComparer.Ordinal);
                    var after = AfterNoun(ph);
                    var mv = ManaValueLimit.Match(after);
                    foreach (var o in pool)
                    {
                        // A pool with any object the printed noun does not
                        // allow is wider than printed.
                        if (!FitsTypes(o, types, you))
                            hits.Add(o is Player && types.Contains("opponent") && !types.Contains("player") ? "Opponent" : "Type");
                        var c = AsCard(o);
                        if (c is null || IsSpell(o))
                        {
                            if (c is not null && ph.Noun.Contains("spell"))
                            {
                                var wantsCreature = Regex.IsMatch(ph.Noun, @"\bcreature\b");
                                var wantsNoncreature = ph.Noun.Contains("noncreature");
                                var wantsInstant = Regex.IsMatch(ph.Noun, @"\b(instant|sorcery)\b");
                                if ((wantsCreature && !c.HasType(CardType.Creature))
                                    || (wantsNoncreature && c.HasType(CardType.Creature))
                                    || (wantsInstant && !wantsCreature && !(c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))))
                                    hits.Add("Spell");
                            }
                            continue;
                        }
                        if (c.Zone != Majik.Core.Zones.ZoneType.Battlefield) continue;
                        var mine = ReferenceEquals(c.Controller, you);
                        if (after.StartsWith("you control") && !mine) hits.Add("Controller");
                        if ((after.StartsWith("an opponent controls") || after.StartsWith("you don't control")) && mine) hits.Add("Controller");
                        if (c.HasType(CardType.Creature))
                        {
                            var flies = c.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>().Any(a => a.Keyword == "Flying");
                            if (after.StartsWith("with flying") && !flies) hits.Add("Flying");
                            if (after.StartsWith("without flying") && flies) hits.Add("Flying");
                        }
                        if (mv.Success)
                        {
                            var n = int.Parse(mv.Groups[1].Value);
                            var v = ((Card)c).ManaCostValue.TotalValue;
                            if (mv.Groups[2].Value == "less" ? v > n : v < n) hits.Add("ManaValue");
                        }
                    }
                    foreach (var h in hits)
                        mismatches.Add($"{h}|{card.Name}|{label}|target {ph.Noun}|{reqs[k].Description}");
                }
            }
        }
        return (mismatches.Distinct().ToList(), compared, unaligned);
    }

    // Its own CI step (ci.yml "Target-filter scanner") so the mismatch table
    // is readable in the log; the Core test step excludes this category.
    [Fact]
    [Trait("Category", "Scanner")]
    public void BoundTargetFilters_MatchTheOraclePhrase_ExceptTheAllowListedOnes()
    {
        var (found, compared, unaligned) = Scan();
        foreach (var grp in found.GroupBy(s => s.Split('|')[0]).OrderBy(g => g.Key))
            _out.WriteLine($"SCAN {grp.Key}: {grp.Count()}");
        _out.WriteLine($"SCAN compared={compared} unaligned={unaligned} mismatches={found.Count}");

        var path = Environment.GetEnvironmentVariable("MAJIK_SCAN_DUMP");
        if (!string.IsNullOrEmpty(path)) File.WriteAllLines(path, found.OrderBy(x => x, StringComparer.Ordinal));

        var known = TemplateTargetFilterAllowList.Entries.ToHashSet(StringComparer.Ordinal);
        var unlisted = found.Where(f => !known.Contains(f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var vanished = known.Where(k => !found.Contains(k)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        unlisted.Should().BeEmpty("a bound target filter that differs from its oracle phrase is a new binder defect");
        vanished.Should().BeEmpty("a fixed mismatch must be removed from the allow-list (shrink-only)");
    }
}