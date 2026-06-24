using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DoppelgangFactory"/> (Streets of New Capenna,
/// {X}{X}{X}{G}{U}).
///
/// Scryfall oracle (verbatim, verified 2026-06-24):
///   "For each of X target permanents, create X tokens that are copies of
///    that permanent."
///
/// X-scaled, multi-target generalisation of the copy-a-permanent token
/// mechanic shared by <see cref="CacklingCounterpartFactory"/> /
/// <see cref="EsikasChariotFactory"/>. The single chosen X both fixes the
/// number of targets AND the number of copies each target makes (CR 107.3),
/// so N chosen targets spawn N copies each (N*N tokens total).
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity ({X}{X}{X}{G}{U} Sorcery, Simic).
/// - Spell definition shape: variable X + one open-cardinality "X target
///   permanents" request gathering every permanent on every battlefield.
/// - Resolve: X = chosen-target count; each chosen creature spawns X token
///   copies under the caster's control (CR 706.2 / CR 707.2).
/// - Non-creature target is a clean no-op (v1 copy-token lossiness, CR 608.2b).
/// </summary>
[Trait("Color", "M")]
public class DoppelgangFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature MakeCreature(string name, int p, int t, Player controller, params string[] keywords)
    {
        var c = new Creature(name, "{1}{G}", p, t);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        foreach (var kw in keywords)
        {
            c.AddAbility(new KeywordAbility(kw, c, controller));
        }
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Doppelgang_Identity_SorceryAtXXXGU()
    {
        var card = DoppelgangFactory.Create(_alice);

        card.Name.Should().Be("Doppelgang");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{X}{X}{X}{G}{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Doppelgang_SpellDefinition_HasVariableXAndOpenCardinalityTarget()
    {
        var def = DoppelgangFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeTrue("X is chosen as the spell is cast (CR 601.2f)");
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.Description.Should().Be("X target permanents");
        req.MinTargets.Should().Be(0);
        req.MaxTargets.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Doppelgang_CandidateGatherer_GathersEveryPermanentOnEveryBattlefield()
    {
        var mine = MakeCreature("Grizzly Bears", 2, 2, _alice);
        var theirs = MakeCreature("Hill Giant", 3, 3, _bob);

        var def = DoppelgangFactory.BuildSpellDefinition(_alice);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx).ToList();

        // CR 109 — Doppelgang has no controller restriction: a permanent ANY
        // player controls is a legal target.
        candidates.Should().Contain(mine);
        candidates.Should().Contain(theirs);
    }

    [Fact]
    public void Doppelgang_Resolve_TwoTargets_SpawnsTwoCopiesEach()
    {
        // CR 107.3 — X = 2 chosen targets, so each target makes 2 copies
        // (4 tokens total).
        var bears = MakeCreature("Grizzly Bears", 2, 2, _alice, "flying");
        var giant = MakeCreature("Hill Giant", 3, 3, _alice);

        var created = DoppelgangFactory.Resolve(
            _alice,
            new object[] { bears, giant },
            zones: null);

        created.Should().HaveCount(4, "2 targets * X(=2) copies each");
        created.Should().OnlyContain(c => c.IsToken);
        created.Should().OnlyContain(c => ReferenceEquals(c.Controller, _alice),
            "CR 707.2 — copy token's controller is the effect's controller");

        var bearCopies = created.Where(c => c.Name == "Grizzly Bears").ToList();
        bearCopies.Should().HaveCount(2);
        bearCopies.Should().OnlyContain(c => c.BasePower == 2 && c.BaseToughness == 2);
        bearCopies.Should().OnlyContain(c =>
            c.Abilities.OfType<KeywordAbility>()
                .Any(k => k.Keyword.Equals("flying", StringComparison.OrdinalIgnoreCase)),
            "CR 706.2 — keyword abilities are copiable values");

        created.Where(c => c.Name == "Hill Giant").Should().HaveCount(2);
    }

    [Fact]
    public void Doppelgang_Resolve_SingleTarget_SpawnsOneCopy()
    {
        // X = 1 chosen target ⇒ 1 copy.
        var bears = MakeCreature("Grizzly Bears", 2, 2, _alice);

        var created = DoppelgangFactory.Resolve(_alice, new object[] { bears }, zones: null);

        created.Should().ContainSingle();
        created[0].Name.Should().Be("Grizzly Bears");
    }

    [Fact]
    public void Doppelgang_Resolve_NonCreatureTarget_NoOp()
    {
        // CR 608.2b + v1 copy-token lossiness — a non-creature permanent target
        // is a clean no-op (TokenFactory only mints creature tokens). Same
        // posture as Cackling Counterpart / Esika's Chariot.
        var land = new Land("Forest");
        land.SetOwner(_alice);
        land.SetController(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);

        var created = DoppelgangFactory.Resolve(_alice, new object[] { land }, zones: null);

        created.Should().BeEmpty();
    }
}
