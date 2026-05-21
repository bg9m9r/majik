using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleSpellBinderTokenModifierTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CreateTokensWithFlying_GrantsFlyingKeyword()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Spectral Procession", ManaCost = "{2W}{2W}{2W}",
              OracleText = "Create three 1/1 white Spirit creature tokens with flying." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(3);
        tokens.All(t => CombatAbilities.HasFlying(t)).Should().BeTrue();
    }

    [Fact]
    public void CreateTokenWithHaste_GrantsHasteKeyword()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "{1}{R}",
              OracleText = "Create a 2/2 red Elemental creature token with haste." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().First(c => c.IsToken);
        CombatAbilities.HasHaste(token).Should().BeTrue();
    }

    [Fact]
    public void Bind_CreateTreasureTokens_SpawnsTreasureArtifacts()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Treasure Maker", ManaCost = "{1}{R}",
              OracleText = "Create 3 Treasure tokens." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.Subtypes.Contains(CardSubtype.Treasure))
            .Should().HaveCount(3);
    }

    [Fact]
    public void Bind_CreateFoodToken_Singular_SpawnsOne()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Foodmonger", ManaCost = "{1}{G}",
              OracleText = "Create a Food token." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.Subtypes.Contains(CardSubtype.Food))
            .Should().HaveCount(1);
    }

    [Fact]
    public void Bind_CreateClueTokens_Plural_SpawnsTwo()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Investigator", ManaCost = "{2}{U}",
              OracleText = "Create 2 Clue tokens." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.Subtypes.Contains(CardSubtype.Clue))
            .Should().HaveCount(2);
    }

    // CR 701.30 — Investigate keyword action creates one Clue token.
    [Fact]
    public void Bind_Investigate_SpawnsOneClueToken()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Sleuth", ManaCost = "{2}{U}",
              OracleText = "Investigate." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.Subtypes.Contains(CardSubtype.Clue))
            .Should().HaveCount(1);
    }

    // CR 701.30 — "Investigate N times" creates N Clue tokens.
    [Fact]
    public void Bind_InvestigateThreeTimes_SpawnsThreeClueTokens()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Triple Sleuth", ManaCost = "{3}{U}",
              OracleText = "Investigate three times." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.Subtypes.Contains(CardSubtype.Clue))
            .Should().HaveCount(3);
    }
}
