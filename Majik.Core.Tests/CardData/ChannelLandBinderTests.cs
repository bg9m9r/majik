using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the Channel (CR 702.74) activation-cost seam in
/// <see cref="LandActivatedAbilityBinder"/>.
///
/// <para>The Channel cycle (Boseiju Who Endures, Otawara, Takenuma, Eiganjo,
/// Sokenzan) prints its ability as
/// <c>Channel — {cost}, Discard this card: &lt;effect&gt;</c>: a
/// discard-from-HAND activation (CR 702.74a), NOT a battlefield {T}
/// activation. Lands are never routed through their <c>[CardName]</c> factory
/// in production (the deck-build path gates the factory swap on
/// <c>!HasType(Land)</c>), so the Channel ability MUST bind through the binder
/// chain to fire on the live table. This is the seam these tests exercise.</para>
/// </summary>
public class ChannelLandBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly ContinuousEffectsService _effects = new();

    private static CardEntity Entity(string name, string oracle, string typeLine = "Legendary Land")
        => new() { Name = name, TypeLine = typeLine, OracleText = oracle };

    // -------------------------------------------------------------------
    // Each cycle member binds exactly one Channel ActivatedAbility whose
    // cost is ManaCostCost + DiscardSelfCost (the hand-zone activation seam).
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Boseiju, Who Endures",
        "{T}: Add {G}.\nChannel — {1}{G}, Discard this card: Destroy target artifact, enchantment, or nonbasic land an opponent controls. That player may search their library for a land card with a basic land type, put it onto the battlefield, then shuffle. This ability costs {1} less to activate for each legendary creature you control.")]
    [InlineData("Otawara, Soaring City",
        "{T}: Add {U}.\nChannel — {3}{U}, Discard this card: Return target artifact, creature, enchantment, or planeswalker to its owner's hand. This ability costs {1} less to activate for each legendary creature you control.")]
    [InlineData("Takenuma, Abandoned Mire",
        "{T}: Add {B}.\nChannel — {3}{B}, Discard this card: Mill three cards, then return a creature or planeswalker card from your graveyard to your hand. This ability costs {1} less to activate for each legendary creature you control.")]
    [InlineData("Eiganjo, Seat of the Empire",
        "{T}: Add {W}.\nChannel — {2}{W}, Discard this card: It deals 4 damage to target attacking or blocking creature. This ability costs {1} less to activate for each legendary creature you control.")]
    [InlineData("Sokenzan, Crucible of Defiance",
        "{T}: Add {R}.\nChannel — {3}{R}, Discard this card: Create two 1/1 colorless Spirit creature tokens. They gain haste until end of turn. This ability costs {1} less to activate for each legendary creature you control.")]
    public void Bind_ChannelLand_AttachesOneChannelAbilityWithDiscardSelfCost(string name, string oracle)
    {
        var land = new Land(name) { Owner = _alice, Controller = _alice };

        var bound = LandActivatedAbilityBinder.Bind(land, Entity(name, oracle), _alice, _effects);

        bound.Should().BeTrue();
        var channel = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        channel.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1,
            "Channel discards the card from hand as part of the cost (CR 702.74a)");
        channel.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "the Channel mana cost binds alongside the discard-self cost");
    }

    [Fact]
    public void Bind_Boseiju_ChannelManaCostIs1G()
    {
        var land = new Land("Boseiju, Who Endures") { Owner = _alice, Controller = _alice };
        LandActivatedAbilityBinder.Bind(
            land,
            Entity("Boseiju, Who Endures",
                "{T}: Add {G}.\nChannel — {1}{G}, Discard this card: Destroy target artifact, enchantment, or nonbasic land an opponent controls."),
            _alice, _effects);

        var channel = land.Abilities.OfType<ActivatedAbility>().Single();
        var mana = channel.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1);
        mana.Green.Should().Be(1);
    }

    [Fact]
    public void Bind_Eiganjo_ChannelManaCostIs2W()
    {
        var land = new Land("Eiganjo, Seat of the Empire") { Owner = _alice, Controller = _alice };
        LandActivatedAbilityBinder.Bind(
            land,
            Entity("Eiganjo, Seat of the Empire",
                "{T}: Add {W}.\nChannel — {2}{W}, Discard this card: It deals 4 damage to target attacking or blocking creature."),
            _alice, _effects);

        var channel = land.Abilities.OfType<ActivatedAbility>().Single();
        var mana = channel.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2);
        mana.White.Should().Be(1);
    }

    // -------------------------------------------------------------------
    // The DiscardSelfCost gates activation to the HAND zone (CR 702.74a).
    // -------------------------------------------------------------------

    [Fact]
    public void ChannelAbility_DiscardSelfCost_PayableOnlyFromHand()
    {
        var land = new Land("Otawara, Soaring City") { Owner = _alice, Controller = _alice };
        LandActivatedAbilityBinder.Bind(
            land,
            Entity("Otawara, Soaring City",
                "{T}: Add {U}.\nChannel — {3}{U}, Discard this card: Return target artifact, creature, enchantment, or planeswalker to its owner's hand."),
            _alice, _effects);
        var discard = land.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardSelfCost>().Single();

        // In hand → payable.
        _alice.Zones.Hand.AddCard(land);
        discard.CanPay(_alice).Should().BeTrue();

        // On battlefield → not payable (Channel is a hand-zone activation).
        _alice.Zones.Hand.RemoveCard(land);
        _alice.Zones.Battlefield.AddCard(land);
        discard.CanPay(_alice).Should().BeFalse(
            "Channel abilities activate from the Hand zone only (CR 702.74a)");
    }

    // -------------------------------------------------------------------
    // Effect-body descriptions carry the verb the semantic audit looks for
    // so each cycle member stops tripping the missing-effect detector.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Boseiju, Who Endures",
        "{T}: Add {G}.\nChannel — {1}{G}, Discard this card: Destroy target artifact, enchantment, or nonbasic land an opponent controls.",
        "destroy")]
    [InlineData("Otawara, Soaring City",
        "{T}: Add {U}.\nChannel — {3}{U}, Discard this card: Return target artifact, creature, enchantment, or planeswalker to its owner's hand.",
        "hand")]
    [InlineData("Takenuma, Abandoned Mire",
        "{T}: Add {B}.\nChannel — {3}{B}, Discard this card: Mill three cards, then return a creature or planeswalker card from your graveyard to your hand.",
        "mill")]
    [InlineData("Eiganjo, Seat of the Empire",
        "{T}: Add {W}.\nChannel — {2}{W}, Discard this card: It deals 4 damage to target attacking or blocking creature.",
        "damage")]
    [InlineData("Sokenzan, Crucible of Defiance",
        "{T}: Add {R}.\nChannel — {3}{R}, Discard this card: Create two 1/1 colorless Spirit creature tokens. They gain haste until end of turn.",
        "token")]
    public void Bind_ChannelEffect_DescriptionCarriesVerb(string name, string oracle, string verb)
    {
        var land = new Land(name) { Owner = _alice, Controller = _alice };
        LandActivatedAbilityBinder.Bind(land, Entity(name, oracle), _alice, _effects);

        var effect = land.Abilities.OfType<ActivatedAbility>().Single().Effects.Single();
        effect.Description.Should().Contain(verb,
            $"the {name} Channel effect description must name its '{verb}' verb so the audit recognises it");
    }

    // -------------------------------------------------------------------
    // Non-Channel lands and the bare mana line are unaffected.
    // -------------------------------------------------------------------

    [Fact]
    public void Bind_PlainManaLand_NoChannelAbility()
    {
        var land = new Land("Forest", subtypes: new[] { CardSubtype.Forest }) { Owner = _alice, Controller = _alice };
        var bound = LandActivatedAbilityBinder.Bind(
            land, Entity("Forest", "{T}: Add {G}.", "Basic Land — Forest"), _alice, _effects);

        bound.Should().BeFalse();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
