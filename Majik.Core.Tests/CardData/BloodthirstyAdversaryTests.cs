using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Bloodthirsty Adversary (Innistrad: Midnight Hunt,
/// {1}{R}) — Creature — Vampire 2/2 with Haste.
///   "When this creature enters, you may pay {2}{R} any number of times.
///    When you pay this cost one or more times, put that many +1/+1 counters
///    on this creature, then exile up to that many target instant and/or
///    sorcery cards with mana value 3 or less from your graveyard and copy
///    them. You may cast any number of the copies without paying their mana
///    costs."
///
/// Coverage:
///   * Identity (Creature — Vampire, {1}{R}, 2/2) + NamedCardFactory dispatch.
///   * Haste keyword present.
///   * ETB paid N times ⇒ N +1/+1 counters.
///   * ETB paid 0 times ⇒ no counters, no exile, no copy (reflexive "one or
///     more times" never fires — CR 603.2).
///   * "up to that many" — exiles at most N targets; extra chosen targets
///     are ignored.
///   * mana value ≤ 3 filter + instant/sorcery filter + your-graveyard filter.
///   * Copies are cast free (effect list executes; mana payment empty).
/// </summary>
public class BloodthirstyAdversaryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    private Creature OnBattlefield()
    {
        var adversary = BloodthirstyAdversaryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(adversary);
        adversary.SetZone(ZoneType.Battlefield);
        return adversary;
    }

    private static SpellDefinition SentinelDef(Action onCast) =>
        new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("test-copy-sentinel", onCast),
            });

    private Instant GraveInstant(string name, string cost, Player owner)
    {
        var c = new Instant(name, cost) { Owner = owner };
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void BloodthirstyAdversary_IsVampireCreature_AtCost1R_2_2()
    {
        var a = BloodthirstyAdversaryFactory.Create(_alice);

        a.Name.Should().Be("Bloodthirsty Adversary");
        a.HasType(CardType.Creature).Should().BeTrue();
        a.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        a.ManaCost.Should().Be("{1}{R}");
        a.Power.Should().Be(2);
        a.Toughness.Should().Be(2);
        a.Owner.Should().BeSameAs(_alice);
        a.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BloodthirstyAdversary()
    {
        var card = NamedCardFactory.Create("Bloodthirsty Adversary", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bloodthirsty Adversary");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void HasHasteKeyword()
    {
        var a = BloodthirstyAdversaryFactory.Create(_alice);

        a.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "Bloodthirsty Adversary has Haste (CR 702.10)");
    }

    // ------------------------------------------------------------------
    // ETB — +1/+1 counters scale with the pay count
    // ------------------------------------------------------------------

    [Fact]
    public void Etb_PaidTwice_PlacesTwoPlusOnePlusOneCounters()
    {
        var a = OnBattlefield();

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 2, chosenTargets: Array.Empty<Card>())
            .Execute();

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "paying {2}{R} twice puts two +1/+1 counters on this creature");
    }

    [Fact]
    public void Etb_PaidZeroTimes_IsCleanNoOp()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var copies = 0;

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 0,
                chosenTargets: new[] { (Card)bolt },
                spellDefinitionLookup: _ => SentinelDef(() => copies++))
            .Execute();

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the reflexive 'when you pay one or more times' never fires at N==0 (CR 603.2)");
        bolt.Zone.Should().Be(ZoneType.Graveyard, "nothing is exiled when N==0");
        copies.Should().Be(0, "no copies cast when N==0");
    }

    // ------------------------------------------------------------------
    // Exile + copy + free cast
    // ------------------------------------------------------------------

    [Fact]
    public void Etb_PaidOnce_ExilesOneTarget_CopiesAndCastsFree()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var copies = 0;

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 1,
                chosenTargets: new[] { (Card)bolt },
                spellDefinitionLookup: _ => SentinelDef(() => copies++))
            .Execute();

        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bolt.Zone.Should().Be(ZoneType.Exile, "the targeted card is exiled before being copied");
        copies.Should().Be(1, "the copy is cast for free (effect list executes)");
    }

    [Fact]
    public void Etb_UpToThatMany_IgnoresTargetsBeyondPayCount()
    {
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var shock = GraveInstant("Shock", "{R}", _alice);
        var copies = 0;

        // Paid once, but two targets handed in — only the first is taken
        // ("exile UP TO that many").
        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 1,
                chosenTargets: new[] { (Card)bolt, shock },
                spellDefinitionLookup: _ => SentinelDef(() => copies++))
            .Execute();

        bolt.Zone.Should().Be(ZoneType.Exile);
        shock.Zone.Should().Be(ZoneType.Graveyard, "only N targets are taken (N == 1)");
        copies.Should().Be(1);
    }

    [Fact]
    public void Etb_ManaValueGreaterThanThree_IsNotLegalTarget()
    {
        var a = OnBattlefield();
        // Mana value 4 — illegal target.
        var bigSpell = new Sorcery("Cruel Ultimatum", "{U}{U}{B}{B}{B}{R}{R}") { Owner = _alice };
        bigSpell.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bigSpell);
        var copies = 0;

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 1,
                chosenTargets: new[] { (Card)bigSpell },
                spellDefinitionLookup: _ => SentinelDef(() => copies++))
            .Execute();

        bigSpell.Zone.Should().Be(ZoneType.Graveyard, "mana value > 3 is not a legal target");
        copies.Should().Be(0);
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the +1/+1 counters still land regardless of whether a legal target exists");
    }

    [Fact]
    public void Etb_CreatureTargetInGraveyard_IsNotLegal()
    {
        var a = OnBattlefield();
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bears.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bears);
        var copies = 0;

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 1,
                chosenTargets: new[] { (Card)bears },
                spellDefinitionLookup: _ => SentinelDef(() => copies++))
            .Execute();

        bears.Zone.Should().Be(ZoneType.Graveyard, "only instant/sorcery cards are legal targets");
        copies.Should().Be(0);
    }

    [Fact]
    public void Etb_OpponentsGraveyardCard_IsNotLegal()
    {
        var a = OnBattlefield();
        var bobBolt = GraveInstant("Lightning Bolt", "{R}", _bob);
        var copies = 0;

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 1,
                chosenTargets: new[] { (Card)bobBolt },
                spellDefinitionLookup: _ => SentinelDef(() => copies++))
            .Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard, "only YOUR graveyard is a legal source");
        copies.Should().Be(0);
    }

    [Fact]
    public void Etb_RoutesExileThroughZoneService_WhenSupplied()
    {
        var zones = new ZoneService(_bus);
        var a = OnBattlefield();
        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);

        var moved = 0;
        _bus.Subscribe<CardMovedEvent>(_ => moved++);

        BloodthirstyAdversaryFactory
            .BuildEtbEffect(a, _alice, timesPaid: 1,
                chosenTargets: new[] { (Card)bolt },
                zoneService: zones)
            .Execute();

        bolt.Zone.Should().Be(ZoneType.Exile);
        moved.Should().BeGreaterThan(0, "the graveyard→exile move publishes a CardMovedEvent");
    }

    [Fact]
    public void LegalTargets_FiltersByTypeManaValueAndOwner()
    {
        GraveInstant("Lightning Bolt", "{R}", _alice);          // legal (mv 1)
        GraveInstant("Lightning Bolt 2", "{1}{R}{R}", _alice);  // legal (mv 3)
        var big = new Sorcery("Big", "{2}{R}{R}") { Owner = _alice }; // mv 4 — illegal
        big.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(big);
        GraveInstant("Bob Bolt", "{R}", _bob);                 // wrong owner

        var legal = BloodthirstyAdversaryFactory.LegalTargets(_alice);

        legal.Should().HaveCount(2);
        legal.Should().OnlyContain(c => c.ManaCostValue.TotalValue <= 3);
    }
}
