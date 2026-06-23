using System;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using System.Linq;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Surrak, Elusive Hunter (Tarkir: Dragonstorm, {2}{G}).
///
/// Legendary Creature — Human Warrior 4/3. Oracle text (verified against
/// Scryfall 2026-06-23):
///   "This spell can't be countered.
///    Trample
///    Whenever a creature you control or a creature spell you control becomes
///    the target of a spell or ability an opponent controls, draw a card."
///
/// The base shape (name, Legendary Creature — Human Warrior, {2}{G}, 4/3) is
/// materialised from the embedded JSON definition
/// (<c>surrak-elusive-hunter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="SvyelunOfSeaAndSkyFactory"/> / <see cref="CounterfluxFactory"/>.
/// The three riders are layered on in C# (the JSON <c>AbilityDefinition</c>
/// schema does not express a cast-time uncounterable flag or a
/// becomes-the-target trigger).
///
/// ## Implemented (v1)
/// - <b>This spell can't be countered</b> (CR 701.5b) — an
///   "Uncounterable" <see cref="KeywordAbility"/> marker on the card shape that
///   <see cref="Majik.Core.Game.SpellCastFlow"/> reads at cast time to stamp
///   <see cref="ISpell.CannotBeCountered"/> on the cast Surrak spell, so a
///   rival counter calling
///   <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/> is
///   vetoed. Same posture as <see cref="CounterfluxFactory"/> /
///   <see cref="EmrakulTheAeonsTornFactory"/>.
/// - <b>Trample</b> (CR 702.19) — a plain <see cref="KeywordAbility"/> marker
///   read by combat math (same posture as every other Trample creature).
/// - <b>Becomes-the-target draw trigger</b> (CR 603.6c / 115.6) — "Whenever a
///   creature you control or a creature spell you control becomes the target of
///   a spell or ability an opponent controls, draw a card." Wired via
///   <see cref="TargetsChosenEvent"/>, the engine's existing "becomes the
///   target" seam (published by both
///   <see cref="Majik.Core.Services.SpellCaster"/> and
///   <see cref="Majik.Core.Services.AbilityActivator"/>, so "a spell or
///   ability" is covered uniformly — same attachment point as
///   <see cref="PawpatchRecruitFactory"/> / <see cref="NaduWingedWisdomFactory"/>).
///   Surrak's filters layered on the Pawpatch shape:
///   <list type="bullet">
///     <item><b>opponent-controlled source</b> (CR 109.5 / 102.1) — the stack
///     object's <see cref="Majik.Core.Stack.IStackObject.Controller"/> must NOT
///     be Surrak's controller.</item>
///     <item><b>"a creature you control OR a creature spell you control"</b> —
///     some chosen target is either (a) a creature permanent whose controller
///     is Surrak's controller, or (b) a SPELL on the stack whose card is a
///     creature and whose controller is Surrak's controller (CR 109.5).</item>
///   </list>
///   On resolution the controller draws one card (library-top move; empty
///   library flags the draw-from-empty SBA per CR 704.5b) — same draw shape as
///   <see cref="SvyelunOfSeaAndSkyFactory"/>'s attack trigger.
/// </summary>
[CardName("Surrak, Elusive Hunter")]
public static class SurrakElusiveHunterFactory
{
    public const string CardName = "Surrak, Elusive Hunter";
    public const string Slug = "surrak-elusive-hunter";

    /// <summary>
    /// Shape-only construction (no live trigger-manager wiring). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to: the
    /// becomes-the-target trigger is attached for inspection but does not fire
    /// on a live bus.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Surrak, Elusive Hunter.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the becomes-the-target draw
    /// trigger is registered with so it surfaces as pending in a live match.
    /// May be null.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Human Warrior, {2}{G}, 4/3). The JSON carries no abilities — all
        // three riders are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 701.5b — "This spell can't be countered". The Uncounterable
        // KeywordAbility marker is read at cast time by SpellCastFlow, which
        // stamps ISpell.CannotBeCountered on the resulting spell (same posture
        // as Counterflux / Emrakul, the Aeons Torn).
        card.AddAbility(new KeywordAbility("Uncounterable", card, owner));

        // CR 702.19 — Trample. Plain keyword marker; combat math reads it via
        // the keyword scan.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // CR 603.6c / 115.6 — "Whenever a creature you control or a creature
        // spell you control becomes the target of a spell or ability an
        // opponent controls, draw a card."
        var targeted = BuildBecomesTargetTrigger(card, owner);
        card.AddAbility(targeted);
        triggers?.RegisterTriggeredAbility(targeted);

        return card;
    }

    /// <summary>
    /// Build the becomes-the-target draw trigger (CR 603.6c / 115.6). Fires on
    /// a <see cref="TargetsChosenEvent"/> whose stack object is controlled by an
    /// OPPONENT of <paramref name="card"/>'s controller (CR 109.5 / 102.1) and
    /// whose chosen targets include either a creature permanent the controller
    /// controls OR a creature SPELL on the stack the controller controls. On
    /// resolution the controller draws one card (CR 120.2; empty library flags
    /// the draw-from-empty SBA per CR 704.5b).
    /// </summary>
    private static TriggeredAbility BuildBecomesTargetTrigger(Creature card, Player owner)
    {
        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;

            // CR 109.5 / 102.1 — "an opponent controls". The targeting
            // spell/ability's controller must NOT be Surrak's controller. Same
            // opponent test as PawpatchRecruit / WardEffect.Applies.
            var sourceController = e.StackObject.Controller;
            if (sourceController == null) return false;
            if (ReferenceEquals(sourceController, controller)) return false;

            // "a creature you control OR a creature spell you control becomes
            // the target" — some chosen target matches (CR 109.5, resolved
            // live).
            foreach (var t in e.Targets)
            {
                if (t is not Target concrete) continue;

                // (a) A creature permanent the controller controls.
                if ((concrete.TargetType == TargetType.Permanent
                        || concrete.TargetType == TargetType.Card)
                    && concrete.TargetObject is Creature targetCreature
                    && targetCreature.HasType(CardType.Creature)
                    && ReferenceEquals(targetCreature.Controller, controller))
                {
                    return true;
                }

                // (b) A creature SPELL on the stack the controller controls.
                if (concrete.TargetType == TargetType.Spell
                    && concrete.TargetObject is ISpell spell
                    && spell.Card.HasType(CardType.Creature)
                    && ReferenceEquals(SpellController(spell), controller))
                {
                    return true;
                }
            }

            return false;
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (becomes-the-target trigger, CR 603.6c / 120.2)",
            () =>
            {
                var controller = card.Controller ?? owner;
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 704.5b — drawing from an empty library is tracked via
                    // the SBA, resolved when the player next receives priority.
                    controller.MarkTriedToDrawFromEmptyLibrary();
                }
                else
                {
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    /// <summary>
    /// CR 109.5 — the controller of a spell on the stack. Prefers the
    /// <see cref="Spell"/>'s own <see cref="Spell.Controller"/>, falling back to
    /// the card's controller for hand-built test spells.
    /// </summary>
    private static Player? SpellController(ISpell spell)
    {
        if (spell is Majik.Core.Spells.Spell s) return s.Controller;
        return spell.Card.Controller;
    }
}
