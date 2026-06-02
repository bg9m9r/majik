using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Septic Rats (New Phyrexia, {1}{B}{B}).
///
/// Creature — Phyrexian Rat 2/2. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    Whenever this creature attacks, if defending player is poisoned, it
///    gets +1/+1 until end of turn."
///
/// ## Shape source
/// Card identity (name / Creature — Phyrexian Rat / {1}{B}{B} / 2/2) is loaded
/// from <c>Majik.Core/CardData/Cards/septic-rats.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The Infect keyword marker and the
/// intervening-if attack-trigger pump are layered on in code because the JSON
/// <see cref="AbilityDefinition"/> schema expresses neither — same posture as
/// <see cref="WerewolfPackLeaderFactory"/> (the suggested attack-trigger
/// intervening-if analogue) and the Infect markers on
/// <see cref="GlistenerElfFactory"/> / <see cref="PlagueStingerFactory"/>.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Phyrexian Rat (CR 205.3m) at {1}{B}{B}, owner / controller
///   wired.
/// - <b>Infect (CR 702.90)</b> — attached as a <see cref="KeywordAbility"/>
///   marker, identical posture to <see cref="GlistenerElfFactory"/> /
///   <see cref="PlagueStingerFactory"/>. The damage-replacement primitive
///   (poison counters to players, -1/-1 counters to creatures) is engine-side;
///   this factory contributes the structurally correct marker.
/// - <b>Attack-trigger pump (CR 508.1f trigger + CR 603.4 intervening-if)</b>:
///   a <see cref="TriggeredAbility"/> over <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   filtered via <see cref="Triggers.OnAttackSelf"/>. The "if defending player
///   is poisoned" clause is the trigger's
///   <see cref="TriggeredAbility.InterveningIf"/> (CR 603.4 — checked both as
///   the ability would be put on the stack and again on resolution). "Poisoned"
///   means the defending player has at least one poison counter (CR 122.3 — a
///   player with one or more poison counters is "poisoned"). The defending
///   player is read from the injected <paramref name="defendingPlayerSource"/>
///   closure — the same live-attack snapshot pattern Werewolf Pack Leader uses
///   for its attacker source. On resolution (with the intervening-if still met)
///   this creature gets +1/+1 until end of turn via a
///   <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c, CR 613.7c / CR 514.2
///   end-of-turn expiry) registered against the supplied
///   <see cref="ContinuousEffectsService"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Infect + the attack trigger are
///   attached; the intervening-if reads a null defending player (never poisoned,
///   so <see cref="TriggeredAbility.CanBePutOnStack"/> is false) and the pump
///   body is a no-op (no effects service). Suitable for dispatcher / shape
///   tests.
/// - <see cref="Create(Player, TriggerManager?, Func{Player?}?, ContinuousEffectsService?)"/>
///   — fully wired: the attack trigger registers with the
///   <see cref="TriggerManager"/>, the intervening-if reads the live defending
///   player, and the pump registers its Layer 7c effect.
///
/// ## Deferred (v1 gaps)
/// - <b>Infect damage replacement</b>: same engine-side gap as every Infect
///   card — the marker is present so combat / damage code can consult it once
///   the replacement primitive lands.
/// - <b>Live defending-player snapshot on the production load path</b>: the
///   intervening-if's defending-player source is injected. The production binder
///   chain builds the card via <see cref="Create(Player)"/> (no live snapshot),
///   so the pump is observable in tests / EV search but not auto-fed from the
///   combat manager yet — same posture as Werewolf Pack Leader's attacker
///   closure.
/// </summary>
[CardName("Septic Rats")]
public static class SepticRatsFactory
{
    public const string CardName = "Septic Rats";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "septic-rats";

    /// <summary>The +P/+T the attack trigger grants until end of turn.</summary>
    public const int PumpPower = 1;

    /// <summary>The +P/+T the attack trigger grants until end of turn.</summary>
    public const int PumpToughness = 1;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches Infect + the attack trigger structurally; the intervening-if
    /// reads a null defending player (never poisoned) and the pump body is a
    /// no-op (no effects service). No TriggerManager wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, defendingPlayerSource: null, effects: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the attack trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="defendingPlayerSource">Closure returning the player being
    /// attacked, read by the intervening-if to test "if defending player is
    /// poisoned". May be null — treated as no defending player, so the trigger
    /// never meets the condition.</param>
    /// <param name="effects">Continuous-effects service the pump registers its
    /// Layer 7c +1/+1 against. May be null — the trigger still resolves but no
    /// continuous effect is recorded.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player?>? defendingPlayerSource,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Creature — Phyrexian Rat / {1}{B}{B} / 2/2) from
        // the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Infect (CR 702.90) — keyword marker. The damage-replacement
        // primitive (poison counters on players, -1/-1 counters on creatures)
        // is engine-side; this factory exposes the marker so combat code can
        // consult it once the replacement lands. Same posture as Glistener Elf
        // / Plague Stinger.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // ----------------------------------------------------------------
        // "Whenever this creature attacks, if defending player is poisoned, it
        // gets +1/+1 until end of turn."
        //
        // CR 508.1f — attack trigger keyed on this creature (Triggers.OnAttackSelf).
        // CR 603.4  — "if …" is an intervening-if: checked as the ability would
        //             be put on the stack and again on resolution. Wired as the
        //             trigger's InterveningIf.
        // CR 122.3  — a player with one or more poison counters is "poisoned".
        // ----------------------------------------------------------------
        bool DefendingPlayerPoisoned()
        {
            var defender = defendingPlayerSource?.Invoke();
            // CR 122.3 — "poisoned" = at least one poison counter.
            return defender != null && defender.PoisonCounters >= 1;
        }

        var pumpEffect = new Effect(
            $"{CardName}: gets +{PumpPower}/+{PumpToughness} until EOT "
            + "(CR 603.4 intervening-if: defending player poisoned)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // CR 603.4 — re-check the intervening-if on resolution. If the
                // defending player is no longer poisoned (e.g. counters removed
                // in response), the ability does nothing.
                if (!DefendingPlayerPoisoned()) return;

                // CR 613.7c — +1/+1 with CR 514.2 end-of-turn expiry.
                effects.Register(new PumpUntilEndOfTurnEffect(
                    card, PumpPower, PumpToughness));
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { pumpEffect },
            interveningIf: DefendingPlayerPoisoned,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
