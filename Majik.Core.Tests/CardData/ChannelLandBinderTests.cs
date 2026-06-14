using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
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
    // CR 118.9 — "This ability costs {1} less to activate for each legendary
    // creature you control" cost-reduction rider. The Channel mana cost binds
    // as a DynamicGenericReductionManaCost whose effective generic component
    // drops by the count of legendary creatures the controller controls,
    // never reducing the colored pips and never below zero.
    // -------------------------------------------------------------------

    private const string BoseijuOracleWithRider =
        "{T}: Add {G}.\nChannel — {1}{G}, Discard this card: Destroy target artifact, enchantment, or nonbasic land an opponent controls. That player may search their library for a land card with a basic land type, put it onto the battlefield, then shuffle. This ability costs {1} less to activate for each legendary creature you control.";

    private static Land BindBoseiju(Player owner, ContinuousEffectsService effects)
    {
        var land = new Land("Boseiju, Who Endures") { Owner = owner, Controller = owner };
        LandActivatedAbilityBinder.Bind(
            land, Entity("Boseiju, Who Endures", BoseijuOracleWithRider), owner, effects);
        return land;
    }

    private static DynamicGenericReductionManaCost ChannelDynamicCost(Land land) =>
        land.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DynamicGenericReductionManaCost>().Single();

    private static Creature LegendaryCreature(string name, Player owner)
    {
        var c = new Creature(name, "{G}", 1, 1, supertypes: new[] { CardSupertype.Legendary })
        { Owner = owner, Controller = owner };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Bind_ChannelRider_AttachesDynamicReductionManaCost()
    {
        var land = BindBoseiju(_alice, _effects);

        // The reduction rider makes the Channel mana cost a dynamic cost rather
        // than a plain fixed ManaCostCost.
        var dyn = ChannelDynamicCost(land);
        dyn.BaseCost.Generic.Should().Be(1);
        dyn.BaseCost.Green.Should().Be(1);
    }

    [Fact]
    public void ChannelRider_NoLegendaryCreatures_PaysFullCost()
    {
        var land = BindBoseiju(_alice, _effects);
        var dyn = ChannelDynamicCost(land);

        var effective = dyn.EffectiveCost(_alice);
        effective.Generic.Should().Be(1, "no legendary creatures → no reduction");
        effective.Green.Should().Be(1);
    }

    [Fact]
    public void ChannelRider_OneLegendaryCreature_ReducesGenericByOne()
    {
        var land = BindBoseiju(_alice, _effects);
        LegendaryCreature("Tameshi, Reality Architect", _alice);

        var effective = ChannelDynamicCost(land).EffectiveCost(_alice);
        effective.Generic.Should().Be(0, "{1}{G} reduced by 1 legendary creature → {G}");
        effective.Green.Should().Be(1, "colored pips are never reduced (CR 118.9)");
    }

    [Fact]
    public void ChannelRider_ManyLegendaryCreatures_ClampsGenericAtZero()
    {
        var land = BindBoseiju(_alice, _effects);
        LegendaryCreature("Tameshi", _alice);
        LegendaryCreature("Kotori", _alice);
        LegendaryCreature("Satoru", _alice);

        var effective = ChannelDynamicCost(land).EffectiveCost(_alice);
        effective.Generic.Should().Be(0, "the generic reduction never drops below zero (CR 118.9)");
        effective.Green.Should().Be(1, "the colored {G} pip is never reduced away");
    }

    [Fact]
    public void ChannelRider_NonLegendaryCreatures_DoNotReduce()
    {
        var land = BindBoseiju(_alice, _effects);
        var nonLegend = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(nonLegend);
        // A legendary NON-creature (e.g. the land itself) must not count either.

        var effective = ChannelDynamicCost(land).EffectiveCost(_alice);
        effective.Generic.Should().Be(1, "only legendary CREATURES reduce the cost");
    }

    [Fact]
    public void ChannelRider_CanPay_ReflectsReducedCost()
    {
        var land = BindBoseiju(_alice, _effects);
        var dyn = ChannelDynamicCost(land);

        // Float exactly {G}: not enough for the full {1}{G} cost...
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("G"));
        dyn.CanPay(_alice).Should().BeFalse("only {G} floated, full cost is {1}{G}");

        // ...but with one legendary creature out, the cost reduces to {G}.
        LegendaryCreature("Tameshi", _alice);
        dyn.CanPay(_alice).Should().BeTrue("cost reduced to {G}, which the floated {G} covers");
    }

    [Fact]
    public void ChannelRider_Pay_DrainsReducedCost()
    {
        var land = BindBoseiju(_alice, _effects);
        LegendaryCreature("Tameshi", _alice);
        var dyn = ChannelDynamicCost(land);

        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("G"));
        dyn.Pay(_alice);

        _alice.ManaPool.Total.Should().Be(0, "the reduced {G} cost drained the floated green");
    }

    [Fact]
    public void Bind_ChannelWithoutRider_UsesPlainManaCostCost()
    {
        // The same Boseiju body WITHOUT the reduction sentence binds the plain
        // fixed cost, not the dynamic one (the seam keys strictly off the rider).
        var land = new Land("Boseiju, Who Endures") { Owner = _alice, Controller = _alice };
        LandActivatedAbilityBinder.Bind(
            land,
            Entity("Boseiju, Who Endures",
                "{T}: Add {G}.\nChannel — {1}{G}, Discard this card: Destroy target artifact, enchantment, or nonbasic land an opponent controls."),
            _alice, _effects);

        var channel = land.Abilities.OfType<ActivatedAbility>().Single();
        channel.Costs.OfType<DynamicGenericReductionManaCost>().Should().BeEmpty(
            "no reduction rider → plain fixed mana cost");
        channel.Costs.OfType<ManaCostCost>().Should().ContainSingle();
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

    // -------------------------------------------------------------------
    // Takenuma — RESOLVE behaviour on the production binder-chain path.
    //
    // Current oracle (Scryfall, authoritative): "Mill three cards, then
    // return a creature or planeswalker card from your graveyard to your
    // hand." There is NO "may" — the return is MANDATORY whenever an
    // eligible card exists (CR 608.2c: the spell/ability does as much as
    // it can). The only player decision is WHICH eligible card to return,
    // which the binder must take from the controller's registered agent
    // (CR 608.2 — the controller of the ability makes its choices). These
    // tests exercise the live binder-chain effect body, which the prior
    // binder tests only covered at bind time (cost shape / verb), never at
    // resolve.
    // -------------------------------------------------------------------

    private const string TakenumaOracle =
        "{T}: Add {B}.\nChannel — {3}{B}, Discard this card: Mill three cards, then return a creature or planeswalker card from your graveyard to your hand. This ability costs {1} less to activate for each legendary creature you control.";

    private Land BindTakenuma()
    {
        var land = new Land("Takenuma, Abandoned Mire") { Owner = _alice, Controller = _alice };
        LandActivatedAbilityBinder.Bind(
            land, Entity("Takenuma, Abandoned Mire", TakenumaOracle), _alice, _effects);
        return land;
    }

    [Fact]
    public void Takenuma_Resolve_Mills3_ThenReturnsAgentChosenCreature()
    {
        AgentRegistry.Clear();
        try
        {
            var land = BindTakenuma();

            // Library top → milled into the graveyard, then become eligible
            // return targets (CR 701.13 mill → graveyard).
            var bolt = new Instant("Lightning Bolt", "R");
            var bear = new Creature("Grizzly Bears", "1G", 2, 2);
            var giant = new Creature("Hill Giant", "3R", 3, 3);
            foreach (var c in new ICard[] { bolt, bear, giant })
            {
                c.SetOwner(_alice);
                _alice.Zones.Library.AddCard(c);
            }

            // Agent picks the Hill Giant — proves the binder honoured the
            // agent's choice rather than the first eligible card (the Bear,
            // milled first). Eligible = the two milled creatures.
            var agent = new Mock<IPlayerAgent>();
            agent.Setup(a => a.ChooseLibraryPickAsync(
                    It.IsAny<Majik.Core.Game.GameContext?>(),
                    It.IsAny<IReadOnlyList<ICard>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ICard?)giant);
            AgentRegistry.Set(_alice, agent.Object);

            land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

            _alice.Zones.Hand.GetCards().Should().Contain(giant,
                "the agent picked the Hill Giant to return to hand");
            _alice.Zones.Hand.GetCards().Should().NotContain(bear,
                "only the chosen card returns; the Bear stays in the graveyard");
            _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bolt, bear },
                "the milled non-chosen cards remain in the graveyard");
            _alice.Zones.Library.GetCards().Should().BeEmpty("all three top cards were milled");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void Takenuma_Resolve_ReturnIsMandatory_AgentCannotDecline()
    {
        // CR 608.2c — Takenuma's return has no "may"; when an eligible
        // creature/planeswalker card is in the graveyard the effect MUST
        // return one. Even an agent that returns null (declines) is forced
        // to a return: the optional-decline path of ChooseLibraryPickAsync
        // does not apply to this mandatory clause.
        AgentRegistry.Clear();
        try
        {
            var land = BindTakenuma();

            var bear = new Creature("Grizzly Bears", "1G", 2, 2);
            bear.SetOwner(_alice);
            _alice.Zones.Library.AddCard(bear);

            var agent = new Mock<IPlayerAgent>();
            agent.Setup(a => a.ChooseLibraryPickAsync(
                    It.IsAny<Majik.Core.Game.GameContext?>(),
                    It.IsAny<IReadOnlyList<ICard>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ICard?)null);
            AgentRegistry.Set(_alice, agent.Object);

            land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

            _alice.Zones.Hand.GetCards().Should().Contain(bear,
                "the return is mandatory (no \"may\"); the lone eligible card is returned even when the agent declines");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void Takenuma_Resolve_NoEligibleCardInGraveyard_NothingReturned()
    {
        // CR 608.2c — no creature/planeswalker card to return → the return
        // half simply does nothing (the mill still happened).
        AgentRegistry.Clear();
        try
        {
            var land = BindTakenuma();

            var bolt = new Instant("Lightning Bolt", "R");
            var wrath = new Sorcery("Wrath of God", "2WW");
            foreach (var c in new ICard[] { bolt, wrath })
            {
                c.SetOwner(_alice);
                _alice.Zones.Library.AddCard(c);
            }

            land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

            _alice.Zones.Hand.GetCards().Should().BeEmpty(
                "no creature/planeswalker among the milled cards → nothing returns");
            _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bolt, wrath },
                "the milled cards stay in the graveyard");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }
}
