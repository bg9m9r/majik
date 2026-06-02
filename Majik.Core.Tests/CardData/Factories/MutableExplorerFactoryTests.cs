using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MutableExplorerFactory"/>.
///
/// Covers:
/// - Identity ({2}{G} Creature — Shapeshifter, 1/1, green).
/// - Mana value 3 (CR 202.3).
/// - Changeling keyword marker (CR 702.73) + every-creature-type stamping
///   (HasSubtype returns true for the engine's modelled creature subtypes).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB resolution creates a tapped Mutavault token on the battlefield.
/// - Token identity: Land, named "Mutavault", IsToken, mana ability +
///   {1} animate ability shape, tapped on entry.
/// </summary>
[Trait("Color", "G")]
public class MutableExplorerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MutableExplorer_Identity()
    {
        var c = MutableExplorerFactory.Create(_alice);

        c.Name.Should().Be("Mutable Explorer");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue(
            "Mutable Explorer's printed creature subtype is Shapeshifter");
        c.ManaCost.Should().Be("{2}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MutableExplorer_IsGreen()
    {
        var c = MutableExplorerFactory.Create(_alice);
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green,
            "Mutable Explorer has a {G} pip in its mana cost");
        colors.Should().HaveCount(1, "only one colour identity");
    }

    [Fact]
    public void MutableExplorer_ManaValue_IsThree()
    {
        var c = MutableExplorerFactory.Create(_alice);
        c.ManaCostValue.TotalValue.Should().Be(3,
            "CR 202.3 — {2}{G} has mana value 3");
    }

    // -----------------------------------------------------------------------
    // Changeling (CR 702.73)
    // -----------------------------------------------------------------------

    [Fact]
    public void MutableExplorer_HasChangelingKeyword()
    {
        var c = MutableExplorerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Changeling",
                "CR 702.73 — Mutable Explorer has Changeling");
    }

    [Fact]
    public void MutableExplorer_Changeling_IsEveryModelledCreatureType()
    {
        var c = MutableExplorerFactory.Create(_alice);

        // CR 702.73a — Changeling stamps every creature type. v1 stamps the
        // engine's modelled set (same list Mutavault's animate uses). Spot-
        // check a representative slice; then assert the entire list to keep
        // the test honest when the enum grows.
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();

        foreach (var st in MutavaultAnimateEffect.EveryCreatureType)
        {
            c.HasSubtype(st).Should().BeTrue(
                $"Changeling makes Mutable Explorer a {st}");
        }
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MutableExplorer_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = MutableExplorerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — Mutavault token creation
    // -----------------------------------------------------------------------

    [Fact]
    public void MutableExplorer_EtbTrigger_CreatesTappedMutavaultTokenOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        var explorer = MutableExplorerFactory.Create(alice, effects, zones: null, triggers: null);
        var etb = explorer.Abilities.OfType<TriggeredAbility>().Single();

        // Sanity — battlefield is empty pre-resolution.
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        foreach (var effect in etb.Effects) effect.Execute();

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .Where(l => l.Name == "Mutavault")
            .ToList();

        tokens.Should().HaveCount(1,
            "ETB creates exactly one Mutavault token");

        var token = tokens.Single();
        token.IsToken.Should().BeTrue(
            "CR 111 — the Mutavault is a token, marked IsToken so SBA 704.5d cleans it up off-battlefield");
        token.IsTapped.Should().BeTrue(
            "oracle: 'create a TAPPED Mutavault token'");
        token.HasType(CardType.Land).Should().BeTrue();
        token.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void MutableExplorer_MutavaultToken_HasManaAbilityAndActivatedAnimateAbility()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();
        var explorer = MutableExplorerFactory.Create(alice, effects, zones: null, triggers: null);
        var etb = explorer.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        var token = alice.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .Single(l => l.Name == "Mutavault");

        // The token has the same Mutavault ability shape: {T}: Add {C}
        // mana ability + {1}: animate activated ability.
        token.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Mutavault token has {T}: Add {C}");

        var activated = token.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();
        activated.Should().HaveCount(1,
            "Mutavault token has its {1}: become-a-2/2 ability alongside the mana ability");
        activated.Single().Effects.Should().HaveCount(1);
    }

    [Fact]
    public void MutableExplorer_MutavaultToken_AnimateAbility_RegistersLayer4And7b()
    {
        // The token's {1} activate must wire its animate effects against
        // the same continuous-effects service threaded into the parent
        // factory — verifies the service plumbs through the ETB closure.
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();
        var explorer = MutableExplorerFactory.Create(alice, effects, zones: null, triggers: null);
        var etb = explorer.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        var token = alice.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .Single(l => l.Name == "Mutavault");

        var activated = token.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Resolve();

        var registered = GetRegisteredEffects(effects).ToList();
        registered.OfType<MutavaultAnimateEffect>().Should().HaveCount(1,
            "Layer 4 animate effect registered on the token's continuous-effects service");
        registered.OfType<MutavaultBecomesPTEffect>().Should().HaveCount(1,
            "Layer 7b set-base P/T effect registered on the token's continuous-effects service");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
