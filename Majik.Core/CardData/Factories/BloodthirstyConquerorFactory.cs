using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodthirsty Conqueror (March of the Machine,
/// {3}{B}{B}).
///
/// Creature — Vampire Knight 5/5. Oracle text (verified against Scryfall):
///   "Flying, deathtouch
///    Whenever an opponent loses life, you gain that much life. (Damage
///    causes loss of life.)"
///
/// ## Why it gets its own factory
/// The lifegain trigger is the exact mirror of <see cref="ExquisiteBloodFactory"/>
/// ("Whenever an opponent loses life, you gain that much life") — there is no
/// JSON <c>whenever_an_opponent_loses_life</c> trigger def, so the trigger is
/// hand-built in code (same shape as Exquisite Blood). The card SHAPE — the
/// printed Flying (CR 702.9) + Deathtouch (CR 702.2) keywords, 5/5 Vampire
/// Knight, {3}{B}{B} — comes from the embedded JSON
/// (<c>bloodthirsty-conqueror.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The JSON <c>keywords</c> array
/// materializes <see cref="KeywordAbility"/> markers that
/// <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
/// <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/> read for the
/// combat pipeline. No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Card identity (Creature — Vampire Knight {3}{B}{B} 5/5, owner /
///   controller wiring) from the embedded JSON.
/// - <b>Flying (CR 702.9) + Deathtouch (CR 702.2)</b>: printed keywords
///   declared in the JSON's <c>"keywords"</c> array.
/// - <b>Opponent-life-loss → controller-gain triggered ability
///   (CR 119.3 / 603.6a)</b>: Fires on <see cref="LifeChangedEvent"/> where
///   (a) the event's <see cref="LifeChangedEvent.Player"/> is NOT the
///   controller (every other player is an opponent — CR 102.2), and (b) the
///   life total strictly decreased (NewLife &lt; PreviousLife). The lost
///   amount is captured at trigger time (CR 603.2a) via a closure-mutable
///   holder. On resolution the controller gains N life
///   (N = PreviousLife − NewLife). No targets — the gain is auto-applied to
///   the controller. Active only on the battlefield (CR 113.6).
///
/// ## Deferred (v1 gaps)
/// - <b>Multiple opponents</b>: 2HG / multiplayer formats — the printed
///   trigger reads "an opponent" which fires per-opponent. The condition
///   correctly matches any non-controller player; the gain is applied once
///   per matching event. For 1v1 (the engine's Modern target) this is exact.
///   Same posture as <see cref="ExquisiteBloodFactory"/>.
/// </summary>
[CardName("Bloodthirsty Conqueror")]
public static class BloodthirstyConquerorFactory
{
    public const string CardName = "Bloodthirsty Conqueror";
    public const string Slug = "bloodthirsty-conqueror";
    public const string PrintedManaCost = "{3}{B}{B}";

    /// <summary>
    /// Build Bloodthirsty Conqueror. The life-loss trigger is attached to the
    /// card shape; on the production routed build the trigger auto-binds via
    /// <see cref="TriggerManager.BindCard"/> when it enters the battlefield.
    /// Card-shape / dispatcher tests fire the ability by invoking its effect
    /// directly.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Build Bloodthirsty Conqueror with the life-loss trigger registered
    /// against <paramref name="triggers"/> when supplied.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Card shape (type / subtypes / cost / P-T / Flying + Deathtouch
        // keyword markers) comes from the embedded JSON definition.
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        // ----------------------------------------------------------------
        // Opponent-loss → controller-gain triggered ability —
        // CR 119.3 / 603.6a.
        //   "Whenever an opponent loses life, you gain that much life."
        // Captures the loss delta at trigger time (CR 603.2a) via a mutable
        // holder; the resolved effect reads it and applies the gain to the
        // controller. Mirror of ExquisiteBloodFactory (per-instance closure,
        // last-write-wins on batched events — exact for the 1v1 flow).
        // ----------------------------------------------------------------
        var lastLossAmount = new int[1]; // closure-mutable holder

        var gainEffect = new Effect(
            $"{CardName}: you gain N life (N = life an opponent just lost)",
            () =>
            {
                var amount = lastLossAmount[0];
                if (amount <= 0) return;
                if (card.Controller == null) return;
                if (card.Controller.HasLost) return; // CR 614 — can't gain after loss

                card.Controller.GainLife(amount);
            });

        var condition = new EventTriggerCondition<LifeChangedEvent>((e, _) =>
        {
            // "an opponent loses life" — event player is NOT the controller
            // (CR 102.2), and the life total strictly decreased.
            if (ReferenceEquals(e.Player, card.Controller)) return false;
            if (e.NewLife >= e.PreviousLife) return false;
            lastLossAmount[0] = e.PreviousLife - e.NewLife;
            return true;
        });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { gainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
