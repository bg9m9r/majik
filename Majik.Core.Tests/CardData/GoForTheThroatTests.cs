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
/// Tests for Go for the Throat (Mirrodin Besieged, {1}{B}, Instant).
///
/// Oracle text: "Destroy target nonartifact creature."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a nonartifact creature (moves to owner's graveyard, CR 701.7).
///   - Artifact Creature target → no-op at resolution (CR 700.4 + CR 608.2b).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class GoForTheThroatTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GoForTheThroat_IsInstant_AtCost1B()
    {
        var card = GoForTheThroatFactory.Create(_alice);

        card.Name.Should().Be("Go for the Throat");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoForTheThroat()
    {
        var card = NamedCardFactory.Create("Go for the Throat", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Go for the Throat");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonartifact creature
    // -----------------------------------------------------------------------

    [Fact]
    public void GoForTheThroat_DestroysNonartifactCreature()
    {
        // Plain creature — nonartifact, legal target.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Go for the Throat destroys the nonartifact target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void GoForTheThroat_BlackCreature_Destroyed()
    {
        // Unlike Doom Blade, Go for the Throat hits black creatures.
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Graveyard,
            "Go for the Throat has no colour restriction — black creatures are legal");
    }

    // -----------------------------------------------------------------------
    // Resolution — artifact creature filter
    // -----------------------------------------------------------------------

    [Fact]
    public void GoForTheThroat_ArtifactCreature_NotDestroyed()
    {
        // Artifact Creature (e.g. Arcbound Ravager) — illegal target.
        var ravager = new Creature("Arcbound Ravager", "{2}", 0, 0);
        ravager.AddCardType(CardType.Artifact);
        ravager.SetOwner(_bob);
        ravager.SetController(_bob);
        ravager.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ravager);

        Resolve(ravager);

        ravager.Zone.Should().Be(ZoneType.Battlefield,
            "Go for the Throat cannot destroy an Artifact Creature (CR 700.4)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(ravager);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(ravager);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void GoForTheThroat_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveRaw(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — illegal target at resolution → effect does nothing");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = GoForTheThroatFactory.BuildDefinition(targetResolver: t => t);
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
