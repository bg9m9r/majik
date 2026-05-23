using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Prismatic Ending (Modern Horizons 2, {W}, Sorcery).
///
/// Covers:
///   - Card identity (Sorcery, {W}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Exile gated by colours-spent cap (1 → mv ≤ 1, 3 → mv ≤ 3).
///   - Cap-too-low fizzle (mv 4 against 3 colours → no exile).
///   - Land target → no effect (CR 608.2b illegal target).
///   - Default cap path (single-arg BuildSpellDefinition) = 1.
///   - ColorCount helper.
/// </summary>
public class PrismaticEndingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticEnding_IsSorcery_AtCostW()
    {
        var pe = PrismaticEndingFactory.Create(_alice);

        pe.Name.Should().Be("Prismatic Ending");
        pe.ManaCost.Should().Be("{W}");
        pe.HasType(CardType.Sorcery).Should().BeTrue();
        pe.Owner.Should().BeSameAs(_alice);
        pe.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PrismaticEnding()
    {
        var card = NamedCardFactory.Create("Prismatic Ending", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Prismatic Ending");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — colour-spent cap
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticEnding_OneColorSpent_Exiles_MvOneNonlandPermanent()
    {
        // Bob controls a 1-mv enchantment (Spreading Seas costs {1}{U} → mv 2,
        // so we use a fresh shape-only enchantment with mv 1).
        var trinket = NewControlledArtifact(_bob, "Mox Cheap", manaCost: "{0}");
        var oneMvCreature = NewControlledCreature(_bob, "Llanowar Elves", "{G}");

        ResolveAgainst(target: oneMvCreature, colorsSpent: 1);

        oneMvCreature.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(oneMvCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(oneMvCreature);

        // The cheap artifact wasn't targeted — still on battlefield.
        trinket.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void PrismaticEnding_ThreeColorsSpent_Exiles_MvThreeNonlandPermanent()
    {
        var c3 = NewControlledCreature(_bob, "Hill Giant", "{3}{R}"); // mv 4 — should NOT be exilable at 3
        var mv3 = NewControlledCreature(_bob, "Centaur Courser", "{2}{G}"); // mv 3

        ResolveAgainst(target: mv3, colorsSpent: 3);

        mv3.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(mv3);

        // Untargeted higher-mv permanent stays put.
        c3.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void PrismaticEnding_TargetExceedsCap_DoesNothing()
    {
        // mv 4 target, cap 3 → CR 608.2b illegal target, no effect.
        var bigGuy = NewControlledCreature(_bob, "Hill Giant", "{3}{R}");

        ResolveAgainst(target: bigGuy, colorsSpent: 3);

        bigGuy.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bigGuy);
        _bob.Zones.Exile.GetCards().Should().NotContain(bigGuy);
    }

    [Fact]
    public void PrismaticEnding_LandTarget_DoesNothing()
    {
        // Lands have mv 0 (no mana cost), so they'd pass the mv ≤ cap
        // check; the explicit "nonland" predicate at resolution must
        // still fizzle this case (CR 608.2b).
        var land = (Permanent)NamedCardFactory.Create("Mountain", _bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        ResolveAgainst(target: land, colorsSpent: 5);

        land.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
        _bob.Zones.Exile.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void PrismaticEnding_DefaultProvider_CapsAtOne()
    {
        // Single-arg BuildSpellDefinition path: no colorsSpentProvider →
        // cap defaults to DefaultColorsSpent = 1.
        var mv1 = NewControlledCreature(_bob, "Llanowar Elves", "{G}");
        var mv2 = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        // mv 1 exiles.
        var defForMv1 = PrismaticEndingFactory.BuildSpellDefinition(t => t);
        Resolve(defForMv1, mv1);
        mv1.Zone.Should().Be(ZoneType.Exile);

        // mv 2 stays put against the same default cap.
        var defForMv2 = PrismaticEndingFactory.BuildSpellDefinition(t => t);
        Resolve(defForMv2, mv2);
        mv2.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void ColorCount_CountsDistinctWUBRG_IgnoringOthers()
    {
        PrismaticEndingFactory.ColorCount(null).Should().Be(0);
        PrismaticEndingFactory.ColorCount("").Should().Be(0);
        PrismaticEndingFactory.ColorCount("W").Should().Be(1);
        PrismaticEndingFactory.ColorCount("WW").Should().Be(1);          // dedupes
        PrismaticEndingFactory.ColorCount("WUG").Should().Be(3);
        PrismaticEndingFactory.ColorCount("WUBRG").Should().Be(5);
        PrismaticEndingFactory.ColorCount("12C").Should().Be(0);         // generics/colourless skipped
        PrismaticEndingFactory.ColorCount("wu1Cg").Should().Be(3);       // case-insensitive
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve Prismatic Ending against <paramref name="target"/> with a
    /// caller-supplied colours-spent cap. Invokes the SpellDefinition's
    /// EffectFactory directly — bypasses SpellCastFlow / targeting prompt
    /// so the cap behaviour is the only thing under test.
    /// </summary>
    private static void ResolveAgainst(Permanent target, int colorsSpent)
    {
        var def = PrismaticEndingFactory.BuildSpellDefinition(
            resolver: t => t,
            colorsSpentProvider: () => colorsSpent);

        Resolve(def, target);
    }

    private static void Resolve(SpellDefinition def, Permanent target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
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

    private static Artifact NewControlledArtifact(Player owner, string name, string manaCost)
    {
        var a = new Artifact(name, manaCost);
        a.SetOwner(owner);
        a.SetController(owner);
        a.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(a);
        return a;
    }
}
