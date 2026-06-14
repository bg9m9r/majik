using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Prime Speaker Vannifar (RNA, {2}{G}{U}, Legendary Creature —
/// Elf Ooze Wizard 2/4).
///
/// Oracle text (Scryfall, verified):
///   "{T}, Sacrifice another creature: Search your library for a creature card
///    with mana value equal to 1 plus the sacrificed creature's mana value,
///    put that card onto the battlefield, then shuffle. Activate only as a
///    sorcery."
///
/// Coverage (UNIQUE behaviour — the contract test already covers dispatch /
/// well-formedness):
///  - Identity: name / type / supertype / subtypes / mana cost / P-T.
///  - Activated-ability shape: {T} (tap) + sacrifice-another-creature cost,
///    sorcery-speed rider (CR 117.1a / 307.5).
///  - Resolution: EXACT mana value — X = 1 + sac.MV, a matching-MV creature
///    is tutored onto the battlefield (not "≤"); a wrong-MV creature is
///    excluded; the no-find branch leaves the battlefield untouched.
/// </summary>
[Trait("Color", "M")] // {2}{G}{U} — multicolour.
public class PrimeSpeakerVannifarTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ResolutionContext Rc(Player controller, IPlayerAgent? agent) =>
        ResolutionContext.For(controller, agent: agent, game: null, chosenTargets: null);

    // ----------------------------------------------------------------------
    // Identity
    // ----------------------------------------------------------------------

    [Fact]
    public void Vannifar_Identity()
    {
        var c = PrimeSpeakerVannifarFactory.Create(_alice);

        c.Name.Should().Be("Prime Speaker Vannifar");
        c.ManaCost.Should().Be("{2}{G}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Should().BeOfType<Creature>();

        var creature = (Creature)c;
        creature.Power.Should().Be(2);
        creature.Toughness.Should().Be(4);
        creature.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Legendary supertype enforces the legend rule (CR 704.5j)");
        creature.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        creature.HasSubtype(CardSubtype.Ooze).Should().BeTrue();
        creature.HasSubtype(CardSubtype.Wizard).Should().BeTrue();

        // {2}{G}{U} → mv 4.
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    // ----------------------------------------------------------------------
    // Activated-ability shape
    // ----------------------------------------------------------------------

    [Fact]
    public void Ability_HasTapAndSacrificeCosts_AndIsSorcerySpeed()
    {
        var c = PrimeSpeakerVannifarFactory.Create(_alice);

        var ability = c.Abilities.OfType<PrimeSpeakerVannifarAbility>().Single();

        // CR 117.1a / 307.5 — "Activate only as a sorcery".
        ability.IsSorcerySpeed.Should().BeTrue();

        // {T} + sacrifice-another-creature.
        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "the {T} tap cost is present");
        ability.Costs.OfType<SacrificeAnotherCreatureCost>().Should().ContainSingle(
            "the 'sacrifice another creature' cost is present");
    }

    // ----------------------------------------------------------------------
    // Resolution — EXACT mana value tutor
    // ----------------------------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task Resolve_TutorsCreatureWithExactlyOneMoreManaValue_OntoBattlefield()
    {
        var van = PrimeSpeakerVannifarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(van);
        van.SetZone(ZoneType.Battlefield);

        // Sacrifice an MV-1 creature → target MV = 1 + 1 = 2.
        var fodder = SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        var twoDrop  = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);   // mv 2 — EXACT
        var oneDrop  = SeedCreatureInLibrary(_alice, "Elvish Mystic", "{G}", 1, 1);      // mv 1 — too small
        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3 — too big

        var ability = van.Abilities.OfType<PrimeSpeakerVannifarAbility>().Single();
        var sacCost = ability.SacrificeChoice;
        sacCost.Target = fodder;
        sacCost.Pay(_alice); // CR 601.2f — cost payment captures the sacrificed creature.

        var agent = new VannifarTestAgent(libraryPick: twoDrop);
        await PrimeSpeakerVannifarFactory.TutorExactManaValueAsync(
            _alice, sacCost, Rc(_alice, agent));

        // The exact-MV creature lands on the battlefield under Alice's control.
        _alice.Zones.Battlefield.GetCards().Should().Contain(twoDrop);
        twoDrop.Zone.Should().Be(ZoneType.Battlefield);
        twoDrop.Controller.Should().BeSameAs(_alice);

        // The too-small / too-big creatures stay in the library.
        _alice.Zones.Library.GetCards().Should().Contain(new[] { oneDrop, threeDrop });
        _alice.Zones.Library.GetCards().Should().NotContain(twoDrop);

        // Fodder went to the graveyard via the sacrifice cost.
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_RejectsWrongManaValuePick_NothingEntersBattlefield()
    {
        var van = PrimeSpeakerVannifarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(van);
        van.SetZone(ZoneType.Battlefield);

        // Sac MV 1 → target MV = 2. Library has only an MV-3 creature, so the
        // agent's (illegal) pick of it must be rejected by the factory.
        var fodder = SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);
        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3

        var ability = van.Abilities.OfType<PrimeSpeakerVannifarAbility>().Single();
        var sacCost = ability.SacrificeChoice;
        sacCost.Target = fodder;
        sacCost.Pay(_alice);

        var agent = new VannifarTestAgent(libraryPick: threeDrop);
        await PrimeSpeakerVannifarFactory.TutorExactManaValueAsync(
            _alice, sacCost, Rc(_alice, agent));

        // Nothing tutored — the wrong-MV creature stays in the library.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(threeDrop);
        _alice.Zones.Library.GetCards().Should().Contain(threeDrop);
        // The sacrifice still happened (it was a cost, paid before resolution).
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_BiggerSacEnablesBiggerExactPick()
    {
        var van = PrimeSpeakerVannifarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(van);
        van.SetZone(ZoneType.Battlefield);

        // Sac MV 2 → target MV = 3. An MV-3 creature is now the exact match.
        var fodder = SeedCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3 — EXACT
        var twoDrop = SeedCreatureInLibrary(_alice, "Centaur Courser", "{2}{G}", 3, 3);     // mv 3 too? no -> mv 3

        var ability = van.Abilities.OfType<PrimeSpeakerVannifarAbility>().Single();
        var sacCost = ability.SacrificeChoice;
        sacCost.Target = fodder;
        sacCost.Pay(_alice);

        var agent = new VannifarTestAgent(libraryPick: threeDrop);
        await PrimeSpeakerVannifarFactory.TutorExactManaValueAsync(
            _alice, sacCost, Rc(_alice, agent));

        _alice.Zones.Battlefield.GetCards().Should().Contain(threeDrop);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Creature SeedCreatureOnBattlefield(
        Player p, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static ICard SeedCreatureInLibrary(
        Player p, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    /// <summary>
    /// Test agent that supplies a pre-canned library pick for Vannifar's tutor
    /// prompt; all other decision hooks throw to flag unexpected use.
    /// </summary>
    private sealed class VannifarTestAgent : IPlayerAgent
    {
        private readonly ICard? _libraryPick;

        public VannifarTestAgent(ICard? libraryPick) => _libraryPick = libraryPick;

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(
                _libraryPick != null && candidates.Contains(_libraryPick)
                    ? _libraryPick
                    : null);

        // ---- unused decision hooks (throw to flag unexpected use) -----
        public Task<ICard?> ChooseFromBattlefieldAsync(Player chooser, IReadOnlyList<ICard> candidates, BotIntent intent, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int m, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int n, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest req, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard src, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> a, IReadOnlyList<Permanent> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
