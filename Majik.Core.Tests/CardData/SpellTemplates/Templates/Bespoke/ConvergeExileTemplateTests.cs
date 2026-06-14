using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.CardData.SpellTemplates.Templates.Control;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Tests for the Converge-exile bespoke template (Prismatic Ending,
/// CR 202.2 / CR 106.4). Verifies:
///   - the template matches the Converge-exile oracle text and beats the
///     plain ExileTarget template;
///   - the colors-of-mana-spent cap is read off the live mana-provenance
///     ledger (Card.PendingCastColors) surfaced via ResolutionContext.SourceCard;
///   - mv ≤ cap exiles; mv > cap fizzles (CR 608.2b); land target fizzles.
/// </summary>
public class ConvergeExileTemplateTests
{
    private const string PrismaticEndingOracle =
        "Converge — Exile target nonland permanent if its mana value is less " +
        "than or equal to the number of colors of mana spent to cast this spell.";

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static SpellBindContext Ctx(string text, Player caster) =>
        new(
            new CardEntity { Name = "Prismatic Ending", OracleText = text, TypeLine = "Sorcery" },
            caster,
            r => r,
            Effects: null,
            Stack: null);

    [Fact]
    public void Template_Matches_PrismaticEndingOracle()
    {
        new ConvergeExileTemplate().TryBind(Ctx(PrismaticEndingOracle, _alice))
            .Should().NotBeNull("the Converge-exile family must bind");
    }

    [Fact]
    public void Template_OutranksPlainExileTarget()
    {
        // The plain ExileTarget template ALSO matches the oracle, but the
        // Converge template must win the registry race (higher priority) so
        // the colors-spent gate is honoured.
        new ExileTargetTemplate().Priority.Should().BeLessThan(new ConvergeExileTemplate().Priority);
        new ExileTargetTemplate().TryBind(Ctx(PrismaticEndingOracle, _alice))
            .Should().NotBeNull("ExileTarget would otherwise also match — proving the priority guard matters");
    }

    [Fact]
    public void Template_DoesNotMatch_PlainExile()
    {
        new ConvergeExileTemplate().TryBind(Ctx("Exile target nonland permanent.", _alice))
            .Should().BeNull("a plain exile spell has no Converge rider");
    }

    [Fact]
    public void OracleSpellBinder_BindsConvergeTemplate_ForPrismaticEnding()
    {
        var entity = new CardEntity
        {
            Name = "Prismatic Ending",
            OracleText = PrismaticEndingOracle,
            TypeLine = "Sorcery",
            ManaCost = "{X}{W}",
        };
        var def = OracleSpellBinder.Bind(entity, _alice, r => r, stack: null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------
    // Resolution — cap follows the LIVE mana-provenance ledger.
    // ------------------------------------------------------------------

    [Fact]
    public void Resolve_OneColorSpent_Exiles_MvOne_NotMvTwo()
    {
        var mv1 = NewControlledCreature(_bob, "Llanowar Elves", "{G}");
        var mv2 = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        // {X}{W} cast with X=0 → only the white pip spent → 1 color.
        ResolveAgainstWithLedger(mv1, ManaColor.White);
        mv1.Zone.Should().Be(ZoneType.Exile);

        ResolveAgainstWithLedger(mv2, ManaColor.White);
        mv2.Zone.Should().Be(ZoneType.Battlefield, "mv 2 exceeds a 1-color cap (CR 608.2b)");
    }

    [Fact]
    public void Resolve_ThreeColorsSpent_Exiles_MvThree_NotMvFour()
    {
        var mv3 = NewControlledCreature(_bob, "Centaur Courser", "{2}{G}");
        var mv4 = NewControlledCreature(_bob, "Hill Giant", "{3}{R}");

        // {X}{W} with X paid by U + B + the W pip → 3 colors.
        ResolveAgainstWithLedger(mv3, ManaColor.White, ManaColor.Blue, ManaColor.Black);
        mv3.Zone.Should().Be(ZoneType.Exile);

        ResolveAgainstWithLedger(mv4, ManaColor.White, ManaColor.Blue, ManaColor.Black);
        mv4.Zone.Should().Be(ZoneType.Battlefield, "mv 4 exceeds a 3-color cap");
    }

    [Fact]
    public void Resolve_LandTarget_DoesNothing()
    {
        var land = (Permanent)NamedCardFactory.Create("Mountain", _bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        ResolveAgainstWithLedger(
            land,
            ManaColor.White, ManaColor.Blue, ManaColor.Black, ManaColor.Red, ManaColor.Green);

        land.Zone.Should().Be(ZoneType.Battlefield, "the nonland predicate must still fizzle a land (CR 608.2b)");
    }

    [Fact]
    public void Resolve_NoColoredManaStamped_CapsAtZero()
    {
        // A card with no PendingCastColors ledger (no cast happened) → cap 0,
        // so even an mv-0 token would be the only thing exilable. An mv-1
        // creature stays put.
        var mv1 = NewControlledCreature(_bob, "Llanowar Elves", "{G}");
        ResolveAgainstWithLedger(mv1 /* no colors */);
        mv1.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Build the Prismatic Ending spell via the production binder, stamp the
    /// per-color spent ledger on the card the way TurnDriver does at pay time,
    /// then resolve the spell so the resolution effect reads the ledger off
    /// ResolutionContext.SourceCard.
    /// </summary>
    private void ResolveAgainstWithLedger(Permanent target, params ManaColor[] colorsSpent)
    {
        var entity = new CardEntity
        {
            Name = "Prismatic Ending",
            OracleText = PrismaticEndingOracle,
            TypeLine = "Sorcery",
            ManaCost = "{X}{W}",
        };
        var def = OracleSpellBinder.Bind(entity, _alice, r => r, stack: null)!;

        var card = new Sorcery("Prismatic Ending", "{X}{W}");
        card.SetOwner(_alice);
        card.SetController(_alice);

        // Mirror TurnDriver.PayCastMana: stamp the per-color spent ledger.
        var counts = new Dictionary<ManaColor, int>();
        foreach (var c in colorsSpent) counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
        card.SetPendingCastColorCounts(counts);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        var spell = new Majik.Core.Spells.Spell(card, _alice, effects: effects);
        spell.Resolve();
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
