using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for <see cref="DeathBaronFactory"/>.
///
/// Death Baron — Creature — Zombie Wizard {1}{B}{B} 2/2. Oracle text:
///   "Skeletons you control and other Zombies you control get +1/+1 and
///    have deathtouch."
///
/// Modelled on <see cref="LordOfAtlantisFactory"/> / Elvish Archdruid: a
/// tribal lord that grants +1/+1 and a keyword. Unlike a single-tribe
/// lord, Death Baron buffs TWO subtypes (Skeleton AND Zombie), wired as
/// two controller-scoped <see cref="LordStaticEffect"/>s. The "other"
/// qualifier applies only to the Zombie clause; since Death Baron is a
/// Zombie (not a Skeleton), it never buffs itself under either clause.
///
/// Covers:
/// - Identity (name, type, mana cost, Zombie + Wizard subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Buffs controller's Skeletons (+1/+1 + Deathtouch).
/// - Buffs controller's OTHER Zombies (+1/+1 + Deathtouch).
/// - Does not self-buff (it's a Zombie, but "other Zombies").
/// - Controller-scoped: opponent's Zombies / Skeletons unaffected.
/// - Non-Zombie/non-Skeleton creatures unaffected.
/// - LTB lifts the bonus (IsActive gate).
/// </summary>
public class DeathBaronTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ─── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void DeathBaron_Identity()
    {
        var baron = DeathBaronFactory.Create(_alice);

        baron.Name.Should().Be("Death Baron");
        baron.ManaCost.Should().Be("{1}{B}{B}");
        baron.HasType(CardType.Creature).Should().BeTrue();
        baron.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        baron.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        baron.BasePower.Should().Be(2);
        baron.BaseToughness.Should().Be(2);
        baron.Owner.Should().BeSameAs(_alice);
        baron.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeathBaron_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Death Baron", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Death Baron");
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
    }

    // ─── Skeleton clause ──────────────────────────────────────────────────────

    [Fact]
    public void DeathBaron_BuffsOwnSkeleton_Plus1Plus1AndDeathtouch()
    {
        var svc = new ContinuousEffectsService();

        var skeleton = MakeCreature("Drudge Skeletons", _alice, 1, 1, CardSubtype.Skeleton, svc);
        var baron = DeathBaronFactory.Create(_alice, svc);
        baron.Zone = ZoneType.Battlefield;
        baron.ActiveEffects = svc;

        skeleton.GetPower().Should().Be(2, "Death Baron gives Skeletons you control +1/+1.");
        skeleton.GetToughness().Should().Be(2);
        HasDeathtouch(skeleton).Should().BeTrue("Death Baron grants Deathtouch to your Skeletons.");
    }

    // ─── Zombie clause ────────────────────────────────────────────────────────

    [Fact]
    public void DeathBaron_BuffsOwnOtherZombie_Plus1Plus1AndDeathtouch()
    {
        var svc = new ContinuousEffectsService();

        var zombie = MakeCreature("Diregraf Ghoul", _alice, 2, 2, CardSubtype.Zombie, svc);
        var baron = DeathBaronFactory.Create(_alice, svc);
        baron.Zone = ZoneType.Battlefield;
        baron.ActiveEffects = svc;

        zombie.GetPower().Should().Be(3, "Death Baron gives other Zombies you control +1/+1.");
        zombie.GetToughness().Should().Be(3);
        HasDeathtouch(zombie).Should().BeTrue("Death Baron grants Deathtouch to your other Zombies.");
    }

    // ─── No self-buff ─────────────────────────────────────────────────────────

    [Fact]
    public void DeathBaron_DoesNotSelfBuff()
    {
        var svc = new ContinuousEffectsService();

        var baron = DeathBaronFactory.Create(_alice, svc);
        baron.Zone = ZoneType.Battlefield;
        baron.ActiveEffects = svc;

        // "other Zombies" — Death Baron is a Zombie but excluded by "other";
        // it is not a Skeleton, so the Skeleton clause never applies to it.
        baron.GetPower().Should().Be(2, "Death Baron does not buff itself (other Zombies).");
        baron.GetToughness().Should().Be(2);
        HasDeathtouch(baron).Should().BeFalse("Death Baron grants no Deathtouch to itself.");
    }

    // ─── Controller scope ─────────────────────────────────────────────────────

    [Fact]
    public void DeathBaron_DoesNotBuff_OpponentZombie()
    {
        // "Zombies you control" — controller-scoped. Opponent's Zombies unaffected.
        var svc = new ContinuousEffectsService();

        var bobZombie = MakeCreature("Diregraf Ghoul", _bob, 2, 2, CardSubtype.Zombie, svc);
        var baron = DeathBaronFactory.Create(_alice, svc);
        baron.Zone = ZoneType.Battlefield;
        baron.ActiveEffects = svc;

        bobZombie.GetPower().Should().Be(2, "Death Baron only buffs Zombies you control.");
        bobZombie.GetToughness().Should().Be(2);
        HasDeathtouch(bobZombie).Should().BeFalse("opponent's Zombies don't get Deathtouch.");
    }

    [Fact]
    public void DeathBaron_DoesNotBuff_NonZombieNonSkeleton()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear, svc);
        var baron = DeathBaronFactory.Create(_alice, svc);
        baron.Zone = ZoneType.Battlefield;
        baron.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "Death Baron only buffs Skeletons and Zombies.");
        bear.GetToughness().Should().Be(2);
        HasDeathtouch(bear).Should().BeFalse("unrelated creatures don't get Deathtouch.");
    }

    // ─── LTB ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DeathBaron_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var zombie = MakeCreature("Diregraf Ghoul", _alice, 2, 2, CardSubtype.Zombie, svc);
        var baron = DeathBaronFactory.Create(_alice, svc);
        baron.Zone = ZoneType.Battlefield;
        baron.ActiveEffects = svc;

        zombie.GetPower().Should().Be(3);

        baron.SetZone(ZoneType.Graveyard);

        zombie.GetPower().Should().Be(2, "bonus lifts when Death Baron leaves the battlefield.");
        zombie.GetToughness().Should().Be(2);
        HasDeathtouch(zombie).Should().BeFalse(
            "Deathtouch grant lifts when Death Baron leaves the battlefield.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Creature MakeCreature(
        string name,
        Player controller,
        int power,
        int toughness,
        CardSubtype subtype,
        ContinuousEffectsService svc)
    {
        var c = new Creature(name, "B", power, toughness, subtypes: new[] { subtype })
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        return c;
    }

    private static bool HasDeathtouch(Creature c)
    {
        var chars = c.ActiveEffects?.Compute(c);
        if (chars is null)
        {
            return c.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>()
                .Any(k => k.Keyword == "Deathtouch");
        }
        return chars.Keywords.Contains("Deathtouch");
    }
}
