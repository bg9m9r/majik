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
}
