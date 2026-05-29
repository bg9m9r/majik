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
/// Tests for Power Word Kill (Adventures in the Forgotten Realms, {1}{B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target non-Angel, non-Demon, non-Devil, non-Dragon creature."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a creature with none of the excluded subtypes (CR 701.7).
///   - Each excluded subtype (Angel / Demon / Devil / Dragon) → no-op at
///     resolution (CR 608.2b illegal-target filter).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class PowerWordKillTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PowerWordKill_IsInstant_AtCost1B()
    {
        var card = PowerWordKillFactory.Create(_alice);

        card.Name.Should().Be("Power Word Kill");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PowerWordKill()
    {
        var card = NamedCardFactory.Create("Power Word Kill", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Power Word Kill");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a non-excluded creature
    // -----------------------------------------------------------------------

    [Fact]
    public void PowerWordKill_DestroysOrdinaryCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}", CardSubtype.Goblin);

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Power Word Kill destroys a creature with none of the excluded subtypes (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    // -----------------------------------------------------------------------
    // Resolution — excluded subtypes are illegal targets
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(CardSubtype.Angel)]
    [InlineData(CardSubtype.Demon)]
    [InlineData(CardSubtype.Devil)]
    [InlineData(CardSubtype.Dragon)]
    public void PowerWordKill_ExcludedSubtype_NotDestroyed(CardSubtype excluded)
    {
        var creature = NewControlledCreature(_bob, "Excluded Creature", "{2}", excluded);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            $"Power Word Kill cannot target a {excluded} creature (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolution — off-battlefield target
    // -----------------------------------------------------------------------

    [Fact]
    public void PowerWordKill_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", CardSubtype.Lhurgoyf);

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
        var def = PowerWordKillFactory.BuildDefinition(targetResolver: t => t);
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

    private static Creature NewControlledCreature(
        Player owner, string name, string cost, CardSubtype subtype)
    {
        var c = new Creature(name, cost, 1, 1, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
