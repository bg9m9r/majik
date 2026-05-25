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
/// Tests for Hero's Downfall (Theros, {1}{B}{B}, Instant).
///
/// Oracle text: "Destroy target creature or planeswalker."
///
/// Covers:
///   - Card identity (Instant, {1}{B}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 creature-or-PW target request,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: destroys a creature (CR 701.7).
///   - Resolve: destroys a planeswalker (CR 701.7).
///   - Resolve: artifact target (not creature, not PW) is illegal at
///     resolution → no-op (CR 608.2b).
///   - Resolve: off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class HerosDownfallTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HerosDownfall_IsInstant_AtCost1BB()
    {
        var card = HerosDownfallFactory.Create(_alice);

        card.Name.Should().Be("Hero's Downfall");
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HerosDownfall()
    {
        var card = NamedCardFactory.Create("Hero's Downfall", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Hero's Downfall");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HerosDownfall_Definition_HasSingleCreatureOrPlaneswalkerTarget()
    {
        var def = HerosDownfallFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys creature / planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void HerosDownfall_DestroysCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Hero's Downfall destroys the targeted creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void HerosDownfall_DestroysPlaneswalker()
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
            "Hero's Downfall destroys the targeted planeswalker (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(pw);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void HerosDownfall_ArtifactTarget_DoesNothing()
    {
        // Pure artifact (not creature, not PW) — illegal at resolution.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Hero's Downfall can only destroy creatures or planeswalkers (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void HerosDownfall_TargetNotOnBattlefield_DoesNothing()
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
        var def = HerosDownfallFactory.BuildDefinition(targetResolver: t => t);
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
