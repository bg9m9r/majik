using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 700.2e — modal "Choose two —" spell. Kozilek's Command
/// (Modern Horizons 3, {X}{C}{C}, Kindred Instant — Eldrazi).
///
/// Oracle text:
///   "Choose two —
///     • Target player creates X 0/1 colorless Eldrazi Spawn creature tokens
///       with "Sacrifice this token: Add {C}."
///     • Target player scries X, then draws a card.
///     • Exile target creature with mana value X or less.
///     • Exile up to X target cards from graveyards."
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/> — same pattern as <see cref="Majik.Core.Tests.CardData.KolaghansCommandTests"/>.
/// </summary>
public class KozileksCommandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    private static IReadOnlyList<object>[] EmptyTargets() => new IReadOnlyList<object>[]
    {
        System.Array.Empty<object>(),
        System.Array.Empty<object>(),
        System.Array.Empty<object>(),
        System.Array.Empty<object>(),
    };

    private static ChosenSpellParams Chosen(
        int x,
        IReadOnlyList<object>[] targets,
        IReadOnlyList<Player> players,
        params int[] modes) =>
        new ChosenSpellParams(
            ModeIndex: modes.Length > 0 ? modes[0] : null,
            X: x,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: players,
            ModeIndexes: modes);

    // -----------------------------------------------------------------------
    // Mode 0 — Target player creates X Eldrazi Spawn tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_CreateEldraziSpawn_TargetPlayerGetsXTokens()
    {
        var def = KozileksCommandFactory.BuildDefinition(_alice, o => o, allPlayers: new[] { _alice, _bob });

        var targets = EmptyTargets();
        targets[KozileksCommandFactory.ModeCreateSpawn] = new object[] { _alice };

        var chosen = Chosen(
            x: 3,
            targets,
            new[] { _alice, _bob },
            KozileksCommandFactory.ModeCreateSpawn,
            KozileksCommandFactory.ModeScryDraw);

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        effects[0].Execute();

        var spawn = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Eldrazi Spawn").ToList();
        spawn.Should().HaveCount(3, because: "mode 0 creates X (=3) Eldrazi Spawn tokens for the target player");
        spawn[0].Power.Should().Be(0);
        spawn[0].Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Target player scries X, then draws a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_ScryThenDraw_TargetPlayerDrawsACard()
    {
        // Stage: Alice's library has cards so the draw succeeds.
        for (var i = 0; i < 5; i++)
        {
            var c = new Instant($"Filler {i}", "{R}");
            c.SetOwner(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var def = KozileksCommandFactory.BuildDefinition(_alice, o => o, allPlayers: new[] { _alice, _bob });

        var targets = EmptyTargets();
        targets[KozileksCommandFactory.ModeScryDraw] = new object[] { _alice };

        var chosen = Chosen(
            x: 2,
            targets,
            new[] { _alice, _bob },
            KozileksCommandFactory.ModeScryDraw,
            KozileksCommandFactory.ModeExileCreature);

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        effects[0].Execute(); // scry-draw is the first chosen mode

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            because: "mode 1 draws exactly one card after the scry");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Exile target creature with mana value X or less
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_ExileCreature_WithinManaValue_LeavesBattlefield()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2); // mv 2
        bear.SetOwner(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = KozileksCommandFactory.BuildDefinition(_alice, o => o, allPlayers: new[] { _alice, _bob });

        var targets = EmptyTargets();
        targets[KozileksCommandFactory.ModeExileCreature] = new object[] { bear };

        var chosen = Chosen(
            x: 3, // mv 2 <= 3 -> legal
            targets,
            new[] { _alice, _bob },
            KozileksCommandFactory.ModeScryDraw,
            KozileksCommandFactory.ModeExileCreature);

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        effects[1].Execute(); // exile-creature is the second chosen mode

        bear.Zone.Should().Be(ZoneType.Exile,
            because: "mode 2 exiles a creature whose mana value (2) is X (3) or less");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Mode2_ExileCreature_AboveManaValue_DoesNothing()
    {
        var giant = new Creature("Colossus", "{8}", 8, 8); // mv 8
        giant.SetOwner(_bob);
        giant.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(giant);

        var def = KozileksCommandFactory.BuildDefinition(_alice, o => o, allPlayers: new[] { _alice, _bob });

        var targets = EmptyTargets();
        targets[KozileksCommandFactory.ModeExileCreature] = new object[] { giant };

        var chosen = Chosen(
            x: 2, // mv 8 > 2 -> illegal at resolution (CR 608.2b / 202.3)
            targets,
            new[] { _alice, _bob },
            KozileksCommandFactory.ModeScryDraw,
            KozileksCommandFactory.ModeExileCreature);

        var effects = def.EffectFactory(chosen);
        effects[1].Execute();

        giant.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 2 cannot exile a creature whose mana value exceeds X");
    }

    // -----------------------------------------------------------------------
    // Mode 3 — Exile up to X target cards from graveyards
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode3_ExileFromGraveyards_ExilesUpToXCards()
    {
        var c1 = new Instant("Bolt", "{R}");
        c1.SetOwner(_bob); c1.SetZone(ZoneType.Graveyard); _bob.Zones.Graveyard.AddCard(c1);
        var c2 = new Creature("Bear", "{1}{G}", 2, 2);
        c2.SetOwner(_bob); c2.SetZone(ZoneType.Graveyard); _bob.Zones.Graveyard.AddCard(c2);
        var c3 = new Instant("Opt", "{U}");
        c3.SetOwner(_alice); c3.SetZone(ZoneType.Graveyard); _alice.Zones.Graveyard.AddCard(c3);

        var def = KozileksCommandFactory.BuildDefinition(_alice, o => o, allPlayers: new[] { _alice, _bob });

        var targets = EmptyTargets();
        // X = 2, three cards targeted; "up to X" means only the first 2 are exiled.
        targets[KozileksCommandFactory.ModeExileGraveyard] = new object[] { c1, c2, c3 };

        var chosen = Chosen(
            x: 2,
            targets,
            new[] { _alice, _bob },
            KozileksCommandFactory.ModeCreateSpawn,
            KozileksCommandFactory.ModeExileGraveyard);

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        effects[1].Execute(); // exile-graveyard is the second chosen mode

        var exiledCount = new ICard[] { c1, c2, c3 }.Count(c => c.Zone == ZoneType.Exile);
        exiledCount.Should().Be(2, because: "mode 3 exiles up to X (=2) targeted cards from graveyards");
    }

    // -----------------------------------------------------------------------
    // Shape / dispatch tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasKindredInstantEldraziShape_Colorless()
    {
        var kc = KozileksCommandFactory.Create(_alice);

        kc.Name.Should().Be("Kozilek's Command");
        kc.HasType(CardType.Instant).Should().BeTrue();
        kc.HasType(CardType.Tribal).Should().BeTrue(); // Kindred (CR 308)
        kc.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        // {X}{C}{C} is colorless (CR 105.2c).
        CardColors.GetColors(kc).Should().BeEmpty();
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsKozileksCommandShape()
    {
        var dispatched = NamedCardFactory.Create("Kozilek's Command", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Kozilek's Command");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesFourModes_VariableX_FourTargetRequests()
    {
        var def = KozileksCommandFactory.BuildDefinition(_alice, o => o, allPlayers: null);

        def.Modes.Should().HaveCount(4);
        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(4);
        def.TargetRequests[KozileksCommandFactory.ModeCreateSpawn].MinTargets.Should().Be(0);
        def.TargetRequests[KozileksCommandFactory.ModeScryDraw].MinTargets.Should().Be(0);
        def.TargetRequests[KozileksCommandFactory.ModeExileCreature].MinTargets.Should().Be(0);
        def.TargetRequests[KozileksCommandFactory.ModeExileGraveyard].MinTargets.Should().Be(0);
    }
}
