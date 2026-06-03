using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.12-ish bestow-on-death — the reusable engine primitive behind
/// "return-to-battlefield-as-an-Aura on dies" (Old-Growth Troll).
///
/// When a normally-creature permanent dies, this effect returns it to the
/// battlefield as an <b>Enchantment — Aura</b> attached to a host the
/// controller controls, and grants that host a set of abilities while the
/// Aura remains attached. It composes three pieces that already exist in the
/// engine:
///   1. <see cref="ZoneService"/> battlefield entry (CR 614 / 303.4) — the
///      returning object is a NEW object (CR 614.12), modelled here as a fresh
///      <see cref="Enchantment"/> Aura carrying the dead permanent's name;
///   2. the host-attachment rail (<see cref="Permanent.AttachTo"/>, CR 303.4f
///      — an Aura enters the battlefield already attached to its host);
///   3. the ability-granting rail (<see cref="GrantAbilityEffect"/>, CR 613.1f
///      — the granted abilities live on the host while the Aura is attached).
///
/// <para>Host selection (CR 303.4 — an Aura can only enter if there is a legal
/// object to enchant): the effect reads the controller's battlefield through a
/// caller-supplied predicate and takes the first match. If there is no legal
/// host, the return does not happen (the dead card stays in the graveyard) —
/// CR 303.4g, an Aura with no legal object to enchant can't enter.</para>
///
/// <para>v1 host-pick is deterministic (first matching permanent) rather than
/// agent-driven; "which host" is a target detail, not the return-as-Aura
/// mechanic this primitive ships. Granted abilities are reconciled by the
/// <see cref="ContinuousEffectsService.Compute"/> pass via each
/// <see cref="GrantAbilityEffect"/> (keyed on the Aura's
/// <see cref="Permanent.AttachedTo"/>), so they follow the Aura and drop when
/// it leaves play (CR 613.6e).</para>
/// </summary>
public static class ReturnAsAuraOnDeathEffect
{
    /// <summary>
    /// Perform the return-as-Aura. Builds a fresh Enchantment — Aura named
    /// <paramref name="auraName"/> for the dead permanent's controller, picks
    /// the first host on the controller's battlefield matching
    /// <paramref name="hostPredicate"/>, puts the Aura onto the battlefield
    /// attached to that host (CR 303.4f), and registers a
    /// <see cref="GrantAbilityEffect"/> for each ability built by
    /// <paramref name="grantedAbilityFactories"/> against the supplied
    /// <paramref name="continuousEffects"/> service.
    /// </summary>
    /// <returns>The materialised Aura, or <c>null</c> when no legal host was
    /// available (CR 303.4g) — in which case nothing entered.</returns>
    public static Enchantment? Apply(
        Permanent deadPermanent,
        string auraName,
        string auraManaCost,
        Func<Permanent, bool> hostPredicate,
        IReadOnlyList<Func<Permanent, IAbility>> grantedAbilityFactories,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(deadPermanent);
        ArgumentNullException.ThrowIfNull(auraName);
        ArgumentNullException.ThrowIfNull(hostPredicate);
        ArgumentNullException.ThrowIfNull(grantedAbilityFactories);

        var controller = deadPermanent.Controller ?? deadPermanent.Owner;
        if (controller == null)
        {
            return null;
        }

        // CR 303.4 / 303.4g — an Aura can only enter the battlefield if there
        // is a legal object for it to enchant.
        var host = controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(p => p.Zone == ZoneType.Battlefield && hostPredicate(p));

        if (host == null)
        {
            return null;
        }

        // CR 614.12 — the returning object is a new object. Model it as a
        // fresh Enchantment — Aura carrying the dead permanent's name.
        var aura = new Enchantment(
            auraName,
            auraManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        aura.SetOwner(deadPermanent.Owner ?? controller);
        aura.SetController(controller);

        // Battlefield entry through ZoneService so CardMovedEvent fires
        // (ETB observers see it enter). The Aura is minted off-battlefield
        // first (Library sentinel, mirroring TokenFactory) then moved.
        aura.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(aura);

        if (zones != null)
        {
            zones.MoveCardTo(aura, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(aura);
            aura.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(aura);
        }

        // CR 303.4f — the Aura enters already attached to the chosen host.
        aura.AttachTo(host);

        // CR 613.1f — grant each ability to the host while the Aura is
        // attached. The selector follows the Aura's AttachedTo so the grant
        // tracks re-attachment and drops when the Aura leaves play.
        if (continuousEffects != null)
        {
            foreach (var abilityFactory in grantedAbilityFactories)
            {
                var grant = new GrantAbilityEffect(
                    source: aura,
                    targetSelector: () => aura.AttachedTo,
                    abilityFactory: abilityFactory);
                continuousEffects.Register(grant);
                // Reconcile the grant immediately so the host carries the
                // ability without waiting for the next Compute pass.
                grant.Sync();
            }
        }

        return aura;
    }
}
