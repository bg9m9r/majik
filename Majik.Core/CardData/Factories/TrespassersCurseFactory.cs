using System.Runtime.CompilerServices;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Trespasser's Curse (Shadows over Innistrad,
/// {1}{B}).
///
/// Enchantment — Aura Curse. Oracle text:
///   "Enchant player
///    Whenever a creature enters under enchanted player's control, that
///    player loses 1 life and you gain 1 life."
///
/// ## Implemented (v1)
/// - Card identity: <see cref="Enchantment"/> {1}{B} with subtypes
///   <see cref="CardSubtype.Aura"/> + <see cref="CardSubtype.Curse"/>
///   (CR 205.3h — the Curse subtype was added in this PR alongside the
///   factory; Curses are always Auras that enchant a player).
/// - <b>"Enchant player"</b>: the engine's <see cref="Permanent.AttachTo"/>
///   only targets other permanents, so the enchanted player is stored on
///   a side-channel weak-table keyed by the Curse instance
///   (<see cref="GetEnchantedPlayer"/> / <see cref="SetEnchantedPlayer"/>).
///   This avoids retrofitting <see cref="Permanent"/> with a player-attach
///   field for one card while keeping the per-Curse target stable across
///   the trigger's lifetime. Future Aura-player plumbing (Curse of Bounty,
///   Curse of Misfortunes, ...) can lift this onto a shared base.
/// - <b>Creature-ETB triggered ability (CR 119.3 / 603.6a)</b>: Fires on
///   <see cref="CardMovedEvent"/> with <see cref="ZoneType.Battlefield"/>
///   in the <c>ToZone</c> slot, the moved card is a
///   <see cref="CardType.Creature"/>, AND the card's controller equals
///   the Curse's enchanted player. On resolution: enchanted player loses
///   1 life, Curse controller gains 1 life. No targets (the affected
///   parties are determined by enchantment + trigger event).
/// - <b>Battlefield gate</b>: the trigger's <see cref="ITriggeredAbility.ActiveZones"/>
///   are restricted to <see cref="ZoneType.Battlefield"/> so the Curse
///   stops firing as soon as it leaves play (CR 603.10c).
///
/// ## Combo / interaction
/// - Symmetric ETB-payoff family — pairs with token swarms (Hordeling
///   Outburst, Empty the Warrens) cast by the enchanted opponent for
///   per-token 1-drain ticks. Each token's ETB is a separate
///   <see cref="CardMovedEvent"/>, so the trigger fires N times for N
///   tokens entering simultaneously (CR 614 — replacement bus per-event).
/// - Pairs with Exquisite Blood for an opponent-creature-driven drain
///   loop (each ETB drains 1 → Exquisite Blood gains 1 → that 1 gain
///   does NOT loop back through Trespasser's Curse because the gain
///   isn't an ETB event).
///
/// ## Deferred (v1 gaps)
/// - <b>Auto-attach to a player via the spell-cast flow</b>: the factory
///   exposes <see cref="SetEnchantedPlayer"/> so tests + future wiring
///   can stamp the enchanted player explicitly. The factory does NOT
///   build a <see cref="SpellDefinition"/> with a "target player" prompt
///   (mirrors Animate Dead's posture — the cast-time SpellDefinition
///   lives on a helper, not the factory itself).
/// - <b>Hexproof / shroud on cast</b>: standard Aura target legality
///   (CR 303.4f / 702.11) deferred to the broader Aura plumbing — same
///   posture as the rest of the named-Aura family.
/// </summary>
[CardName("Trespasser's Curse")]
public static class TrespassersCurseFactory
{
    public const string CardName = "Trespasser's Curse";
    public const string PrintedManaCost = "{1}{B}";
    public const int LifeSwing = 1;

    // Side-channel storage for the enchanted player. Keyed by the Curse
    // instance so each cast has its own enchanted-player slot without
    // bleeding across copies. ConditionalWeakTable lets the Curse be GC'd
    // normally when it leaves play permanently.
    private static readonly ConditionalWeakTable<Enchantment, PlayerHolder> _enchanted =
        new();

    private sealed class PlayerHolder { public Player? Player; }

    /// <summary>
    /// Returns the player this Curse is enchanting (CR 303.4 — "Enchant
    /// player"). Returns null if no enchanted player has been stamped
    /// yet (e.g. the Curse was built directly, not via the cast flow).
    /// </summary>
    public static Player? GetEnchantedPlayer(Enchantment curse)
    {
        ArgumentNullException.ThrowIfNull(curse);
        return _enchanted.TryGetValue(curse, out var holder) ? holder.Player : null;
    }

    /// <summary>
    /// Stamp the enchanted player onto <paramref name="curse"/>. Called by
    /// the cast-time spell definition once the agent has chosen a player,
    /// or by tests bypassing the cast flow.
    /// </summary>
    public static void SetEnchantedPlayer(Enchantment curse, Player player)
    {
        ArgumentNullException.ThrowIfNull(curse);
        ArgumentNullException.ThrowIfNull(player);
        var holder = _enchanted.GetValue(curse, _ => new PlayerHolder());
        holder.Player = player;
    }

    /// <summary>
    /// Construct Trespasser's Curse. The ETB-creature trigger is attached
    /// to the card shape but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for card-shape / dispatcher
    /// tests — tests fire the trigger by invoking its effect directly
    /// after calling <see cref="SetEnchantedPlayer"/>.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Trespasser's Curse with the ETB-creature trigger
    /// registered against <paramref name="triggers"/> when supplied.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura, CardSubtype.Curse });
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Creature-ETB triggered ability — CR 119.3 / 603.6a.
        //   "Whenever a creature enters under enchanted player's control,
        //    that player loses 1 life and you gain 1 life."
        // Fires on CardMovedEvent → Battlefield, moved card is a Creature,
        // controller equals the side-channel enchanted player.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: enchanted player loses {LifeSwing}, controller gains {LifeSwing}",
            () =>
            {
                var enchanted = GetEnchantedPlayer(card);
                if (enchanted == null) return;
                if (card.Controller == null) return;

                if (!enchanted.HasLost) enchanted.LoseLife(LifeSwing);
                if (!card.Controller.HasLost) card.Controller.GainLife(LifeSwing);
            });

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;

            var enchanted = GetEnchantedPlayer(card);
            if (enchanted == null) return false;
            return ReferenceEquals(e.Card.Controller, enchanted);
        });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
