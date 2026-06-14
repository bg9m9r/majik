using System.Text.Json;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the declarative <c>grant_keyword_until_eot_group</c> verb
/// (<see cref="GrantKeywordToCreaturesYouControlEffectDef"/>, CR 613.1c /
/// CR 514.2) — the GROUP-apply sibling of the single-target
/// <c>grant_keyword_until_eot_target</c> verb. It is the declarative form of
/// the regex-bound <c>LandActivatedAbilityBinder.BindGrantKeywordsToCreaturesYouControl</c>
/// primitive (Vault of the Archangel's "{2}{W}{B}, {T}: Creatures you control
/// gain deathtouch and lifelink until end of turn").
///
/// <para>At resolution the verb walks the activating player's battlefield
/// (CR 611.2c — a one-shot, resolution-time snapshot; creatures that enter
/// later this turn are not retroactively granted) and registers one
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/> per keyword on each
/// creature's OWN <see cref="ContinuousEffectsService"/>, so the grants
/// auto-expire at cleanup (CR 514.2). Opponent creatures are untouched — the
/// controller's Battlefield zone scopes "you control".</para>
///
/// <para>This is untargeted (self-selecting "creatures you control"), so
/// <see cref="EffectDefinition.ToTargetRequest"/> returns <c>null</c>.</para>
/// </summary>
public class JsonGrantKeywordGroupEffectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    private Creature BattlefieldCreature(Player owner, string name, ContinuousEffectsService fx)
    {
        var c = new Creature(name, "{G}", 2, 2) { Owner = owner, Controller = owner };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = fx;
        return c;
    }

    private static async Task ResolveAsync(EffectDefinition def, ICard host, Player controller)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            def, card: host, controller: controller, replacements: null);
        var ctx = ResolutionContext.For(controller, agent: null, game: null, chosenTargets: null);
        await effect.ExecuteAsync(ctx);
    }

    [Fact]
    public void GroupGrant_IsUntargeted()
    {
        var def = new GrantKeywordToCreaturesYouControlEffectDef
        {
            Keywords = new() { "Deathtouch", "Lifelink" },
        };
        def.ToTargetRequest().Should().BeNull(
            "the group grant self-selects 'creatures you control'; it does not target");
    }

    [Fact]
    public async Task GroupGrant_GrantsAllKeywords_ToControllerCreaturesOnly_UntilEndOfTurn()
    {
        var fx = new ContinuousEffectsService(_bus);
        var mine = BattlefieldCreature(_alice, "Bear", fx);
        var theirs = BattlefieldCreature(_bob, "Hill Giant", fx);
        var host = new Land("Vault of the Archangel", supertypes: null, subtypes: null)
        { Owner = _alice, Controller = _alice };

        CombatAbilities.HasDeathtouch(mine).Should().BeFalse();

        await ResolveAsync(
            new GrantKeywordToCreaturesYouControlEffectDef
            { Keywords = new() { "Deathtouch", "Lifelink" } },
            host: host, controller: _alice);

        CombatAbilities.HasDeathtouch(mine).Should().BeTrue(
            "CR 613.1c — creatures you control gain deathtouch until end of turn");
        CombatAbilities.HasLifelink(mine).Should().BeTrue(
            "CR 613.1c — creatures you control gain lifelink until end of turn");

        CombatAbilities.HasDeathtouch(theirs).Should().BeFalse(
            "only the activating player's creatures are affected");
        CombatAbilities.HasLifelink(theirs).Should().BeFalse(
            "only the activating player's creatures are affected");

        fx.ExpireEndOfTurn(); // CR 514.2
        CombatAbilities.HasDeathtouch(mine).Should().BeFalse("the grant ends at cleanup");
        CombatAbilities.HasLifelink(mine).Should().BeFalse("the grant ends at cleanup");
    }

    [Fact]
    public async Task GroupGrant_NoCreatures_NoOp()
    {
        var host = new Land("Vault of the Archangel", supertypes: null, subtypes: null)
        { Owner = _alice, Controller = _alice };

        // No creatures on the battlefield — must resolve cleanly.
        var act = async () => await ResolveAsync(
            new GrantKeywordToCreaturesYouControlEffectDef { Keywords = new() { "Trample" } },
            host: host, controller: _alice);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GroupGrant_RoundTripsThroughJson()
    {
        EffectDefinition def = new GrantKeywordToCreaturesYouControlEffectDef
        {
            Keywords = new() { "Deathtouch", "Lifelink" },
        };

        var json = JsonSerializer.Serialize(def);
        json.Should().Contain("grant_keyword_until_eot_group",
            "the polymorphic discriminator is emitted");

        var back = JsonSerializer.Deserialize<EffectDefinition>(json);
        back.Should().BeOfType<GrantKeywordToCreaturesYouControlEffectDef>();
        ((GrantKeywordToCreaturesYouControlEffectDef)back!).Keywords
            .Should().BeEquivalentTo("Deathtouch", "Lifelink");
    }
}
