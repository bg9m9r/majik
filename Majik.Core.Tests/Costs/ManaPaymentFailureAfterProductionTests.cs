using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// ManaPaymentResolver.Pay can fail AFTER it has tapped sources: the payability
/// simulation reads each ability's preview (ManaGenerated), and a dynamic
/// ability can produce something else (Cabal Coffers' printed seed versus its
/// count, a chosen-colour land). By then the mana abilities have resolved and
/// their "tapped for mana" event has been published.
///
/// What happens, pinned here: Pay returns false having spent nothing; the
/// sources stay tapped; everything they produced, and everything that was
/// floating before, stays in the pool; and the cast is reversed (the card goes
/// back to hand, nothing on the stack, no SpellCastEvent). That is the legal
/// outcome under CR 732.1-732.2 (formerly 729): the payments of the illegal
/// action are cancelled, and reversing the mana abilities activated during it
/// is the player's option, so keeping the mana is allowed. The mana empties at
/// the end of the step (CR 500.4) as usual.
/// </summary>
public class ManaPaymentFailureAfterProductionTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Land LyingLand()
    {
        // Previews {R}, produces {G}.
        var land = new Land("Liar's Grove");
        land.SetOwner(_alice); land.SetController(_alice);
        land.AddAbility(new ManaAbility(land, _alice, () => ManaCost.Parse("{G}"),
            printedManaGenerated: ManaCost.Parse("{R}")));
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void Pay_FailingAfterProduction_LeavesTheSourceTapped_AndAllTheManaFloating()
    {
        var land = LyingLand();
        _alice.AddManaToPool(ManaCost.Parse("{U}"));
        var events = new List<ManaAbilityActivatedEvent>();
        _bus.Subscribe<ManaAbilityActivatedEvent>(events.Add);
        var resolver = new ManaPaymentResolver(null, _bus);

        var ok = resolver.Pay(_alice, ManaCost.Parse("{R}"),
            new ManaPayment(new ICard[] { land }));

        ok.Should().BeFalse();
        land.IsTapped.Should().BeTrue("the mana ability resolved; it is not undone");
        events.Should().ContainSingle("the tapped-for-mana event fired as the mana was produced");
        _alice.ManaPool.Green.Should().Be(1, "what the source produced stays in the pool");
        _alice.ManaPool.Blue.Should().Be(1, "nothing was spent");
        _alice.ManaPool.Total.Should().Be(2);
    }

    [Fact]
    public async Task ACastWhosePaymentFailsAfterProduction_IsReversed_AndTheManaStaysFloating()
    {
        var land = LyingLand();
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new SpellCastFlow(stack, new ZoneService(_bus), _bus);
        var bolt = new Instant("Shock", "{R}");
        bolt.SetOwner(_alice); bolt.SetController(_alice);
        bolt.SetZone(ZoneType.Hand); _alice.Zones.Hand.AddCard(bolt);
        var casts = 0;
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.SpellCastEvent>(_ => casts++);
        var def = new SpellDefinition(Array.Empty<string>(), false, Array.Empty<TargetRequest>(),
            _ => Array.Empty<IEffect>());
        var resolver = new ManaPaymentResolver(null, _bus);
        var payment = new ManaPayment(new ICard[] { land });
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);

        var act = () => flow.CastAsync(_alice, bolt, def, new ScriptedAgent(), ctx,
            preChosenMana: payment,
            payManaCost: c => resolver.Pay(_alice, c, payment));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mana payment failed*");
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        stack.IsEmpty.Should().BeTrue();
        casts.Should().Be(0);
        land.IsTapped.Should().BeTrue();
        _alice.ManaPool.Green.Should().Be(1);
    }
}