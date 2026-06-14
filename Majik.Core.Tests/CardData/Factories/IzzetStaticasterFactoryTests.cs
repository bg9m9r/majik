using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 707.2 / 109.2 — Izzet Staticaster's "{T}: deal 1 damage to target
/// creature and each other creature with the same name as that creature"
/// reads the EFFECTIVE name, so a clone of the target (a permanent that became
/// a copy via <see cref="CopyCharacteristicsEffect"/>) is counted even though
/// its immutable printed <see cref="Card.Name"/> differs. This exercises the
/// copy-immutable-name-mana-cost-on-instance pay-down: the same-name sweep now
/// consults <see cref="Permanent.GetEffectiveName"/>.
/// </summary>
public class IzzetStaticasterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature OnBattlefield(Creature c, Player owner)
    {
        c.SetOwner(owner);
        c.SetController(owner);
        c.Zone = Majik.Core.Zones.ZoneType.Battlefield;
        return c;
    }

    [Fact]
    public void Ping_HitsCloneOfTarget_ViaEffectiveName()
    {
        var svc = new ContinuousEffectsService();

        // The target: a printed Grizzly Bears.
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);

        // A clone whose PRINTED name is "Clone" but which became a copy of
        // Grizzly Bears (CR 707.2) — its effective name is "Grizzly Bears".
        var clone = OnBattlefield(new Creature("Clone", "{3}{U}", 0, 0), _alice);
        clone.ActiveEffects = svc;
        svc.Register(new CopyCharacteristicsEffect(clone, bear));

        // A printed-name twin that should also be hit.
        var bear2 = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);

        // An unrelated creature that should NOT be hit.
        var giant = OnBattlefield(new Creature("Hill Giant", "{3}{R}", 3, 3), _alice);

        var all = new List<Creature> { bear, clone, bear2, giant };
        var staticaster = IzzetStaticasterFactory.Create(_alice, () => all);

        var ability = staticaster.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var eff in ability.Effects) eff.Execute();

        // Primary target + printed twin + the clone (matched by effective name)
        // each took 1 damage; the unrelated Hill Giant did not.
        bear.Damage.Should().Be(1);
        bear2.Damage.Should().Be(1);
        clone.Damage.Should().Be(1, "the clone's effective name is Grizzly Bears (CR 707.2)");
        giant.Damage.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Re-source-safe (agatha-bespoke-migration-remaining-audit-grep-tail) —
    // the {T}: ping ability re-homes onto a new bearer via
    // ActivatedAbility.RebindTo (Agatha's Soul Cauldron group-grant; CR 707.2
    // / 613.1f). The effect reads ChosenTargets / source off the live
    // ResolutionContext rather than capturing the authoring permanent, so the
    // re-sourced copy taps the NEW bearer (Stage-1 cost re-home) and still
    // damages its own chosen target + the same-name sweep.
    // -----------------------------------------------------------------------

    [Fact]
    public void Staticaster_PingAbility_IsRebindSafe()
    {
        var staticaster = IzzetStaticasterFactory.Create(_alice);
        var ability = staticaster.Abilities.OfType<ActivatedAbility>().Single();

        ability.RebindSafe.Should().BeTrue(
            "the ping reads its target off ResolutionContext.ChosenTargets and "
            + "its tap cost re-homes, so Agatha's group-grant may RebindTo it");
    }

    [Fact]
    public void RebindTo_PingAbility_TapsNewBearer_AndDamagesChosenTarget()
    {
        // Arrange — Staticaster (the printed source) plus a DIFFERENT creature
        // (the counter-bearer Agatha re-homes the ability to).
        var staticaster = IzzetStaticasterFactory.Create(_alice);
        OnBattlefield(staticaster, _alice);

        var bearer = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);
        // CR 302.6 — the bearer has been under control since before this turn,
        // so the {T} tap cost is legal (mirrors a real Agatha-grant scenario).
        bearer.ClearSummoningSickness();

        var victim = OnBattlefield(new Creature("Hill Giant", "{3}{R}", 3, 3), _alice);

        var ability = staticaster.Abilities.OfType<ActivatedAbility>().Single();

        // Act — re-home the ENTIRE ability onto the bearer (CR 707.2 / 613.1f).
        var rebound = ability.RebindTo(bearer, _alice);

        // STAGE 1 — pay the re-homed tap cost: it must tap the bearer, not the
        // original Staticaster.
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        rebound.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });
        rebound.Resolve();

        // Tap re-homed: the bearer is tapped; the original Staticaster is not.
        bearer.IsTapped.Should().BeTrue("the rebound tap cost hits the bearer");
        staticaster.IsTapped.Should().BeFalse("the original Staticaster is untouched");

        // Damage re-homed: the rebound ability still deals 1 to its own target.
        victim.Damage.Should().Be(1, "the rebound ability damages its chosen target");
    }
}
