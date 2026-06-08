using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// STAGE 3 (re-sourceable abilities) — the data-driven (CardDef/JSON) self-source
/// activated-ability verbs (pump / connive / explore) were migrated to read their
/// subject off <see cref="ResolutionContext.Source"/> (CR 113.7) instead of the
/// authoring card captured in a closure. Two properties are proven per verb:
///
/// <list type="number">
///   <item><b>Normal play unchanged</b> — the SAME effect, resolved with the
///     ability's own source (the card itself), still affects that card on the
///     battlefield. (Effect.Execute uses the legacy null-context path which
///     falls back to the captured card.)</item>
///   <item><b>Re-home via RebindTo</b> — when the ability is re-sourced onto a
///     different bearer (the Agatha grant mechanism), resolving the rebound copy
///     affects the BEARER, because the effect reads
///     <see cref="ResolutionContext.Source"/> which is now the bearer.</item>
/// </list>
///
/// These three verbs were the only CardDef activated-ability verbs that captured
/// the source card as their SUBJECT; every other verb is scoped to the
/// controller or to chosen targets and already re-homes. That is what lets
/// <see cref="CardDefActivatedAbility"/> mark its output
/// <see cref="ActivatedAbility.RebindSafe"/>.
/// </summary>
public class CardDefRebindSourceTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>Build a real Creature from a CardDef carrying a single
    /// data-driven activated ability ({R}: ...self verb...).</summary>
    private static Creature BuildCreatureWith(
        Player owner, string name, int power, int toughness, EffectDefinition selfVerb)
    {
        var abilityDef = new ActivatedAbilityDefinition
        {
            Costs = { new ManaCostDef { Amount = "{R}" } },
            Effects = { selfVerb },
        };
        var def = CardDef.Creature(name, "R", power, toughness)
            .WithAbility(abilityDef.ToCardDefAbility())
            .Build();
        return (Creature)CardDefRuntime.Build(def, owner);
    }

    private static ActivatedAbility ActivatedOf(Creature c)
        => c.Abilities.OfType<ActivatedAbility>().Single(a => a is not IManaAbility);

    // -----------------------------------------------------------------------
    // The provenance flag itself.
    // -----------------------------------------------------------------------

    [Fact]
    public void CardDefActivatedAbility_IsMarkedRebindSafe()
    {
        var card = BuildCreatureWith(_alice, "Firey Dork", 2, 2,
            new PumpSelfEffectDef { Power = 1, Toughness = 0 });

        ActivatedOf(card).RebindSafe.Should().BeTrue(
            "every data-driven CardDef activated ability reads its source off the "
            + "ResolutionContext, so it is sound to re-home via RebindTo");
    }

    // -----------------------------------------------------------------------
    // pump_self — firebreathing.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PumpSelf_NormalPlay_PumpsTheCardItself()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        var dork = BuildCreatureWith(_alice, "Firey Dork", 2, 2,
            new PumpSelfEffectDef { Power = 1, Toughness = 0 });
        dork.SetZone(ZoneType.Battlefield);
        dork.ActiveEffects = effects;

        var powerBefore = dork.GetPower();
        // Resolve through the real ability path so ResolutionContext.Source = dork.
        await ActivatedOf(dork).ResolveAsync(agent: null, game: null);

        dork.GetPower().Should().Be(powerBefore + 1,
            "the firebreathing pumps the source creature on the normal battlefield path");
    }

    [Fact]
    public async Task PumpSelf_RebindToBearer_PumpsBearer_NotOriginalCard()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        var dork = BuildCreatureWith(_alice, "Firey Dork", 2, 2,
            new PumpSelfEffectDef { Power = 1, Toughness = 0 });
        // The original card is NOT on the battlefield (exiled, in Agatha terms).
        dork.SetZone(ZoneType.Exile);

        var bearer = new Creature("Bearer", "1G", 3, 3) { Controller = _alice };
        bearer.SetZone(ZoneType.Battlefield);
        bearer.ActiveEffects = effects;

        var rebound = ActivatedOf(dork).RebindTo(bearer, _alice);
        rebound.RebindSafe.Should().BeTrue("RebindTo preserves the provenance flag");

        var bearerPowerBefore = bearer.GetPower();
        await rebound.ResolveAsync(agent: null, game: null);

        bearer.GetPower().Should().Be(bearerPowerBefore + 1,
            "the re-homed firebreathing pumps the BEARER (ResolutionContext.Source = bearer)");
        dork.GetPower().Should().Be(2,
            "the exiled original card is untouched (and isn't on the battlefield anyway)");
    }

    // -----------------------------------------------------------------------
    // explore_self.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExploreSelf_RebindToBearer_ExploresBearer()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        ZoneServiceRegistry.Set(_alice, zones);
        try
        {
            var explorer = BuildCreatureWith(_alice, "Explorer Dork", 1, 1,
                new ExploreSelfEffectDef { Count = 1 });
            explorer.SetZone(ZoneType.Exile);

            // A nonland on top of the library → explore puts a +1/+1 counter on
            // the exploring permanent (CR 701.40c).
            var topNonland = new Creature("Top Bear", "1G", 2, 2);
            topNonland.SetOwner(_alice);
            _alice.Zones.Library.AddCard(topNonland);
            topNonland.SetZone(ZoneType.Library);

            var bearer = new Creature("Bearer", "1G", 3, 3);
            bearer.SetOwner(_alice);
            bearer.ChangeController(_alice);
            bearer.SetZone(ZoneType.Battlefield);

            var rebound = ActivatedOf(explorer).RebindTo(bearer, _alice);
            await rebound.ResolveAsync(agent: null, game: null);

            bearer.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne)
                .Should().Be(1,
                    "the re-homed explore puts the +1/+1 counter on the BEARER (CR 701.40c)");
            explorer.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne)
                .Should().Be(0, "the exiled original explorer is untouched");
        }
        finally
        {
            ZoneServiceRegistry.Remove(_alice);
        }
    }
}
