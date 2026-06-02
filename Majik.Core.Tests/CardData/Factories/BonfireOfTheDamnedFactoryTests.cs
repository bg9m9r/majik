using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BonfireOfTheDamnedFactory"/>
/// (Avacyn Restored, {X}{X}{R}).
///
/// Sorcery. Oracle text:
///   "Bonfire of the Damned deals X damage to target player or
///    planeswalker and each creature that player or that planeswalker's
///    controller controls.
///    Miracle {X}{R}."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Miracle keyword marker (primitive deferred — class xmldoc).
///   - SpellDefinition has HasVariableX=true + one "target player or
///     planeswalker" request.
///   - X damage to Player target + X damage to each creature that
///     player controls.
///   - X damage to Planeswalker (loyalty) + X damage to each creature
///     the PW's controller controls.
///   - X = 0 — primary + sweep both no-op (CR 119.2 guards in Fx).
/// </summary>
[Trait("Color", "R")]
public class BonfireOfTheDamnedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // Identity resolver — no token mapping needed; the chosen target is
    // already the live object in these tests (no DB hop).
    private static object IdentityResolver(object t) => t;

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsSorceryShape_XXR()
    {
        var bonfire = BonfireOfTheDamnedFactory.Create(_alice);

        bonfire.Should().BeOfType<Sorcery>();
        bonfire.Name.Should().Be("Bonfire of the Damned");
        bonfire.ManaCost.Should().Be("{X}{X}{R}");
        bonfire.HasType(CardType.Sorcery).Should().BeTrue();
        bonfire.Owner.Should().BeSameAs(_alice);
        bonfire.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Create_AttachesMiracleKeywordMarker()
    {
        var bonfire = BonfireOfTheDamnedFactory.Create(_alice);

        bonfire.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Miracle",
                "Miracle primitive is deferred — marker surfaces the keyword");
    }

    // -------------------------------------------------------------------------
    // SpellDefinition shape
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_HasVariableX_AndPlayerOrPlaneswalkerTarget()
    {
        var def = BonfireOfTheDamnedFactory.BuildSpellDefinition(_alice, IdentityResolver);

        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target player or planeswalker");
    }

    // -------------------------------------------------------------------------
    // Resolution — Player target
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_PlayerTarget_DealsXToPlayerAndSweepsXEachCreatureControlled()
    {
        var def = BonfireOfTheDamnedFactory.BuildSpellDefinition(_alice, IdentityResolver);

        // Bob controls two creatures.
        var ape = new Creature("Test Ape", "{2}{G}", 3, 3);
        var elk = new Creature("Test Elk", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, ape);
        PutOnBattlefield(_bob, elk);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty);

        var bobLifeBefore = _bob.LifeTotal;

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 2,
            "primary damage — X=2 to the target player");
        ape.Damage.Should().Be(2, "sweep — X=2 to each creature Bob controls");
        elk.Damage.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Resolution — Planeswalker target
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_PlaneswalkerTarget_RemovesLoyalty_AndSweepsControllerCreatures()
    {
        var def = BonfireOfTheDamnedFactory.BuildSpellDefinition(_alice, IdentityResolver);

        var jace = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3);
        PutOnBattlefield(_bob, jace);

        var golem = new Creature("Test Golem", "{4}", 4, 4);
        PutOnBattlefield(_bob, golem);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { jace } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        jace.Loyalty.Should().Be(1,
            "Planeswalker damage routes to loyalty (CR 306.7) — 3 - 2 = 1");
        golem.Damage.Should().Be(2,
            "sweep targets each creature the planeswalker's controller controls");
    }

    // -------------------------------------------------------------------------
    // X = 0 — primary + sweep are no-ops
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_XZero_NoOp()
    {
        var def = BonfireOfTheDamnedFactory.BuildSpellDefinition(_alice, IdentityResolver);

        var beast = new Creature("Test Beast", "{2}{G}", 3, 3);
        PutOnBattlefield(_bob, beast);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 0,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty);

        var bobLifeBefore = _bob.LifeTotal;
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore);
        beast.Damage.Should().Be(0);
    }
}
