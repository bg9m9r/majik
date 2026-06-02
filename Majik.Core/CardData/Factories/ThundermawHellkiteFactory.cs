using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thundermaw Hellkite (Magic 2013, {3}{R}{R}).
/// Creature — Dragon, 5/5.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Flying
///    Haste (This creature can attack and {T} as soon as it comes under
///    your control.)
///    When this creature enters, it deals 1 damage to each creature with
///    flying your opponents control. Tap those creatures."
///
/// ## Implemented (v1)
///
/// - 5/5 Creature — Dragon at {3}{R}{R}, owner/controller wired. Base shape
///   materialised from the embedded JSON definition
///   (<c>thundermaw-hellkite.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="GlorybringerFactory"/> / <see cref="GraveTitanFactory"/>).
/// - <b>Flying (CR 702.9) + Haste (CR 702.10)</b> — keyword markers via
///   <see cref="KeywordAbility"/>, read by the combat/block subsystem the
///   same way <see cref="StormbreathDragonFactory"/> wires them.
/// - <b>"When this creature enters, it deals 1 damage to each creature with
///   flying your opponents control. Tap those creatures." (CR 603.6a)</b> —
///   a single <see cref="TriggeredAbility"/> on <see cref="Triggers.OnEnterBattlefieldSelf"/>.
///   On resolution it iterates the candidate pool supplied by
///   <paramref name="opponentCreaturesResolver"/>, filters to creatures with
///   flying (<see cref="CombatAbilities.HasFlying"/>, CR 702.9) that an
///   opponent controls (CR 109.2 — controller is not Thundermaw's
///   controller), deals 1 damage to each (<see cref="Creature.TakeDamage"/>,
///   CR 119.3), then taps each (<see cref="Majik.Core.Cards.Permanent.Tap"/>,
///   guarded by <see cref="Majik.Core.Cards.Permanent.IsTapped"/> so an
///   already-tapped flyer is a no-op — CR 701.21a).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (dispatcher / structural
///   tests). The ETB trigger is attached for shape observability but, with no
///   resolver and no TriggerManager, damages/taps nothing.
/// - <see cref="Create(Player, TriggerManager?, Func{IReadOnlyList{Creature}}?)"/>
///   — supplies the TriggerManager and the opponent-creature pool resolver.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Trigger-on-stack timing</b>: the damage + tap run immediately when the
///   ETB effect executes rather than queuing on the stack and resolving in
///   APNAP order (CR 603.3). Observationally equivalent for this one-shot
///   sweep (same posture as <see cref="GraveTitanFactory"/>).
/// - <b>Agent-driven candidate pool</b>: the opponent-creature pool is a
///   closure rather than a live battlefield query; the factory itself enforces
///   the "with flying" + "opponent controls" legality gate. Same closure-
///   injection posture as <see cref="GlorybringerFactory"/>'s target resolver.
/// - <b>Damage prevention / replacement (CR 615)</b>: damage routes directly
///   through <see cref="Creature.TakeDamage"/>, the same as other ping effects.
/// </summary>
[CardName("Thundermaw Hellkite")]
public static class ThundermawHellkiteFactory
{
    public const string CardName = "Thundermaw Hellkite";
    public const string Slug = "thundermaw-hellkite";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>CR 119.3 — damage dealt to each opponent-controlled flyer.</summary>
    public const int EtbDamage = 1;

    /// <summary>
    /// Construct Thundermaw Hellkite with no live wiring (the shape /
    /// dispatcher path). The ETB trigger is attached for shape observability
    /// but damages/taps nothing (no resolver). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, opponentCreaturesResolver: null);

    /// <summary>
    /// Construct Thundermaw Hellkite with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the ETB trigger is registered
    /// with so it surfaces as pending. May be null — the trigger is still
    /// attached to the card shape.</param>
    /// <param name="opponentCreaturesResolver">Closure returning the candidate
    /// pool of opponent creatures for the ETB sweep. The factory filters this
    /// pool to flyers an opponent controls (CR 702.9 / CR 109.2) before
    /// dealing 1 damage + tapping. May be null — no resolver means no targets,
    /// so no damage or taps.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? opponentCreaturesResolver = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Dragon,
        // {3}{R}{R}, 5/5). No abilities in the JSON — the keyword markers + ETB
        // trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 / 702.10 — Flying + Haste keyword markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // "When this creature enters, it deals 1 damage to each creature with
        // flying your opponents control. Tap those creatures." (CR 603.6a.)
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: on enter, 1 damage to each opponent flyer, then tap those creatures",
            () => ResolveEtb(card, owner, opponentCreaturesResolver));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 113.6 — the ability functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    private static void ResolveEtb(
        Creature card,
        Player owner,
        Func<IReadOnlyList<Creature>>? opponentCreaturesResolver)
    {
        var pool = opponentCreaturesResolver?.Invoke();
        if (pool == null) return;

        var controller = card.Controller ?? owner;

        foreach (var c in pool)
        {
            if (c == null) continue;
            // "each creature with flying your opponents control" — CR 702.9 +
            // CR 109.2. Filter to flyers an opponent controls.
            if (!CombatAbilities.HasFlying(c)) continue;
            if (ReferenceEquals(c.Controller, controller)) continue;

            // CR 119.3 — deal 1 damage.
            c.TakeDamage(EtbDamage);

            // "Tap those creatures." CR 701.21a — tapping an already-tapped
            // permanent has no effect (and Permanent.Tap throws on a tapped
            // permanent), so guard on IsTapped.
            if (!c.IsTapped) c.Tap();
        }
    }
}
