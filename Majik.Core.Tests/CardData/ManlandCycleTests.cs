using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Parametric tests for the Worldwake / Battle for Zendikar / Oath of the
/// Gatewatch "manland" cycle: Celestial Colonnade, Stirring Wildwood,
/// Lumbering Falls, Shambling Vent, Needle Spires, Hissing Quagmire.
///
/// Each card shares the same shape:
///   - Land (no printed subtypes / supertype).
///   - ETB-tapped (CR 614.1c).
///   - Two mana abilities — one per allied colour.
///   - One activated ability with a 3-pip mana cost; resolution registers
///     <see cref="ManlandCycleAnimateEffect"/> (Layer 4) + a
///     <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b), both flagged
///     ExpiresAtEndOfTurn.
///   - After Compute(): Creature + Elemental added, printed Land kept,
///     keyword set populated.
///   - After ExpireEndOfTurn(): both effects gone, Compute() back to land.
/// </summary>
public class ManlandCycleTests
{
    /// <summary>Per-card spec: name + the two mana colours + animated body
    /// P/T + keyword grants. Wraps the parametric table so xUnit theory
    /// methods take a single bag and don't get nagged by xUnit1026.</summary>
    public sealed record ManlandSpec(
        string Name,
        string Color1,
        string Color2,
        int Power,
        int Toughness,
        string[] Keywords);

    public static readonly ManlandSpec CelestialColonnade =
        new("Celestial Colonnade", "W", "U", 4, 4, new[] { "Flying", "Vigilance" });
    public static readonly ManlandSpec StirringWildwood =
        new("Stirring Wildwood", "G", "W", 3, 4, new[] { "Reach" });
    public static readonly ManlandSpec LumberingFalls =
        new("Lumbering Falls", "G", "U", 3, 3, new[] { "Hexproof" });
    public static readonly ManlandSpec ShamblingVent =
        new("Shambling Vent", "W", "B", 2, 3, new[] { "Lifelink" });
    public static readonly ManlandSpec NeedleSpires =
        new("Needle Spires", "R", "W", 2, 1, new[] { "Double Strike" });
    public static readonly ManlandSpec HissingQuagmire =
        new("Hissing Quagmire", "B", "G", 2, 2, new[] { "Deathtouch" });

    public static IEnumerable<object[]> Cards() => new[]
    {
        new object[] { CelestialColonnade },
        new object[] { StirringWildwood   },
        new object[] { LumberingFalls     },
        new object[] { ShamblingVent      },
        new object[] { NeedleSpires       },
        new object[] { HissingQuagmire    },
    };

    private readonly Player _alice = new("Alice", 20);

    private Land Create(string name, ContinuousEffectsService? effects, ReplacementBus? replacements)
        => name switch
        {
            "Celestial Colonnade" => CelestialColonnadeFactory.Create(_alice, effects, replacements),
            "Stirring Wildwood"   => StirringWildwoodFactory  .Create(_alice, effects, replacements),
            "Lumbering Falls"     => LumberingFallsFactory    .Create(_alice, effects, replacements),
            "Shambling Vent"      => ShamblingVentFactory     .Create(_alice, effects, replacements),
            "Needle Spires"       => NeedleSpiresFactory      .Create(_alice, effects, replacements),
            "Hissing Quagmire"    => HissingQuagmireFactory   .Create(_alice, effects, replacements),
            _ => throw new ArgumentException($"Unknown manland {name}"),
        };

    private Land CreateShape(string name)
        => name switch
        {
            "Celestial Colonnade" => CelestialColonnadeFactory.Create(_alice),
            "Stirring Wildwood"   => StirringWildwoodFactory  .Create(_alice),
            "Lumbering Falls"     => LumberingFallsFactory    .Create(_alice),
            "Shambling Vent"      => ShamblingVentFactory     .Create(_alice),
            "Needle Spires"       => NeedleSpiresFactory      .Create(_alice),
            "Hissing Quagmire"    => HissingQuagmireFactory   .Create(_alice),
            _ => throw new ArgumentException($"Unknown manland {name}"),
        };

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void Manland_IsLand_NoSubtypes_NoSupertypes(ManlandSpec spec)
    {
        var land = CreateShape(spec.Name);

        land.Name.Should().Be(spec.Name);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed manland is just a Land until activated");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void NamedCardFactory_Dispatches_Manland(ManlandSpec spec)
    {
        var card = NamedCardFactory.Create(spec.Name, _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be(spec.Name);
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2, "{T}: Add c1 / {T}: Add c2");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "the animate ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add c1 / {T}: Add c2 — mana abilities produce the right colour
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void Manland_TapForFirstColor_ProducesThatColor(ManlandSpec spec)
    {
        var land = CreateShape(spec.Name);
        var ability = land.Abilities.OfType<ManaAbility>().First();

        ability.CanActivate().Should().BeTrue();
        var produced = ability.Activate();

        AssertSingleColorProduced(produced, spec.Color1);
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void Manland_TapForSecondColor_ProducesThatColor(ManlandSpec spec)
    {
        // Fresh land so we can tap for the second colour.
        var land = CreateShape(spec.Name);
        var second = land.Abilities.OfType<ManaAbility>().Skip(1).First();

        second.CanActivate().Should().BeTrue();
        var produced = second.Activate();

        AssertSingleColorProduced(produced, spec.Color2);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Animate — Layer 4 + Layer 7b grant
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void Animate_RegistersLayer4AndLayer7b_EotExpiring_OnTheLand(ManlandSpec spec)
    {
        var effects = new ContinuousEffectsService();
        var land = Create(spec.Name, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.Keywords.Should().BeEquivalentTo(spec.Keywords);

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == spec.Power && e.NewToughness == spec.Toughness);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        // Compute(land) reflects the Layer 4 grants: printed Land stays,
        // Creature + Elemental added, keyword set populated.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "\"It's still a land.\"");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
        foreach (var k in spec.Keywords)
        {
            chars.Keywords.Should().Contain(k);
        }
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void Animate_EndOfTurnExpiration_RevertsLand(ManlandSpec spec)
    {
        var effects = new ContinuousEffectsService();
        var land = Create(spec.Name, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        GetRegisteredEffects(effects).OfType<ManlandCycleAnimateEffect>().Should().NotBeEmpty();
        GetRegisteredEffects(effects).OfType<ManlandCycleBecomesPTEffect>().Should().NotBeEmpty();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .Where(e => ReferenceEquals(e.Target, land))
            .Should().BeEmpty();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Elemental);
        foreach (var k in spec.Keywords)
        {
            chars.Keywords.Should().NotContain(k);
        }
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void Animate_NoEffectsService_NoOp_ShapeRemainsLand(ManlandSpec spec)
    {
        var land = CreateShape(spec.Name);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        var resolve = () => animate.Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB-tapped
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void EntersTappedReplacement_IsRegistered_WhenReplacementBusSupplied(ManlandSpec spec)
    {
        var replacements = new ReplacementBus();
        var act = () => Create(spec.Name, effects: null, replacements: replacements);
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void AssertSingleColorProduced(ManaCost produced, string color)
    {
        switch (color)
        {
            case "W": produced.White.Should().Be(1); break;
            case "U": produced.Blue.Should().Be(1);  break;
            case "B": produced.Black.Should().Be(1); break;
            case "R": produced.Red.Should().Be(1);   break;
            case "G": produced.Green.Should().Be(1); break;
            default: throw new ArgumentException($"Unknown color {color}");
        }
        produced.Generic.Should().Be(0);
        var total = produced.White + produced.Blue + produced.Black + produced.Red + produced.Green;
        total.Should().Be(1);
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
