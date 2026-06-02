using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Counterbalance (Coldsnap, {U}{U}).
///
/// Enchantment. Oracle text:
///   "Whenever an opponent casts a spell, you may reveal the top card of
///    your library. If you do, counter that spell if it has the same mana
///    value as the revealed card."
///
/// ## Shape source
/// Card identity (name, {U}{U}, Enchantment) is loaded from
/// <c>Majik.Core/CardData/Cards/counterbalance.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The triggered ability is wired in code.
///
/// ## Implemented (v1)
/// - <b>Enchantment {U}{U}</b> with owner/controller wired.
/// - <b>Cast-trigger reveal-and-counter</b> (CR 603.2, CR 701.5):
///   on every <see cref="SpellCastEvent"/> whose controller is an
///   <i>opponent</i> of Counterbalance's controller (CR 109.5 — "an
///   opponent casts a spell"; asymmetric, unlike the symmetric Chalice of
///   the Void), the trigger snapshots that spell into a per-card queue.
///   On resolution the effect reveals the top card of the controller's
///   library (CR 701.16 reveal — the card is only looked at, it stays on
///   top of the library) and, if the revealed card's mana value
///   (CR 202.3) equals the cast spell's mana value, counters that spell
///   (CR 701.5): removes it from the stack and puts it into its owner's
///   graveyard via <see cref="OracleSpellBinder.RemoveFromStack"/>.
///
/// ## "you may reveal" (CR 116.2b)
/// The printed clause is optional ("you <i>may</i> reveal"). In v1 the
/// reveal is taken automatically whenever the controller has a top card —
/// same deterministic posture other reveal-driven counter/look effects in
/// the engine adopt (no live agent yes/no surface is threaded into the
/// trigger-resolution closure yet). Declining the reveal can never improve
/// Counterbalance's outcome (a decline simply forgoes a possible counter),
/// so auto-revealing is strictly the maximal-value line and matches every
/// tournament play pattern for the card.
///
/// ## Mana-value comparison (CR 202.3 / CR 202.3b)
/// The cast spell's mana value is read from its card's
/// <see cref="Card.ManaCostValue"/> total plus any chosen X stamped as
/// <see cref="Card.PendingCastX"/> (set by SpellCastFlow at cast time and
/// still live while the spell is on the stack), matching how Chalice of
/// the Void computes the value. The revealed library card's mana value is
/// its printed <see cref="Card.ManaCostValue"/> total (a face-down /
/// library card has no chosen X).
/// </summary>
[CardName("Counterbalance")]
public static class CounterbalanceFactory
{
    public const string CardName = "Counterbalance";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("counterbalance");

    /// <summary>
    /// Construct Counterbalance with no live runtime wiring. The triggered
    /// ability is attached to the card shape; it is not registered with a
    /// <see cref="TriggerManager"/> and the counter effect falls back to a
    /// direct graveyard placement (no <see cref="Majik.Core.Stack.Stack"/>
    /// handle). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, stack: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Counterbalance with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the cast-trigger is
    /// registered; when <paramref name="stack"/> is supplied the counter
    /// effect routes through <see cref="OracleSpellBinder.RemoveFromStack"/>
    /// so the resolver no longer sees the countered spell.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cast-trigger — CR 603.2 / CR 701.5.
        //   "Whenever an opponent casts a spell, you may reveal the top
        //    card of your library. If you do, counter that spell if it has
        //    the same mana value as the revealed card."
        //
        // Asymmetric (CR 109.5): fires only when the spell's controller is
        // an opponent of Counterbalance's controller — NOT on the
        // controller's own casts (contrast Chalice of the Void, which is
        // symmetric). The condition predicate snapshots each opponent spell
        // into a per-card queue so the effect (which runs later, when the
        // trigger resolves) knows which spell to test + counter. The reveal
        // + MV comparison happens at resolution, per the printed "If you
        // do, counter that spell if …" wording.
        // ----------------------------------------------------------------
        var pendingSpells = new Queue<ISpell>();

        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // "an opponent casts a spell" — CR 109.5: the spell's
            // controller must not be Counterbalance's controller.
            var caster = e.Spell.Controller;
            if (caster == null || ReferenceEquals(caster, card.Controller ?? owner))
            {
                return false;
            }

            pendingSpells.Enqueue(e.Spell);
            return true;
        });

        var revealAndCounterEffect = new Effect(
            "Counterbalance — reveal top card; counter the spell on MV match (CR 701.5)",
            () =>
            {
                if (pendingSpells.Count == 0) return;
                var spell = pendingSpells.Dequeue();

                // CR 116.2b — "you may reveal": v1 auto-reveals (see class
                // xmldoc). CR 701.16 — revealing only looks at the top card;
                // it stays on top of the library.
                var controller = card.Controller ?? owner;
                var revealed = controller.Zones.Library.GetCards().FirstOrDefault();
                if (revealed is not Card revealedCard)
                {
                    // Empty library → nothing revealed → nothing countered.
                    return;
                }

                // CR 202.3 — mana value comparison.
                int revealedMv = revealedCard.ManaCostValue.TotalValue;
                int spellMv = ManaValueOf(spell.Card);
                if (revealedMv != spellMv)
                {
                    return;
                }

                // CR 701.5 — counter: remove from the stack, then put the
                // card into its owner's graveyard. CR 608.2b: if the spell
                // already left the stack the walk is a no-op and we still
                // ensure the card lands in its owner's graveyard.
                if (stack != null)
                {
                    OracleSpellBinder.RemoveFromStack(stack, spell);
                }
                if (spell.Card.Owner != null
                    && spell.Card.Zone != ZoneType.Graveyard)
                {
                    spell.Card.Owner.Zones.Graveyard.AddCard(spell.Card);
                }
                spell.Card.SetZone(ZoneType.Graveyard);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { revealAndCounterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// CR 202.3 / CR 202.3b — mana value of the spell as it sits on the
    /// stack: printed mana value plus any chosen X stamped by SpellCastFlow
    /// (<see cref="Card.PendingCastX"/>, still live while on the stack).
    /// </summary>
    private static int ManaValueOf(ICard card)
    {
        int printed = card is Card concrete
            ? concrete.ManaCostValue.TotalValue
            : Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost).TotalValue;
        int x = (card as Card)?.PendingCastX ?? 0;
        return printed + x;
    }
}
