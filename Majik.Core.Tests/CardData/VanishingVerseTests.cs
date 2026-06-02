using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vanishing Verse (Strixhaven: School of Mages, {W}{B}, Instant).
///
/// Oracle text: "Exile target monocolored permanent."
///
/// Covers:
///   - Card identity (Instant, {W}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "monocolored permanent" target,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: exiles a monocolored creature (1 colour).
///   - Resolve: exiles a monocolored noncreature permanent (enchantment).
///   - Resolve: multicolored permanent (>=2 colours) → illegal at
///     resolution, exile fizzles (CR 105 / CR 608.2b monocolored filter).
///   - Resolve: colorless permanent (0 colours) → illegal, exile fizzles.
///   - Resolve: off-battlefield target → exile fizzles (CR 608.2b).
/// </summary>
public class VanishingVerseTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VanishingVerse_IsInstant_AtCostWB()
    {
        var card = VanishingVerseFactory.Create(_alice);

        card.Name.Should().Be("Vanishing Verse");
        card.ManaCost.Should().Be("{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VanishingVerse()
    {
        var card = NamedCardFactory.Create("Vanishing Verse", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vanishing Verse");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{W}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void VanishingVerse_Definition_HasSingleMonocoloredPermanentTarget()
    {
        var def = VanishingVerseFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("monocolored permanent");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile legal monocolored permanents
    // -----------------------------------------------------------------------

    [Fact]
    public void VanishingVerse_ExilesMonocoloredCreature()
    {
        // Goblin Guide is mono-red (1 colour) → legal monocolored target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Exile,
            "Vanishing Verse exiles the targeted monocolored permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void VanishingVerse_ExilesMonocoloredEnchantment()
    {
        // Mono-white enchantment (1 colour) → legal.
        var enchantment = new Enchantment("Oblivion Ring", "{2}{W}",
            supertypes: null, subtypes: null)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets (CR 105 monocolored filter / CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void VanishingVerse_MulticoloredTarget_FizzlesExile()
    {
        // Two colours (W + U) → multicolored, NOT monocolored → illegal.
        var hybrid = NewControlledCreature(_bob, "Geist of Saint Traft", "{1}{W}{U}");

        Resolve(hybrid);

        hybrid.Zone.Should().Be(ZoneType.Battlefield,
            "Vanishing Verse cannot exile a multicolored permanent (CR 105 — >=2 colours)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(hybrid);
        _bob.Zones.Exile.GetCards().Should().NotContain(hybrid);
    }

    [Fact]
    public void VanishingVerse_ColorlessTarget_FizzlesExile()
    {
        // No mana cost → colorless (0 colours) → NOT monocolored → illegal.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Vanishing Verse cannot exile a colorless permanent (CR 105 — 0 colours)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);
        _bob.Zones.Exile.GetCards().Should().NotContain(artifact);
    }

    [Fact]
    public void VanishingVerse_TargetNotOnBattlefield_FizzlesExile()
    {
        var creature = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Exile fizzles — creature stays in graveyard (CR 608.2b).
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Exile.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = VanishingVerseFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
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
}
