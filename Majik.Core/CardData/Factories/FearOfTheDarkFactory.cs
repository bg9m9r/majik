using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fear of the Dark (Duskmourn, {4}{B}).
///
/// Enchantment Creature — Nightmare 5/5. Oracle text (verified against
/// Scryfall 2026-06-14):
///   "Whenever this creature attacks, if defending player controls no Glimmer
///    creatures, it gains menace and deathtouch until end of turn. (A creature
///    with menace can't be blocked except by two or more creatures.)"
///
/// The base shape (name, Creature + Enchantment types, Nightmare subtype,
/// {4}{B}, 5/5, black) is materialised from the embedded JSON definition
/// (<c>fear-of-the-dark.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional attack-trigger
/// keyword grant is layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't express an attack-triggered conditional keyword grant, so it lives
/// in the factory (same posture as <see cref="AdantoVanguardFactory"/>).
///
/// ## Implemented (v1)
/// - <b>{4}{B} Enchantment Creature — Nightmare 5/5, black</b> (CR 301.1 /
///   302.1 — dual Creature + Enchantment type), from the JSON def.
/// - <b>Conditional attack-trigger keyword grant (CR 508.1f + CR 603.4 +
///   CR 702.11 / CR 702.2)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnAttackSelf"/>. The trigger carries an
///   intervening-if (CR 603.4) — "if defending player controls no Glimmer
///   creatures" — re-checked both when it would trigger and on resolution.
///   The defending player travels on the <see cref="CreatureAttacksEvent"/>
///   (<see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/>); a
///   boxed cell captures the most-recent defender from the condition so the
///   parameterless intervening-if can read it. On resolution it registers two
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> grants — "Menace"
///   (CR 702.111) and "Deathtouch" (CR 702.2) — against the supplied
///   <see cref="ContinuousEffectsService"/>, expiring at cleanup (CR 514.2).
///
/// ## Deferred (v1 gaps)
/// - <b>No-service shape path</b>: the shape-only <see cref="Create(Player)"/>
///   overload attaches the trigger structurally but registers no continuous
///   effects (no layers service); the keyword grant is a no-op on that path.
///   Functional behaviour requires the wiring overload with a live
///   <see cref="ContinuousEffectsService"/> + <see cref="TriggerManager"/>.
/// </summary>
[CardName("Fear of the Dark")]
public static class FearOfTheDarkFactory
{
    public const string CardName = "Fear of the Dark";
    public const string Slug = "fear-of-the-dark";

    public const string GrantedMenace = "Menace";
    public const string GrantedDeathtouch = "Deathtouch";

    /// <summary>
    /// Construct Fear of the Dark with no live wiring. The attack trigger is
    /// attached structurally; without a <see cref="TriggerManager"/> the bus
    /// won't fire it and without a <see cref="ContinuousEffectsService"/> its
    /// resolution grants nothing. Suitable for shape / dispatcher tests. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Fear of the Dark.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the menace + deathtouch
    /// EOT grants are registered against on resolution. Pass null to skip the
    /// grant (the trigger is still attached structurally).</param>
    /// <param name="triggers">When supplied, the attack trigger is registered
    /// so a <see cref="CreatureAttacksEvent"/> for Fear of the Dark lands it on
    /// the stack automatically.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment, Nightmare subtype, {4}{B}, 5/5, black). The JSON carries
        // no abilities — the conditional attack grant is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 603.4 — the intervening-if reads "defending player controls no
        // Glimmer creatures." The defending player travels on the attack event,
        // not on the parameterless interveningIf delegate, so capture it in a
        // boxed cell from the condition and read it back in the intervening-if.
        var defender = new Player?[] { null };

        // CR 508.1f — "Whenever this creature attacks, …" per-attacker trigger.
        // Capture the defending player as a side effect of the match so the
        // intervening-if can consult it (the grant has no target).
        var attackCondition =
            new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Attacker, card)) return false;
                defender[0] = e.DefendingPlayerOrPlaneswalker as Player;
                return true;
            });

        var grantEffect = new Effect(
            $"{CardName}: gains menace and deathtouch until end of turn (CR 702.111 / 702.2)",
            () =>
            {
                if (continuousEffects == null) return;
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 702.111 — Menace (Layer 6 keyword grant, EOT, CR 514.2).
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, GrantedMenace));
                // CR 702.2 — Deathtouch.
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, GrantedDeathtouch));
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: attackCondition,
            effects: new IEffect[] { grantEffect },
            // CR 603.4 — "if defending player controls no Glimmer creatures"
            // (CR 702 intervening-if), checked both on trigger and on
            // resolution. A null defender (e.g. attacking a planeswalker) is
            // treated as "controls no Glimmer creatures" so the grant lands.
            interveningIf: () => DefendingPlayerControlsNoGlimmer(defender[0]),
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 702 intervening-if helper — true iff <paramref name="defender"/> is
    /// null (no defending player, e.g. a planeswalker defender) or controls no
    /// creature with the Glimmer subtype (CR 205.3) on the battlefield.
    /// Exposed for tests / bots without driving the full trigger flow.
    /// </summary>
    public static bool DefendingPlayerControlsNoGlimmer(Player? defender)
    {
        if (defender == null) return true;
        foreach (var c in defender.Zones.Battlefield.GetCards())
        {
            if (c is Creature creature && creature.HasSubtype(CardSubtype.Glimmer))
                return false;
        }
        return true;
    }
}
