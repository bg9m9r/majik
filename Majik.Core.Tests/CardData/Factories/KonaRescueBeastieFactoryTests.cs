using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KonaRescueBeastieFactory"/> (Duskmourn, {3}{G}).
/// Legendary Creature — Beast Survivor 4/3. Oracle text (verified against
/// Scryfall 2026-06-24):
///   "Survival — At the beginning of your second main phase, if Kona is tapped,
///    you may put a permanent card from your hand onto the battlefield."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({3}{G} Legendary Creature — Beast Survivor 4/3).
/// - Survival intervening-if (CR 603.4): the trigger only goes on the stack when
///   Kona is tapped (untapped ⇒ no trigger).
/// - Survival resolution: a permanent card moves hand → battlefield (CR 115.2),
///   gated on the "you may".
/// - Non-permanent cards (instant / sorcery) are NOT eligible (CR 110.4a).
/// (Dispatch + well-formedness are covered for every card by
/// CardFactoryContractTests.)
/// </summary>
[Trait("Color", "G")]
public class KonaRescueBeastieFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private static T InHand<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }

    private ResolutionContext MakeCtx(IPlayerAgent? agent)
    {
        var game = new GameContext(
            _alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 1, currentPhase: null, stack: new Majik.Core.Stack.Stack());
        return ResolutionContext.For(_alice, agent, game, chosenTargets: null);
    }

    [Fact]
    public void Identity_LegendaryBeastSurvivor_4_3_AtThreeG()
    {
        var kona = KonaRescueBeastieFactory.Create(_alice);

        kona.Name.Should().Be("Kona, Rescue Beastie");
        kona.ManaCost.Should().Be("{3}{G}");
        kona.Power.Should().Be(4);
        kona.Toughness.Should().Be(3);
        kona.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        kona.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        kona.HasSubtype(CardSubtype.Survivor).Should().BeTrue();
    }

    [Fact]
    public void Survival_InterveningIf_OnlyWhenTapped()
    {
        var kona = KonaRescueBeastieFactory.Create(_alice);
        OnBattlefield(kona, _alice);
        var trigger = kona.Abilities.OfType<TriggeredAbility>().Single();

        // Untapped: the intervening-if (CR 603.4) blocks the trigger from the stack.
        kona.IsTapped.Should().BeFalse();
        trigger.CanBePutOnStack().Should().BeFalse("untapped ⇒ Survival doesn't trigger (CR 603.4)");

        // Tapped: the intervening-if is satisfied.
        kona.Tap();
        trigger.CanBePutOnStack().Should().BeTrue("tapped ⇒ Survival triggers");
    }

    [Fact]
    public async Task Survival_WhenTapped_PutsPermanentCardFromHandOntoBattlefield()
    {
        var kona = KonaRescueBeastieFactory.Create(_alice);
        OnBattlefield(kona, _alice);
        kona.Tap();

        var land = InHand(new Land("Forest"), _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // take the optional "you may"

        await KonaRescueBeastieFactory.ResolveSurvivalAsync(kona, _alice, zoneService: null, MakeCtx(agent));

        land.Zone.Should().Be(ZoneType.Battlefield,
            "the chosen permanent card is put onto the battlefield (CR 115.2)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(land);
        _alice.Zones.Hand.GetCards().Should().NotContain(land);
    }

    [Fact]
    public async Task Survival_Decline_NothingMoves()
    {
        var kona = KonaRescueBeastieFactory.Create(_alice);
        OnBattlefield(kona, _alice);
        kona.Tap();

        var land = InHand(new Land("Forest"), _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the optional "you may"

        await KonaRescueBeastieFactory.ResolveSurvivalAsync(kona, _alice, zoneService: null, MakeCtx(agent));

        land.Zone.Should().Be(ZoneType.Hand, "declining the 'you may' puts nothing onto the battlefield");
    }

    [Fact]
    public async Task Survival_NonPermanentCardsNotEligible_NoOp()
    {
        var kona = KonaRescueBeastieFactory.Create(_alice);
        OnBattlefield(kona, _alice);
        kona.Tap();

        // Only an instant + a sorcery in hand — neither is a permanent card (CR 110.4a).
        var bolt = InHand(new Instant("Lightning Bolt", "{R}"), _alice);
        var divination = InHand(new Sorcery("Divination", "{2}{U}"), _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // would opt in, but there's no eligible card

        await KonaRescueBeastieFactory.ResolveSurvivalAsync(kona, _alice, zoneService: null, MakeCtx(agent));

        bolt.Zone.Should().Be(ZoneType.Hand);
        divination.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Battlefield.GetCards().OfType<Instant>().Should().BeEmpty();
    }

    [Fact]
    public void IsPermanentCard_ClassifiesByType()
    {
        KonaRescueBeastieFactory.IsPermanentCard(new Land("Forest")).Should().BeTrue();
        KonaRescueBeastieFactory.IsPermanentCard(new Creature("Bear", "{1}{G}", 2, 2)).Should().BeTrue();
        KonaRescueBeastieFactory.IsPermanentCard(new Instant("Bolt", "{R}")).Should().BeFalse();
        KonaRescueBeastieFactory.IsPermanentCard(new Sorcery("Divination", "{2}{U}")).Should().BeFalse();
    }
}
