using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for OracleSpellBinder's "destroy up to N target artifacts and/or
/// enchantments" template — exercised via Force of Vigor's oracle text.
/// </summary>
public class OracleSpellBinderForceOfVigorTests
{
    private static readonly string ForceOracle =
        "If it's not your turn, you may exile a green card from your hand " +
        "rather than pay this spell's mana cost. " +
        "Destroy up to two target artifacts and/or enchantments.";

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Bind smoke tests ─────────────────────────────────────────────────────

    [Fact]
    public void Bind_FullForceOracle_ReturnsDefinition()
    {
        var def = Bind("Force of Vigor", "{2}{G}{G}", ForceOracle);
        def.Should().NotBeNull();
    }

    [Fact]
    public void Bind_BareDestroyUpToTwo_ReturnsDefinition()
    {
        var def = Bind("X", "{G}", "Destroy up to two target artifacts and/or enchantments.");
        def.Should().NotBeNull();
    }

    [Fact]
    public void Bind_DestroyUpToTwo_TargetRequestAllowsZeroMinimum()
    {
        var def = Bind("X", "{G}", "Destroy up to two target artifacts and/or enchantments.");
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(2);
    }

    // ── Destroy effects ───────────────────────────────────────────────────────

    [Fact]
    public void DestroyUpToTwo_BothArtifactAndEnchantment_BothDestroyed()
    {
        var orb = SetupOnBattlefield(new Artifact("Sphere", "{2}"), _bob);
        var glyph = SetupOnBattlefield(new Enchantment("Pacifism", "{1}{W}"), _bob);

        var def = Bind("Force of Vigor", "{2}{G}{G}", ForceOracle)!;
        ResolveMulti(def, orb, glyph);

        orb.Zone.Should().Be(ZoneType.Graveyard);
        glyph.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DestroyUpToTwo_SingleArtifact_Destroyed()
    {
        var orb = SetupOnBattlefield(new Artifact("Sphere", "{2}"), _bob);

        var def = Bind("Force of Vigor", "{2}{G}{G}", ForceOracle)!;
        ResolveMulti(def, orb);

        orb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DestroyUpToTwo_SingleEnchantment_Destroyed()
    {
        var ench = SetupOnBattlefield(new Enchantment("Pacifism", "{1}{W}"), _bob);

        var def = Bind("Force of Vigor", "{2}{G}{G}", ForceOracle)!;
        ResolveMulti(def, ench);

        ench.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DestroyUpToTwo_ZeroTargets_IsNoOp()
    {
        // Spell with no targets chosen must not throw (CR 601.2c — "up to N"
        // allows zero legal selections).
        var def = Bind("Force of Vigor", "{2}{G}{G}", ForceOracle)!;
        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { Array.Empty<object>() },
            ManaPayment.Empty);

        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void DestroyUpToTwo_CreatureTargeted_NotDestroyed()
    {
        // A creature is not a legal target — even if passed, the type-guard
        // inside the effect ignores it.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = Bind("Force of Vigor", "{2}{G}{G}", ForceOracle)!;
        ResolveMulti(def, bear);

        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SpellDefinition? Bind(string name, string cost, string oracle) =>
        OracleSpellBinder.Bind(
            new CardEntity { Name = name, ManaCost = cost, OracleText = oracle },
            _alice, raw => raw, stack: null);

    private static T SetupOnBattlefield<T>(T card, Player owner) where T : ICard
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private void ResolveMulti(SpellDefinition def, params ICard[] targets)
    {
        var targetList = (IReadOnlyList<object>)targets.Cast<object>().ToArray();
        var chosen = new ChosenSpellParams(
            null, null,
            new[] { targetList },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
