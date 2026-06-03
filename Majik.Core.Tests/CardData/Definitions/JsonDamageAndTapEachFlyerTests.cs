using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Engine-level coverage for the declarative
/// <c>damage_and_tap_each_flyer_opponents_control</c> verb — the Thundermaw
/// Hellkite ETB "deals 1 damage to each creature with flying your opponents
/// control. Tap those creatures." (CR 109.5 "your opponents", CR 702.9 Flying,
/// CR 701.21a Tap). This is the group-apply form of the single-target
/// <c>deal_damage</c> + <c>tap_target</c> verbs.
///
/// Each test builds a runtime card from inline JSON (an <c>etb_self</c> trigger
/// carrying the new verb), then resolves the ETB ability against a live
/// <see cref="GameContext"/> so the untargeted effect can enumerate opponents'
/// flyers off <c>ctx.Game</c>. The verb must:
/// <list type="bullet">
///   <item>deal N damage to every flyer an OPPONENT controls (CR 109.5),</item>
///   <item>tap those same creatures (CR 701.21a),</item>
///   <item>leave the controller's OWN flyers untouched,</item>
///   <item>leave opponents' NON-flyers untouched (CR 702.9),</item>
///   <item>recognise GRANTED flying (CR 613.1f), not just printed.</item>
/// </list>
/// </summary>
public class JsonDamageAndTapEachFlyerTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private const string ThundermawJson = """
    {
      "name": "Thundermaw Hellkite",
      "types": ["Creature"],
      "subtypes": ["Dragon"],
      "manaCost": "{3}{R}{R}",
      "power": 5,
      "toughness": 5,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "etb_self" },
          "effects": [
            { "type": "damage_and_tap_each_flyer_opponents_control", "amount": 1 }
          ]
        }
      ]
    }
    """;

    private Creature BuildHellkite(Player controller)
    {
        var def = CardDefinitionLoader.FromJson(ThundermawJson);
        var card = (Creature)CardDefinitionFactory.Build(def, controller);
        card.SetOwner(controller);
        card.SetController(controller);
        card.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(card);
        return card;
    }

    private static TriggeredAbility Etb(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>().Single();

    /// <summary>A battlefield creature under <paramref name="owner"/>, optionally
    /// with the printed Flying keyword marker (CR 702.9).</summary>
    private static Creature OnBattlefield(
        Player owner, string name, bool flying, int toughness = 2)
    {
        var c = new Creature(name, "{1}{U}", 1, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        if (flying) c.AddAbility(new KeywordAbility("Flying", c, owner));
        return c;
    }

    private async Task ResolveEtb(TriggeredAbility ability)
    {
        ability.SetChosenTargets(System.Array.Empty<IReadOnlyList<object>>());
        await ability.ResolveAsync(agent: null, game: NewContext());
    }

    [Fact]
    public void Build_ProducesUntargetedEtbAbility()
    {
        var hellkite = BuildHellkite(_alice);

        hellkite.Name.Should().Be("Thundermaw Hellkite");
        hellkite.HasType(CardType.Creature).Should().BeTrue();
        var etb = Etb(hellkite);
        etb.TargetRequests.Should().BeEmpty(
            "the verb is an untargeted group effect (CR 608.2) — no target slot");
    }

    [Fact]
    public async Task Etb_DamagesAndTaps_OpponentFlyer()
    {
        var hellkite = BuildHellkite(_alice);
        var bobFlyer = OnBattlefield(_bob, "Bob Drake", flying: true);

        await ResolveEtb(Etb(hellkite));

        bobFlyer.Damage.Should().Be(1, "an opponent's flyer takes 1 damage (CR 109.5)");
        bobFlyer.IsTapped.Should().BeTrue("'Tap those creatures' taps each flyer hit (CR 701.21a)");
    }

    [Fact]
    public async Task Etb_LeavesOwnFlyer_Untouched()
    {
        var hellkite = BuildHellkite(_alice);
        var aliceFlyer = OnBattlefield(_alice, "Alice Drake", flying: true);

        await ResolveEtb(Etb(hellkite));

        aliceFlyer.Damage.Should().Be(0, "the controller's OWN flyers are not opponents' (CR 109.5)");
        aliceFlyer.IsTapped.Should().BeFalse();
    }

    [Fact]
    public async Task Etb_LeavesOpponentGroundCreature_Untouched()
    {
        var hellkite = BuildHellkite(_alice);
        var bobGround = OnBattlefield(_bob, "Bob Bear", flying: false);

        await ResolveEtb(Etb(hellkite));

        bobGround.Damage.Should().Be(0, "only creatures WITH flying are hit (CR 702.9)");
        bobGround.IsTapped.Should().BeFalse();
    }

    [Fact]
    public async Task Etb_HitsMultipleOpponentFlyers_ButNotGroundOrOwn()
    {
        var hellkite = BuildHellkite(_alice);
        var bobFlyer1 = OnBattlefield(_bob, "Bob Drake 1", flying: true);
        var bobFlyer2 = OnBattlefield(_bob, "Bob Drake 2", flying: true);
        var bobGround = OnBattlefield(_bob, "Bob Bear", flying: false);
        var aliceFlyer = OnBattlefield(_alice, "Alice Drake", flying: true);

        await ResolveEtb(Etb(hellkite));

        bobFlyer1.IsTapped.Should().BeTrue();
        bobFlyer1.Damage.Should().Be(1);
        bobFlyer2.IsTapped.Should().BeTrue();
        bobFlyer2.Damage.Should().Be(1);

        bobGround.IsTapped.Should().BeFalse();
        bobGround.Damage.Should().Be(0);
        aliceFlyer.IsTapped.Should().BeFalse();
        aliceFlyer.Damage.Should().Be(0);
    }

    [Fact]
    public async Task Etb_TogglesNothing_WhenOpponentHasNoFlyers()
    {
        var hellkite = BuildHellkite(_alice);
        var bobGround = OnBattlefield(_bob, "Bob Bear", flying: false);

        // No flyers to hit — resolves cleanly without throwing.
        await ResolveEtb(Etb(hellkite));

        bobGround.Damage.Should().Be(0);
    }

    [Fact]
    public void Factory_BuildsThundermaw_WithFlyingHasteAndEtb()
    {
        var hellkite = ThundermawHellkiteFactory.Create(_alice);

        hellkite.Name.Should().Be("Thundermaw Hellkite");
        hellkite.GetPower().Should().Be(5);
        hellkite.GetToughness().Should().Be(5);
        hellkite.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        hellkite.HasEffectiveKeyword("Flying").Should().BeTrue("CR 702.9");
        hellkite.HasEffectiveKeyword("Haste").Should().BeTrue("CR 702.10");
        hellkite.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the JSON shell carries the declarative ETB trigger");
    }
}
