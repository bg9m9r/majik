using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Integration coverage for <see cref="ActivateLoyaltyAbilityCommand"/> end-
/// to-end through <see cref="GameFacade"/>: submitting the command must run
/// the engine's loyalty dispatcher (TurnDriver.DispatchLoyalty), which pays
/// the loyalty cost as the ability goes on the stack (CR 606.3/606.5), marks
/// the once-per-turn flag (CR 606.3), and resolves the loyalty effect off the
/// stack (CR 608) once both players pass.
/// </summary>
public class ActivateLoyaltyAbilityFacadeTests
{
    // Maps a seeded loyalty ability's Id to a 1-element flag the effect flips
    // on resolution, so the test can assert the effect ran off the stack.
    private static readonly Dictionary<Guid, bool[]> _captures = new();

    private static Planeswalker SeedPlaneswalker(
        Player owner, int startingLoyalty, out LoyaltyAbility plus1)
    {
        var pw = new Planeswalker("Test Walker", "{1}{B}{G}", startingLoyalty);
        pw.SetOwner(owner);
        pw.SetController(owner);
        // A simple, target-free +1 whose effect is observable: flip a flag.
        var capture = new bool[1];
        plus1 = new LoyaltyAbility(pw, +1, () => capture[0] = true);
        pw.AddAbility(plus1);
        owner.Zones.Battlefield.AddCard(pw);
        _captures[plus1.Id] = capture;
        return pw;
    }

    [Fact]
    public async Task ActivatePlus1_PaysLoyaltyCost_MarksOncePerTurn_ResolvesEffect()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var grist = SeedPlaneswalker(facade.Alice, startingLoyalty: 3, out var plus1);

        await facade.StartAsync();

        // Game starts in Alice's PreCombatMain with an empty stack — the
        // sorcery-speed window loyalty abilities require (CR 606.3).
        facade.GetState().Phase.Should().Be(StepStateType.PreCombatMain.ToString());

        await facade.SubmitAsync(new ActivateLoyaltyAbilityCommand(grist.InstanceId, plus1.Id)
        {
            PlayerId = facade.Alice.Id,
        });

        // CR 606.3/606.5 — the +1 cost is paid as the ability is put on the
        // stack, BEFORE the effect resolves. Loyalty 3 → 4 immediately.
        grist.Loyalty.Should().Be(4, "CR 606.3 — +1 loyalty cost is paid on announcement.");
        grist.LoyaltyAbilityActivatedThisTurn.Should().BeTrue(
            "CR 606.3 — once per planeswalker per turn.");

        // The ability is now on the stack; the effect has not resolved yet.
        _captures[plus1.Id][0].Should().BeFalse("the effect resolves off the stack, not on announcement.");

        // Both players pass → the loyalty ability resolves (CR 608).
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Alice.Id });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Bob.Id });

        _captures[plus1.Id][0].Should().BeTrue("CR 608 — the loyalty effect resolved off the stack.");
    }

    [Fact]
    public async Task ActivateLoyalty_OncePerTurn_SecondActivationRejectedByWireGate()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var grist = SeedPlaneswalker(facade.Alice, startingLoyalty: 3, out var plus1);

        await facade.StartAsync();

        await facade.SubmitAsync(new ActivateLoyaltyAbilityCommand(grist.InstanceId, plus1.Id)
        {
            PlayerId = facade.Alice.Id,
        });
        grist.LoyaltyAbilityActivatedThisTurn.Should().BeTrue();

        // CR 606.3 — a second loyalty activation this turn is not offered:
        // LoyaltyAbility.CanActivate() returns false (once-per-turn), so
        // PriorityKinds drops ActivateLoyaltyAbilityCommand and the RemoteAgent
        // wire pre-check rejects the resubmission up front.
        var act = async () => await facade.SubmitAsync(
            new ActivateLoyaltyAbilityCommand(grist.InstanceId, plus1.Id)
            {
                PlayerId = facade.Alice.Id,
            });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
