using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Marketwatch Phantom (Murders at Karlov Manor,
/// {1}{W}).
///
/// Creature — Spirit Detective 2/2. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Whenever another creature you control with power 2 or less enters,
///    this creature gains flying until end of turn."
///
/// ## Implemented (v1)
///
/// - <b>2/2 Creature — Spirit Detective at {1}{W}</b> (CR 205.3m). No printed
///   Flying — Flying is only ever granted by the trigger below, then expires
///   at cleanup.
/// - <b>"Another creature you control with power 2 or less enters" trigger
///   (CR 603.6e / CR 109.5)</b> — fires on a
///   <see cref="CardMovedEvent"/> whose moved object is a creature OTHER than
///   this permanent (CR 109.5 "another"), entering the battlefield under the
///   trigger controller's control ("you control"), whose current power is 2 or
///   less. CR 603.2e / 603.3 — a creature's power "as it enters" is read off
///   the entering object at trigger time. The self-exclusion is the
///   <c>!ReferenceEquals</c> guard; the source's OWN entry never fires it.
///   <para>
///   The controller is resolved LIVE off <c>card.Controller</c> each time the
///   condition is evaluated, so a control change carries the trigger with the
///   permanent (CR 109.5 — same posture as the declarative
///   <c>whenever_another_creature_enters</c> trigger with <c>YouControlOnly</c>).
///   </para>
/// - <b>"This creature gains flying until end of turn" (CR 514.2 / CR 613.1c
///   Layer 6)</b> — on resolution a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> grants the source "Flying"
///   until the cleanup step. When a
///   <see cref="ContinuousEffectsService"/> is supplied the grant registers on
///   it; the shape-only path attaches the trigger but cannot mutate keywords
///   (mirrors <see cref="PsychicFrogFactory"/>'s flying-grant posture).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (trigger attached, NOT
///   registered, no continuous-effects service). Suitable for dispatcher /
///   shape tests.
/// - <see cref="Create(Player, TriggerManager?, ContinuousEffectsService?)"/>
///   — runtime-wired.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Marketwatch Phantom")]
public static class MarketwatchPhantomFactory
{
    public const string CardName = "Marketwatch Phantom";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int PowerThreshold = 2;
    public const string GrantedKeyword = "Flying";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "marketwatch-phantom";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Marketwatch Phantom with no live wiring. The
    /// "another creature you control with power 2 or less enters" trigger is
    /// attached for shape, but NOT registered and the flying grant has no
    /// continuous-effects service. Suitable for factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, effects: null);

    /// <summary>
    /// Construct Marketwatch Phantom. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered with the
    /// <see cref="TriggerManager"/>. When <paramref name="effects"/> is
    /// supplied the "gains flying until end of turn" grant registers with the
    /// continuous-effects service (CR 514.2).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? effects = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // The card shape (2/2 Spirit Detective at {1}{W}, no printed Flying) is
        // materialised from the embedded JSON definition so the printed
        // characteristics live in one place; the trigger + grant are wired in
        // code because the entering-creature power filter and the "gains flying
        // until end of turn" SELF grant are not (yet) expressible in the
        // declarative effect union.
        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // "Whenever another creature you control with power 2 or less enters,
        //  this creature gains flying until end of turn." CR 603.6e / 109.5.
        // ----------------------------------------------------------------
        var grantEffect = new Effect(
            $"{CardName}: gains flying until end of turn",
            ctx =>
            {
                var subject = (ctx.Source as Creature) ?? card;
                var fx = effects ?? subject.ActiveEffects;
                // CR 514.2 — a Layer-6 grant that expires at cleanup. Without a
                // continuous-effects service (shape-only path) the keyword
                // cannot be added; same two-mode posture as Psychic Frog.
                fx?.Register(new GrantKeywordUntilEndOfTurnEffect(subject, GrantedKeyword));
                return ValueTask.CompletedTask;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (ReferenceEquals(e.Card, card)) return false; // CR 109.5 "another"
                if (e.Card is not Creature entering) return false;
                // CR 109.5 — "you control": resolved live so a control change
                // carries the trigger with the permanent.
                if (!ReferenceEquals(entering.Controller, card.Controller ?? owner)) return false;
                // CR 208.3 — "with power 2 or less": the entering creature's
                // current power.
                return entering.Power <= PowerThreshold;
            }),
            effects: new IEffect[] { grantEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
