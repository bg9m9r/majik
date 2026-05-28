using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WakerOfWavesFactory"/>.
///
/// Covers:
/// - Identity: name, type, mana cost {5}{U}{U}, MV 7, 7/7,
///   Whale subtype, owner/controller.
/// - NamedCardFactory dispatch.
/// - Static effect: opponent's creature (ANY type) gets -1 power (toughness
///   unaffected) while Waker is on the battlefield.
/// - Static effect: controller's own creature is NOT debuffed.
/// - Static effect: LTB lifts the debuff.
/// - Activated ability cost shape: {1}{U} + DiscardSelfCost.
/// - Activated ability: payable while Waker is in hand; rejected elsewhere.
/// - Activated ability resolve: top 2 to hand+graveyard (first card → hand
///   when no agent registered; the other → graveyard).
/// - Activated ability resolve: single card in library → hand.
/// - Activated ability resolve: empty library → no-op.
/// </summary>
public class WakerOfWavesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WakerOfWaves_Identity()
    {
        var c = WakerOfWavesFactory.Create(_alice);

        c.Name.Should().Be("Waker of Waves");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Whale).Should().BeTrue("Waker of Waves is a Whale");
        c.BasePower.Should().Be(7);
        c.BaseToughness.Should().Be(7);
        c.ManaCost.Should().Be("{5}{U}{U}");
        c.ManaCostValue.TotalValue.Should().Be(7, "MV = 5 + 1 + 1 = 7");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WakerOfWaves_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Waker of Waves", _alice);

        c.Should().BeOfType<Creature>("Waker of Waves is a Creature");
        c.Name.Should().Be("Waker of Waves");
        c.HasSubtype(CardSubtype.Whale).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static anti-anthem: "Creatures your opponents control get -1/-0."
    // -----------------------------------------------------------------------

    [Fact]
    public void WakerOfWaves_StaticDebuff_ReducesOpponentCreaturePowerByOne()
    {
        var svc = new ContinuousEffectsService();

        // Bob (opponent) controls a 3/3 bear-type creature.
        var oppCreature = new Creature("Grizzly Bears", "1G", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var waker = WakerOfWavesFactory.Create(_alice, continuousEffects: svc);
        waker.Zone = ZoneType.Battlefield;
        waker.ActiveEffects = svc;

        oppCreature.GetPower().Should().Be(2, "opponent's creature gets -1 power");
        oppCreature.GetToughness().Should().Be(3, "toughness is unaffected (-0)");
    }

    [Fact]
    public void WakerOfWaves_StaticDebuff_AppliesToAnyCreatureType()
    {
        var svc = new ContinuousEffectsService();

        // Bob controls a Goblin, an Angel, and a plain Beast — different types.
        var goblin = new Creature("Goblin Guide", "R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob, Controller = _bob,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var angel = new Creature("Serra Angel", "3WW", 4, 4,
            subtypes: new[] { CardSubtype.Angel })
        {
            Owner = _bob, Controller = _bob,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var beast = new Creature("Grizzly Bears", "1G", 3, 3,
            subtypes: new[] { CardSubtype.Beast })
        {
            Owner = _bob, Controller = _bob,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var waker = WakerOfWavesFactory.Create(_alice, continuousEffects: svc);
        waker.Zone = ZoneType.Battlefield;
        waker.ActiveEffects = svc;

        // All opponent creatures — regardless of type — get -1/-0.
        goblin.GetPower().Should().Be(1, "Goblin gets -1 power");
        goblin.GetToughness().Should().Be(2, "Goblin toughness unchanged");
        angel.GetPower().Should().Be(3, "Angel gets -1 power");
        angel.GetToughness().Should().Be(4, "Angel toughness unchanged");
        beast.GetPower().Should().Be(2, "Beast gets -1 power");
        beast.GetToughness().Should().Be(3, "Beast toughness unchanged");
    }

    [Fact]
    public void WakerOfWaves_StaticDebuff_DoesNotAffectControllersOwnCreatures()
    {
        var svc = new ContinuousEffectsService();

        // Alice (Waker's controller) controls another creature.
        var ownCreature = new Creature("Merfolk Looter", "1U", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var waker = WakerOfWavesFactory.Create(_alice, continuousEffects: svc);
        waker.Zone = ZoneType.Battlefield;
        waker.ActiveEffects = svc;

        ownCreature.GetPower().Should().Be(1,
            "CR 109.5 — 'your opponents control' excludes Waker's controller");
        ownCreature.GetToughness().Should().Be(1);
    }

    [Fact]
    public void WakerOfWaves_StaticDebuff_LTB_LiftsDebuff()
    {
        var svc = new ContinuousEffectsService();

        var oppCreature = new Creature("Hill Giant", "3R", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var waker = WakerOfWavesFactory.Create(_alice, continuousEffects: svc);
        waker.Zone = ZoneType.Battlefield;
        waker.ActiveEffects = svc;

        // Baseline: debuff active.
        oppCreature.GetPower().Should().Be(2);

        // Waker leaves the battlefield — LordStaticEffect.IsActive() returns
        // false; debuff lifts (CR 613 — continuous effects from a permanent
        // stop applying when it leaves play).
        waker.SetZone(ZoneType.Graveyard);

        oppCreature.GetPower().Should().Be(3, "debuff lifts on LTB");
        oppCreature.GetToughness().Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Activated ability cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WakerOfWaves_HasActivatedAbility_WithCorrectCostShape()
    {
        var waker = WakerOfWavesFactory.Create(_alice);

        var ability = waker.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "{1}{U} mana + DiscardSelfCost");
        ability.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1);
        manaCost.Blue.Should().Be(1);
    }

    [Fact]
    public void WakerOfWaves_ActivatedAbility_PayableWhenCardInHand()
    {
        var waker = WakerOfWavesFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(waker);

        var ability = waker.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = ability.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeTrue(
            "activation is legal while Waker is in hand");
    }

    [Fact]
    public void WakerOfWaves_ActivatedAbility_RejectedWhenNotInHand()
    {
        var waker = WakerOfWavesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(waker);

        var ability = waker.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = ability.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "activation only legal from hand — DiscardSelfCost rejects battlefield");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolve: top-2 distribute
    // -----------------------------------------------------------------------

    [Fact]
    public void WakerOfWaves_Resolve_PutsFirstCardInHand_SecondInGraveyard()
    {
        var waker = WakerOfWavesFactory.Create(_alice);

        // Seed Alice's library with two cards (top = card1, second = card2).
        var card1 = new Instant("Lightning Bolt", "R");
        card1.SetOwner(_alice);
        var card2 = new Instant("Counterspell", "UU");
        card2.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card1);
        _alice.Zones.Library.AddCard(card2);

        var ability = waker.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        // No agent registered → fallback picks first card (card1) → hand;
        // card2 → graveyard.
        _alice.Zones.Hand.GetCards().Should().Contain(card1,
            "first top-2 card goes to hand when no agent registered");
        _alice.Zones.Graveyard.GetCards().Should().Contain(card2,
            "second top-2 card goes to graveyard");
        _alice.Zones.Library.GetCards().Should().NotContain(new ICard[] { card1, card2 },
            "both cards leave the library");
    }

    [Fact]
    public void WakerOfWaves_Resolve_SingleCardInLibrary_GoesToHand()
    {
        var waker = WakerOfWavesFactory.Create(_alice);

        var card = new Sorcery("Time Warp", "3UU");
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);

        var ability = waker.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(card,
            "single library card goes to hand (pick from [card] — only option)");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void WakerOfWaves_Resolve_EmptyLibrary_IsNoOp()
    {
        var waker = WakerOfWavesFactory.Create(_alice);
        // Library is empty — no cards to seed.

        var ability = waker.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "empty library — nothing to put into hand");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
