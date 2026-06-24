using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Resonance (Duskmourn: House of Horror,
/// {2}{R}{R}). Enchantment. Oracle text (verified against Scryfall + the
/// embedded seed):
///   "If this card is in your opening hand, you may begin the game with it on
///    the battlefield.
///    Whenever you cast an instant or sorcery spell that targets only a single
///    creature you control, copy that spell. You may choose new targets for the
///    copy."
///
/// The base shape (name / Enchantment / {2}{R}{R}) is materialised from the
/// embedded JSON definition (<c>leyline-of-resonance.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (opening-hand Leyline alt-cost marker + the cast-copy trigger) are layered
/// on top here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// keyword markers or this gated copy trigger, so they live in the factory
/// (same posture as <see cref="StormscaleScionFactory"/> and the other
/// Leyline factories <see cref="LeylineOfCombustionFactory"/> /
/// <see cref="LeylineOfLightningFactory"/>).
///
/// ## Implemented
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>) so the
///   shared <see cref="Majik.Core.Events.OpeningHandCheckEvent"/> subscriber
///   picks Resonance up. Same wiring as the other Leylines.
/// - <b>"Whenever you cast an instant or sorcery spell that targets only a
///   single creature you control, copy that spell. You may choose new targets
///   for the copy."</b> (CR 603.1 / 603.3 / 707.10). An on-cast
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> gated to:
///     (a) the spell is controlled by Resonance's controller (CR 109.5 — "you
///         cast");
///     (b) the spell card is an <see cref="CardType.Instant"/> or
///         <see cref="CardType.Sorcery"/>;
///     (c) the spell targets exactly one object, and that object is a
///         <see cref="Creature"/> the controller controls (CR 115 — "targets
///         only a single creature you control"). A multi-target spell, a spell
///         that targets a non-creature, or one targeting a creature an opponent
///         controls does NOT qualify.
///   On resolution <see cref="SpellCopier.PushCopyOfTopSpellAsync"/> pushes a
///   distinct copy of the captured spell onto the stack (CR 706.10a / 707.10),
///   honouring "you may choose new targets for the copy" (CR 707.10a) via the
///   copier's per-slot retarget prompt when a live agent + game context are
///   available; with no live decision surface the copy keeps the original's
///   target verbatim. Same on-cast attachment point as
///   <see cref="LeylineOfLightningFactory"/>; same copy primitive as
///   <see cref="GalvanicIterationFactory"/>.
/// </summary>
[CardName("Leyline of Resonance")]
public static class LeylineOfResonanceFactory
{
    public const string CardName = "Leyline of Resonance";
    public const string Slug = "leyline-of-resonance";

    /// <summary>
    /// Constructs Leyline of Resonance with no live runtime wiring (the
    /// shape / dispatcher path — the overload <see cref="NamedCardFactory"/>
    /// dispatches to). The cast-copy trigger rides the card shape so the live
    /// <see cref="TriggerManager"/> auto-binds it on the first zone crossing
    /// (battlefield entry — CR 603.6a / <see cref="CardMovedEvent"/> →
    /// SyncCardRegistration); the copy itself reads the stack + agent from the
    /// resolver-supplied <see cref="Abilities.ResolutionContext"/>.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs Leyline of Resonance. When <paramref name="triggers"/> is
    /// supplied the on-cast copy trigger is registered so a qualifying
    /// controller-cast <see cref="SpellCastEvent"/> surfaces it as pending.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {2}{R}{R}). The JSON carries no abilities — both printed behaviours
        // are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner);

        // CR 702.95 — Leyline keyword marker.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        // ----------------------------------------------------------------
        // "Whenever you cast an instant or sorcery spell that targets only a
        //  single creature you control, copy that spell. You may choose new
        //  targets for the copy." (CR 603.1 / 603.3 / 707.10).
        //
        // The triggering spell is captured by the condition (runs at event-
        // publish time, before the queued effect resolves — same plumbing as
        // GalvanicIterationFactory), then copied at resolution.
        // ----------------------------------------------------------------
        ISpell? captured = null;

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;
            var spell = e.Spell;

            // (a) CR 109.5 — "you cast".
            if (!ReferenceEquals(spell.Controller, controller)) return false;

            // (b) instant or sorcery.
            var spellCard = spell.Card;
            if (spellCard is null) return false;
            if (!spellCard.HasType(CardType.Instant)
                && !spellCard.HasType(CardType.Sorcery)) return false;

            // (c) "targets only a single creature you control" — exactly one
            // target, a Permanent that is a Creature controlled by you.
            if (!TargetsOnlyASingleCreatureYouControl(spell, controller)) return false;

            captured = spell;
            return true;
        });

        var copyEffect = Fx.Inline(
            $"{CardName}: copy that spell (you may choose new targets for the copy)",
            async rc =>
            {
                var toCopy = captured;
                captured = null;
                if (toCopy is null) return;

                var stack = rc.Game?.Stack;
                if (stack is null) return;

                // CR 706.10a / 707.10 — push a distinct copy above the original.
                // CR 707.10a — "you may choose new targets for the copy": the
                // copier prompts the copy's controller per retained target slot
                // when a live agent + game context are supplied, else keeps the
                // original target verbatim. Copy controller = Resonance's
                // controller (CR 707.10).
                await SpellCopier.PushCopyOfTopSpellAsync(
                    stack,
                    toCopy,
                    rc.Agent,
                    rc.Game,
                    copyController: card.Controller ?? owner,
                    ct: rc.Ct).ConfigureAwait(false);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { copyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 115 / 109.5 — does <paramref name="spell"/> target exactly one
    /// object, and is that object a <see cref="Creature"/> controlled by
    /// <paramref name="controller"/>? A spell with zero or multiple targets, or
    /// one whose sole target is a non-creature / a creature an opponent
    /// controls, does NOT qualify ("targets ONLY a single creature you
    /// control").
    /// </summary>
    private static bool TargetsOnlyASingleCreatureYouControl(ISpell spell, Player controller)
    {
        var targets = spell.Targets;
        if (targets.Count != 1) return false;

        if (targets[0] is not Target concrete) return false;
        if (concrete.TargetType != TargetType.Permanent) return false;

        return concrete.TargetObject is Creature creature
               && ReferenceEquals(creature.Controller, controller);
    }
}
