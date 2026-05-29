using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ForbiddingWatchtowerFactory"/> (Urza's Saga / Tenth
/// Edition mono-white manland). Land:
///   "This land enters tapped.
///    {T}: Add {W}.
///    {1}{W}: This land becomes a 1/5 white Soldier creature until end of
///    turn. It's still a land."
///
/// Near-twin of <see cref="CaveOfTheFrostDragonFactory"/> (white manland:
/// {T}: Add {W} from JSON + an activated animate-until-EOT that adds Creature
/// type, a subtype, and a base P/T on Layers 4/7b) — but:
///   - enters tapped <em>unconditionally</em> (no "two or more other lands"
///     predicate) — applied on the production load path by
///     <see cref="EntersTappedBinder"/>, so this factory wires no replacement;
///   - animates to a 1/5 <see cref="CardSubtype.Soldier"/> body with no
///     evasion keyword;
///   - the animate cost is {1}{W}.
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {W} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({1}{W}, instant speed) + Layer 4 / Layer 7b
///   continuous effects:
///     * Adds Creature type + Soldier subtype on Layer 4 (no Flying).
///     * Records 1/5 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// </summary>
public class ForbiddingWatchtowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ForbiddingWatchtower_Identity()
    {
        var land = ForbiddingWatchtowerFactory.Create(_alice);

        land.Name.Should().Be("Forbidding Watchtower");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Forbidding Watchtower is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ForbiddingWatchtower_HasManaAndAnimateAbilities()
    {
        var land = ForbiddingWatchtowerFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {W} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}{W} animate ability is wired");
    }

    [Fact]
    public void ForbiddingWatchtower_TapForWhite_TapsLandAndProducesOneWhite()
    {
        var land = ForbiddingWatchtowerFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();
        manaAbility.CanActivate().Should().BeTrue();

        var produced = manaAbility.Activate();
        produced.White.Should().Be(1);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ForbiddingWatchtower()
    {
        var card = NamedCardFactory.Create("Forbidding Watchtower", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Forbidding Watchtower");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void ForbiddingWatchtower_AnimateAbility_HasPrintedManaCost1W()
    {
        var land = ForbiddingWatchtowerFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        var mana = animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({1}{W})").Subject;
        mana.Cost.Generic.Should().Be(1, "{1} generic pip");
        mana.Cost.White.Should().Be(1, "{W} white pip");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void ForbiddingWatchtower_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = ForbiddingWatchtowerFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Soldier,
            "Soldier subtype added");
        chars.Keywords.Should().NotContain("Flying",
            "Forbidding Watchtower's animated body has no flying");
    }

    [Fact]
    public void ForbiddingWatchtower_AnimateEffect_AppliesTypeAndSubtype_NoFlying()
    {
        var land = ForbiddingWatchtowerFactory.Create(_alice);
        var effect = new ForbiddingWatchtowerAnimateEffect(land);

        var chars = new PermanentCharacteristics();
        chars.Types.Add(CardType.Land); // printed
        effect.Apply(chars);

        chars.Types.Should().Contain(CardType.Creature, "creature type added");
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays — \"It's still a land\"");
        chars.Subtypes.Should().Contain(CardSubtype.Soldier,
            "Soldier subtype added");
        chars.Keywords.Should().NotContain("Flying",
            "the 1/5 Soldier body has no evasion");
        effect.ExpiresAtEndOfTurn.Should().BeTrue("animation lifts at cleanup (CR 514.2)");
    }

    [Fact]
    public void ForbiddingWatchtower_BecomesPTEffect_SetsBase1_5()
    {
        var land = ForbiddingWatchtowerFactory.Create(_alice);
        var effect = new ForbiddingWatchtowerBecomesPTEffect(land, 1, 5);

        effect.NewPower.Should().Be(1);
        effect.NewToughness.Should().Be(5);
        effect.Layer.Should().Be(Layer.PT_SetBase);
        effect.ExpiresAtEndOfTurn.Should().BeTrue();

        var chars = new CreatureCharacteristics();
        effect.Apply(chars);
        chars.Power.Should().Be(1);
        chars.Toughness.Should().Be(5);
    }

    [Fact]
    public void ForbiddingWatchtower_Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = ForbiddingWatchtowerFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Soldier);
    }
}
