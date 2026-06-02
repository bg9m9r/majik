using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the COMBINED split card factory <see cref="CrimePunishmentFactory"/>
/// (Crime // Punishment, Guildpact, {3}{W}{B} // {X}{B}{G}). Both faces are
/// Sorceries.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   Crime {3}{W}{B} — Sorcery: "Put target creature or enchantment card from
///     an opponent's graveyard onto the battlefield under your control."
///   Punishment {X}{B}{G} — Sorcery: "Destroy each artifact, creature, and
///     enchantment with mana value X."
///
/// Split cards present each half as its own castable face (CR 712.2). This
/// combined factory mirrors the two-face posture of <see cref="WearTearFactory"/>:
/// the combined card name "Crime // Punishment" is the <c>[CardName]</c>
/// dispatch key (matching the embedded seed row), the card SHAPE is built from
/// the embedded JSON definition (<c>crime-punishment.json</c>), and each face's
/// resolve-time behaviour is built on demand here. Real split-cast face choice
/// (CR 712.3) is a shared deferral with Wear // Tear / Boom // Bust; the
/// combined object carries the front (Crime) {3}{W}{B} cost.
///
/// Covers:
///   - Combined card identity (Sorcery, combined name, white+black, Crime cost).
///   - <see cref="NamedCardFactory"/> dispatch for the combined name.
///   - Crime face — reanimate a creature / enchantment card from an OPPONENT's
///     graveyard onto the caster's battlefield under the caster's control;
///     caster's own graveyard and non-creature/enchantment cards excluded.
///   - Punishment face — destroy each artifact/creature/enchantment with mana
///     value EXACTLY X; mv != X and lands untouched.
/// </summary>
[Trait("Color", "WB")]
public class CrimePunishmentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ────────────────────────────────────────────────

    [Fact]
    public void CrimePunishment_IsSorcery_WithCrimeFrontFaceCost()
    {
        var card = CrimePunishmentFactory.Create(_alice);

        card.Name.Should().Be("Crime // Punishment");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        // The combined card carries the front (Crime) face mana cost.
        card.ManaCost.ToString().Should().Be("{3}{W}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CrimePunishment_IsWhiteAndBlack()
    {
        var card = CrimePunishmentFactory.Create(_alice);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.White);
        colors.Should().Contain(ManaColorEnum.Black);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CrimePunishment()
    {
        var card = NamedCardFactory.Create("Crime // Punishment", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Crime // Punishment");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{3}{W}{B}");
    }

    // ── Crime face — reanimate from an opponent's graveyard ─────────────────

    [Fact]
    public void CrimeFace_OffersOpponentCreatureAndEnchantmentCards_NotCasterOwn()
    {
        // Bob (opponent) graveyard: a creature + an enchantment → both offered.
        var bobCreature = SeedGraveyard(_bob, new Creature("Bob Bear", "{1}{G}", 2, 2));
        var bobAura = SeedGraveyard(_bob, new Enchantment("Bob Aura", "{1}{W}"));
        // Bob also has an instant in graveyard → NOT offered (wrong type).
        SeedGraveyard(_bob, new Instant("Bob Bolt", "{R}"));
        // Alice's OWN graveyard creature → NOT offered ("an opponent's").
        SeedGraveyard(_alice, new Creature("Alice Bear", "{1}{G}", 2, 2));

        var def = CrimePunishmentFactory.BuildCrimeDefinition(
            caster: _alice,
            allPlayers: new[] { _alice, _bob },
            targetResolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].LegalCandidates.Should()
            .BeEquivalentTo(new object[] { bobCreature, bobAura });
    }

    [Fact]
    public void CrimeFace_ReanimatesOpponentCard_UnderCasterControl()
    {
        var bobCreature = SeedGraveyard(_bob, new Creature("Bob Bear", "{1}{G}", 2, 2));

        var def = CrimePunishmentFactory.BuildCrimeDefinition(
            caster: _alice,
            allPlayers: new[] { _alice, _bob },
            targetResolver: x => x);

        Resolve(def, bobCreature);

        bobCreature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Crime puts the target onto the battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bobCreature);
        bobCreature.Controller.Should().BeSameAs(_alice,
            because: "it enters under the caster's control");
        bobCreature.Owner.Should().BeSameAs(_bob,
            because: "control change does not change ownership (CR 108.3)");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobCreature);
    }

    [Fact]
    public void CrimeFace_NoOpponentTargets_ProducesEmptyCandidateList()
    {
        // Only the caster's own graveyard has a creature → no legal target.
        SeedGraveyard(_alice, new Creature("Alice Bear", "{1}{G}", 2, 2));

        var def = CrimePunishmentFactory.BuildCrimeDefinition(
            caster: _alice,
            allPlayers: new[] { _alice, _bob },
            targetResolver: x => x);

        def.TargetRequests[0].LegalCandidates.Should().BeEmpty();
    }

    // ── Punishment face — destroy each a/c/e with mana value X ──────────────

    [Fact]
    public void PunishmentFace_DestroysArtifactCreatureEnchantment_WithManaValueExactlyX()
    {
        // mv-3 of each destroyable type — all destroyed at X = 3.
        var art = SeedBattlefield(_alice, new Artifact("3-Artifact", "{3}"));
        var crea = SeedBattlefield(_alice, new Creature("3-Creature", "{2}{G}", 3, 3));
        var ench = SeedBattlefield(_bob, new Enchantment("3-Enchantment", "{1}{W}{W}"));

        var effects = CrimePunishmentFactory.BuildPunishmentResolveEffect(
            _alice, new[] { _alice, _bob }, x: 3);
        foreach (var e in effects) e.Execute();

        art.Zone.Should().Be(ZoneType.Graveyard, "mv-3 artifact destroyed at X=3");
        crea.Zone.Should().Be(ZoneType.Graveyard, "mv-3 creature destroyed at X=3");
        ench.Zone.Should().Be(ZoneType.Graveyard, "mv-3 enchantment destroyed at X=3");
    }

    [Fact]
    public void PunishmentFace_LeavesPermanentsWithDifferentManaValueAlone()
    {
        // Punishment is "mana value X" (exact), NOT "X or less" — so mv-2 and
        // mv-4 permanents survive at X = 3.
        var mv2 = SeedBattlefield(_alice, new Creature("2-Creature", "{1}{G}", 2, 2));
        var mv3 = SeedBattlefield(_alice, new Creature("3-Creature", "{2}{G}", 3, 3));
        var mv4 = SeedBattlefield(_alice, new Artifact("4-Artifact", "{4}"));

        var effects = CrimePunishmentFactory.BuildPunishmentResolveEffect(
            _alice, new[] { _alice, _bob }, x: 3);
        foreach (var e in effects) e.Execute();

        mv2.Zone.Should().Be(ZoneType.Battlefield, "mv-2 != 3, survives");
        mv3.Zone.Should().Be(ZoneType.Graveyard, "mv-3 == 3, destroyed");
        mv4.Zone.Should().Be(ZoneType.Battlefield, "mv-4 != 3, survives");
    }

    [Fact]
    public void PunishmentFace_DoesNotTouchLands()
    {
        // Lands are mv-0; at X = 0 they must still survive (not in the
        // artifact/creature/enchantment type set).
        var land = SeedBattlefield(_alice, new Land("Forest"));
        var mv0Art = SeedBattlefield(_alice, new Artifact("0-Artifact", "{0}"));

        var effects = CrimePunishmentFactory.BuildPunishmentResolveEffect(
            _alice, new[] { _alice, _bob }, x: 0);
        foreach (var e in effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Battlefield, "land is not artifact/creature/enchantment");
        mv0Art.Zone.Should().Be(ZoneType.Graveyard, "sanity — mv-0 artifact destroyed at X=0");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static T SeedGraveyard<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        return card;
    }

    private static T SeedBattlefield<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private static void Resolve(SpellDefinition def, ICard target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
