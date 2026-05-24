using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spell Queller (Eldritch Moon, {1}{W}{U}).
///
/// Creature — Spirit 2/3. Oracle text:
///   "Flash
///    When Spell Queller enters, exile target spell with mana value 4 or less.
///    When Spell Queller leaves the battlefield, the exiled card's owner may
///    cast it without paying its mana cost."
///
/// ## Implemented (v1)
/// - 2/3 Spirit creature at {1}{W}{U} with Flash (CR 702.8).
/// - <b>ETB triggered ability</b> (CR 603.6a) — declares a 1..1
///   <see cref="TargetRequest"/> for "target spell with mana value 4 or less".
///   On resolution, the chosen target (an <see cref="ISpell"/> on the stack
///   selected by the caller via <see cref="TriggeredAbility.SetChosenTargets"/>)
///   is validated: still on the stack, mana value ≤ 4 (CR 202.3b — includes
///   the chosen X via <see cref="Card.PendingCastX"/>, matching Chalice of the
///   Void / Spell Snare). If legal, the spell is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and the underlying card
///   is moved to its owner's <see cref="ZoneType.Exile"/> zone. The exiled
///   card is captured on a per-Queller closure for the LTB ability.
/// - <b>LTB triggered ability</b> (CR 603.6c) — fires when Spell Queller
///   moves OUT of <see cref="ZoneType.Battlefield"/> (any destination, not
///   just graveyard — matches "leaves the battlefield" wording, CR 603.10c).
///   On resolution, if an exiled card was captured by the ETB AND that card
///   is still in exile (CR 400.7 — object identity changes on zone change,
///   but our exile placement leaves the same Card instance there), the
///   factory invokes an optional <c>onExiledCardReleased</c> callback so the
///   host can drive the free cast via <see cref="CastFromExileAlternativeCost"/>
///   + <see cref="Majik.Core.Game.SpellCastFlow"/> (mirrors
///   <see cref="CrashingFootfallsFactory"/>'s <c>onCascadeResolved</c>
///   pattern). The "may" decision is delegated to the caller — production
///   code routes through the original owner's <see cref="Players.Agents.IPlayerAgent"/>;
///   tests assert castability directly by building
///   <see cref="CastFromExileAlternativeCost"/> from the exiled card.
///
/// ## Why the LTB exposes a callback rather than auto-casting
/// The cast is at the EXILED card's owner's discretion — not necessarily
/// Spell Queller's controller (a stolen Queller still hands the cast back
/// to the original owner). Mid-resolution synchronous cast-through is also
/// awkward because <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> is
/// async and pulls from an agent. The callback shape lets the factory stay
/// sync inside the trigger effect and lets the host decide whether to
/// surface the cast through the real cast pipeline or, in test code, drive
/// it inline.
///
/// ## Mana value comparison (CR 202.3b)
/// Mana value of the targeted spell is sampled at resolution time as
/// <c>printed mana value + PendingCastX</c> — same shape as Chalice of the
/// Void / Spell Snare. A spell whose mv exceeds 4 at resolution becomes an
/// illegal target (CR 608.2b); the effect does nothing and the spell
/// remains on the stack.
///
/// ## Target-selection prompt
/// Spell Queller's ETB target is filled by the agent-prompt pipeline via the
/// <see cref="TargetRequest.CandidateGatherer"/> that enumerates the live
/// stack at trigger-resolve time and filters to spells with mana value ≤ 4.
/// <see cref="HeuristicBotAgent"/>'s Counter intent ranks the most-expensive
/// eligible spell first; the resolve effect tolerates missing / illegal
/// targets per CR 603.10b.
/// </summary>
[CardName("Spell Queller")]
public static class SpellQuellerFactory
{
    public const string CardName = "Spell Queller";
    public const string PrintedManaCost = "{1}{W}{U}";
    public const int Power = 2;
    public const int Toughness = 3;
    public const int MaxTargetManaValue = 4;

    /// <summary>
    /// Construct Spell Queller with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered
    /// with a <see cref="TriggerManager"/>, and the ETB exile path uses
    /// raw zone manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null, onExiledCardReleased: null);

    /// <summary>
    /// Construct Spell Queller with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="stack">When supplied, the ETB effect removes the
    /// targeted spell from the stack via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/>.</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so their respective events land them on the stack
    /// automatically.</param>
    /// <param name="onExiledCardReleased">Optional callback fired during
    /// the LTB resolution with the exiled card. Production callers use
    /// this to drive the original owner's free-cast through
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> +
    /// <see cref="CastFromExileAlternativeCost"/> (CR 702.85a — same
    /// pattern as Cascade). Tests use it to observe trigger firing.
    /// Null = no host wiring (the exiled card simply remains in exile).</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers,
        Action<ICard>? onExiledCardReleased)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // Captured-exile slot shared between the ETB effect (writer) and
        // the LTB effect (reader). Null until the ETB resolves with a
        // legal target.
        ICard? exiled = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21 (Exile).
        //   "When Spell Queller enters, exile target spell with mana value
        //    4 or less."
        // Target is supplied via TriggeredAbility.SetChosenTargets — same
        // pattern as Snapcaster Mage. The "pick a spell from the stack"
        // prompt is deferred to the agent MVP.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            "Spell Queller — exile target spell with mana value 4 or less (CR 701.21)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not ISpell spell) return;

                // CR 608.2b — illegal-on-resolution check. The target must
                // still be on the stack at resolution time.
                var targetCard = spell.Card as Card;
                if (targetCard == null) return;
                if (targetCard.Zone != ZoneType.Stack) return;

                // CR 202.3b — mana value = printed + chosen X. Spell Snare
                // / Chalice of the Void use the same shape.
                var printed = targetCard.ManaCostValue.TotalValue;
                var x = targetCard.PendingCastX ?? 0;
                var manaValue = printed + x;
                if (manaValue > MaxTargetManaValue) return;

                // CR 701.21 — exile is a zone change. Remove from the
                // stack first (if a Stack handle is supplied) so the
                // resolver no longer sees the targeted spell, then place
                // the underlying card into its owner's exile zone.
                if (stack != null)
                {
                    OracleSpellBinder.RemoveFromStack(stack, spell);
                }

                var targetOwner = targetCard.Owner;
                if (targetOwner != null && targetCard.Zone != ZoneType.Exile)
                {
                    targetOwner.Zones.Exile.AddCard(targetCard);
                }
                targetCard.SetZone(ZoneType.Exile);

                exiled = targetCard;
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell with mana value 4 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt MVP: enumerate stack spells whose mana
                    // value is ≤ 4 (CR 601.2c — choose-time legality).
                    // Counter intent in the bot's ranker picks the most-
                    // expensive eligible spell (ISpell.Card mana value).
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<Majik.Core.Spells.ISpell>()
                        .Where(s => Majik.Core.ValueObjects.ManaCost
                            .Parse(s.Card?.ManaCost ?? "").TotalValue <= 4)
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When Spell Queller leaves the battlefield, the exiled card's
        //    owner may cast it without paying its mana cost."
        // Matches "leaves the battlefield" — any FromZone == Battlefield
        // movement, not just dies-to-graveyard (CR 603.10c). The "may"
        // decision + the actual free cast is delegated to the host via
        // onExiledCardReleased; this factory just signals "the exiled card
        // is now released" and lets the caller pump it through SpellCastFlow
        // with a CastFromExileAlternativeCost (mirrors Cascade — CR 702.85a).
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            "Spell Queller — exiled card's owner may cast it without paying its mana cost (CR 702.85a)",
            () =>
            {
                if (exiled == null) return;
                // CR 400.7 — if the card has moved out of exile since
                // (extraction effects etc.), the release no-ops.
                if (exiled.Zone != ZoneType.Exile) return;
                onExiledCardReleased?.Invoke(exiled);
            });

        var ltb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed
            // on the battlefield. ActiveZones = Battlefield matches the
            // "looks back" semantics other LTB triggers (Wurmcoil Engine
            // dies trigger) already rely on.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltb);
        triggers?.RegisterTriggeredAbility(ltb);

        return card;
    }

    /// <summary>
    /// Build a <see cref="CastFromExileAlternativeCost"/> the exiled card's
    /// owner can use to cast the released card for free (CR 702.85a). The
    /// returned cost gates on Zone == Exile + Owner == caster — same shape
    /// as the cascade free-cast path.
    /// </summary>
    public static CastFromExileAlternativeCost BuildFreeCastCost() =>
        new(
            description: "Spell Queller — cast the exiled card without paying its mana cost",
            cost: ManaCost.Parse(string.Empty));
}
