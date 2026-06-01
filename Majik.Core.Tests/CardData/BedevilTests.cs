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
/// Tests for Bedevil (War of the Spark, {B}{B}{R}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "Destroy target artifact, creature, or planeswalker."
///
/// Bedevil is the Rakdos cousin of Hero's Downfall / Dreadbore: the same
/// Destroy resolve at instant timing, with the target filter widened to also
/// accept a pure (non-creature) artifact. The test set mirrors
/// DreadboreTests, adding a positive artifact-destroy case.
///
/// Covers:
///   - Card identity (Instant, {B}{B}{R}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 artifact/creature/PW target
///     request, no modes, no variable X, BotIntent.Removal.
///   - Resolve: destroys a creature (CR 701.7).
///   - Resolve: destroys a planeswalker (CR 701.7).
///   - Resolve: destroys a pure artifact (CR 701.7) — Bedevil's widening.
///   - Resolve: enchantment target (not artifact/creature/PW) is illegal at
///     resolution → no-op (CR 608.2b).
///   - Resolve: off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class BedevilTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Bedevil_IsInstant_AtCostBBR()
    {
        var card = BedevilFactory.Create(_alice);

        card.Name.Should().Be("Bedevil");
        card.ManaCost.Should().Be("{B}{B}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Bedevil()
    {
        var card = NamedCardFactory.Create("Bedevil", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Bedevil");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}{B}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Bedevil_Definition_HasSingleArtifactCreatureOrPlaneswalkerTarget()
    {
        var def = BedevilFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("artifact, creature, or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys creature / planeswalker / artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void Bedevil_DestroysCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Bedevil destroys the targeted creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void Bedevil_DestroysPlaneswalker()
    {
        var pw = new Planeswalker(
            name: "Liliana, the Last Hope",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            subtypes: new[] { CardSubtype.Liliana })
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        Resolve(pw);

        pw.Zone.Should().Be(ZoneType.Graveyard,
            "Bedevil destroys the targeted planeswalker (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(pw);
    }

    [Fact]
    public void Bedevil_DestroysArtifact()
    {
        // Pure artifact (not creature, not PW) — Bedevil's widening over
        // Hero's Downfall / Dreadbore makes this a legal target.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "Bedevil destroys the targeted artifact (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void Bedevil_EnchantmentTarget_DoesNothing()
    {
        // Pure enchantment (not artifact, not creature, not PW) — illegal at
        // resolution.
        var enchantment = new Enchantment("Oblivion Ring", "{2}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            "Bedevil can only destroy artifacts, creatures, or planeswalkers (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(enchantment);
    }

    [Fact]
    public void Bedevil_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged by the resolve — CR 608.2b illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = BedevilFactory.BuildDefinition(targetResolver: t => t);
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
