using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Abrupt Decay (Return to Ravnica, {B}{G}, Instant).
///
/// Covers:
///   - Card identity (Instant, {B}{G}, owner/controller).
///   - "Can't Be Countered" keyword marker present on the card.
///   - NamedCardFactory dispatch.
///   - Destroys target nonland permanent with mana value 3 or less.
///   - Target with mv 4 → no-op at resolution (mv exceeds cap).
///   - Target is a land → no-op (nonland predicate).
///   - Target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class AbruptDecayTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + keyword marker
    // -----------------------------------------------------------------------

    [Fact]
    public void AbruptDecay_IsInstant_AtCostBG()
    {
        var card = AbruptDecayFactory.Create(_alice);

        card.Name.Should().Be("Abrupt Decay");
        card.ManaCost.Should().Be("{B}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AbruptDecay_HasCantBeCounteredKeyword()
    {
        var card = AbruptDecayFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(AbruptDecayFactory.CantBeCounteredMarker,
                "Abrupt Decay carries the 'Can't Be Countered' structural marker");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AbruptDecay()
    {
        var card = NamedCardFactory.Create("Abrupt Decay", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Abrupt Decay");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys nonland permanent with mv ≤ 3
    // -----------------------------------------------------------------------

    [Fact]
    public void AbruptDecay_DestroysNonlandPermanent_WithMvThreeOrLess()
    {
        // mv 1 enchantment — should be destroyed.
        var enchantment = NewControlledEnchantment(_bob, "Shallow Enchant", "{G}");

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            "Abrupt Decay destroys a nonland permanent with mv 1 (≤ 3)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(enchantment);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
    }

    [Fact]
    public void AbruptDecay_DestroysMvThreePermanent()
    {
        // mv exactly 3 — boundary case, should still be destroyed.
        var creature = NewControlledCreature(_bob, "Centaur Courser", "{2}{G}"); // mv 3

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Abrupt Decay destroys a nonland permanent with mv exactly 3");
    }

    [Fact]
    public void AbruptDecay_MvFour_DoesNothing()
    {
        // mv 4 — exceeds the cap; CR 608.2b resolution-time gate.
        var bigGuy = NewControlledCreature(_bob, "Hill Giant", "{3}{R}"); // mv 4

        Resolve(bigGuy);

        bigGuy.Zone.Should().Be(ZoneType.Battlefield,
            "Abrupt Decay does not affect a permanent with mv 4 (exceeds cap of 3)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bigGuy);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bigGuy);
    }

    [Fact]
    public void AbruptDecay_LandTarget_DoesNothing()
    {
        // Lands have mv 0 (no printed mana cost), so they'd pass the mv ≤ 3
        // check; the explicit nonland predicate must still fizzle this (CR 608.2b).
        var land = (Permanent)NamedCardFactory.Create("Mountain", _bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Abrupt Decay does not affect a land (nonland only)");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void AbruptDecay_TargetNotOnBattlefield_DoesNothing()
    {
        // Simulate target leaving battlefield before resolution (CR 608.2b).
        var creature = NewControlledCreature(_bob, "Llanowar Elves", "{G}");

        // Remove from battlefield — simulate it leaving before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        // Attempt to resolve Abrupt Decay targeting the now-gone creature.
        ResolveRaw(creature);

        // It was already in graveyard, so no additional move should happen.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve Abrupt Decay against a <see cref="Permanent"/> target.
    /// Drives the SpellDefinition's EffectFactory directly.
    /// </summary>
    private static void Resolve(Permanent target) => ResolveRaw(target);

    private static void ResolveRaw(object targetToken)
    {
        var def = AbruptDecayFactory.BuildSpellDefinition(resolver: t => t);
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

    private static Enchantment NewControlledEnchantment(Player owner, string name, string cost)
    {
        var e = new Enchantment(name, cost);
        e.SetOwner(owner);
        e.SetController(owner);
        e.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(e);
        return e;
    }
}
