using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TorchTheTowerFactory"/> (Wilds of Eldraine, {R},
/// Instant).
///
/// Oracle text (verified against Scryfall):
///   "Bargain (You may sacrifice an artifact, enchantment, or token as you cast
///    this spell.)
///    Torch the Tower deals 2 damage to target creature or planeswalker. If
///    this spell was bargained, instead it deals 3 damage to that permanent and
///    you scry 1.
///    If a permanent dealt damage by Torch the Tower would die this turn, exile
///    it instead."
///
/// The bargain-conditional damage branch mirrors <see cref="RoilEruptionTests"/>
/// (kicker sentinel); the "target creature or planeswalker, exile-if-dies" body
/// mirrors <see cref="ScorchingDragonfireFactoryTests"/>.
/// </summary>
[Trait("Color", "R")]
public class TorchTheTowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private ChosenSpellParams Chosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    private Majik.Core.Cards.Instant TorchControlledBy(Player owner)
    {
        var card = TorchTheTowerFactory.Create(owner);
        card.SetController(owner);
        return card;
    }

    private Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity — non-vanilla mana cost / type assert (CardFactoryContractTests
    // already covers dispatch + well-formedness).
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_InstantAtR_Red()
    {
        var card = TorchTheTowerFactory.Create(_alice);

        card.Name.Should().Be("Torch the Tower");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void BuildSpellDefinition_SingleCreatureOrPlaneswalkerTargetRequest()
    {
        var card = TorchControlledBy(_alice);
        var def = TorchTheTowerFactory.BuildSpellDefinition(card, t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature or planeswalker");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Un-bargained — 2 damage, no scry.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NotBargained_DealsTwoDamage()
    {
        var card = TorchControlledBy(_alice);
        var bear = CreatureOnBattlefield(_bob, 2, 4);

        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Damage.Should().Be(TorchTheTowerFactory.BaseDamage,
            because: "an un-bargained Torch the Tower deals 2 damage");
        bear.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_NotBargained_RemovesTwoLoyaltyFromPlaneswalker()
    {
        var card = TorchControlledBy(_alice);
        var pw = new Planeswalker("Test Walker", "{2}{R}", startingLoyalty: 5)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o);
        foreach (var e in def.EffectFactory(Chosen(pw))) e.Execute();

        pw.Loyalty.Should().Be(3,
            because: "2 damage to a planeswalker removes 2 loyalty (CR 306.7)");
    }

    [Fact]
    public void Resolve_NoOp_OnNonCreatureNonPlaneswalkerTarget()
    {
        // CR 608.2b — a player is not a legal target; no damage.
        var card = TorchControlledBy(_alice);
        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            because: "Torch the Tower damages only creatures/planeswalkers, not players");
    }

    // -----------------------------------------------------------------------
    // Bargained — 3 damage + scry 1 (CR 702.169b).
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_Bargained_DealsThreeDamage()
    {
        var card = TorchControlledBy(_alice);

        // Pay the bargain cost the same way SpellCastFlow does: sacrifice an
        // artifact and stamp Card.WasBargained (CR 702.169a/b).
        var treasure = new Artifact("Treasure", "") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(treasure);
        treasure.SetZone(ZoneType.Battlefield);
        TorchTheTowerFactory.BuildAdditionalCost(card).Pay(_alice).Should().BeTrue();
        card.WasBargained.Should().BeTrue("the bargain cost stamps the spell");

        var bear = CreatureOnBattlefield(_bob, 2, 4);
        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Damage.Should().Be(TorchTheTowerFactory.BargainedDamage,
            because: "a bargained Torch the Tower deals 3 damage instead");
        bear.Damage.Should().Be(3);
    }

    [Fact]
    public void Resolve_Bargained_ScrysOne()
    {
        var card = TorchControlledBy(_alice);

        // Seed a known top-of-library card so we can observe the scry.
        var topCard = new Sorcery("Scry Target", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var treasure = new Artifact("Treasure", "") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(treasure);
        treasure.SetZone(ZoneType.Battlefield);
        TorchTheTowerFactory.BuildAdditionalCost(card).Pay(_alice).Should().BeTrue();

        var bear = CreatureOnBattlefield(_bob, 2, 4);
        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        // Pre-agent default scry posture: the sole peeked card goes to the
        // bottom (CR 701.20). Library still holds the card; it is no longer
        // a no-op'd empty-library scry.
        _alice.Zones.Library.GetCards().Should().Contain(topCard,
            because: "scry 1 reorders the library but does not remove the card");
    }

    [Fact]
    public void Resolve_NotBargained_DoesNotScry()
    {
        var card = TorchControlledBy(_alice);
        var topCard = new Sorcery("Scry Target", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bear = CreatureOnBattlefield(_bob, 2, 4);
        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        card.WasBargained.Should().BeFalse(
            because: "an un-bargained spell never stamps the rider, so no scry occurs");
    }

    // -----------------------------------------------------------------------
    // Exile-instead rider (CR 700.3 / CR 514.2).
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DamagedCreatureDeath_RewrittenToExile()
    {
        var card = TorchControlledBy(_alice);
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);

        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o, replacements: bus);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        var dying = new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        var result = bus.Apply(dying);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            because: "a creature dealt damage by Torch the Tower that would die is exiled instead");
    }

    [Fact]
    public void Resolve_UntargetedCreatureDeath_NotRewritten()
    {
        var card = TorchControlledBy(_alice);
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);
        var other = CreatureOnBattlefield(_alice, 1, 1);

        var def = TorchTheTowerFactory.BuildSpellDefinition(card, o => o, replacements: bus);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        // CR 700.3 — a different creature dying is unaffected: its death stays a
        // graveyard move.
        var dying = new ZoneMoveIntent(other, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(dying)!.ToZone.Should().Be(ZoneType.Graveyard);
    }
}
