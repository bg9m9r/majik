using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crashing Footfalls (Modern Horizons, {1}{R}{G}{W}).
///
/// Sorcery. Oracle text:
///   "Cascade
///    Create two 4/4 green Rhino warrior creature tokens with trample."
///
/// ## Implemented (v1)
/// - Sorcery shell at printed cost {1}{R}{G}{W} (mana value 4 — by design
///   high enough that no Modern-legal cascade source can target it via
///   cascade-into-Footfalls, BUT low enough that Shardless Agent / Violent
///   Outburst at MV 3 cascade INTO Crashing Footfalls).
/// - Cascade trigger (CR 702.85): a <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> for this card. On resolution it invokes
///   <see cref="CascadeAction.Cascade"/> with sourceManaValue = 4. Optional
///   <c>willCast</c> predicate is forwarded — production callers pass an
///   agent-driven decision; tests default to always-cast. The actual
///   alternative-cost cast (CR 702.85a — "without paying its mana cost") is
///   driven by the caller via <see cref="Costs.CastFromExileAlternativeCost"/>
///   on the <see cref="CascadeAction.CascadeResult.Eligible"/> card; this
///   factory only fires the trigger.
/// - Resolve effect: <see cref="BuildSpellDefinition"/> creates two 4/4
///   green Rhino Warrior creature tokens with Trample on the battlefield
///   under the caster's control (CR 111.6).
///
/// ## Deferred (v1 gaps)
/// - <b>"Green" colour on tokens</b>: tokens have no <see cref="ICard.ManaCost"/>
///   so colour is identity-only via subtype/ability text today — Majik
///   doesn't yet model token characteristic colour. The Trample +
///   creature-type assignments match the printed text; the green colour
///   identity is a downstream concern (same gap as Wurmcoil's "colorless"
///   tokens, Solitude's "white" creatures, etc.).
/// - <b>Cascade-into-cascade</b>: if cascade resolves into another cascade
///   spell (e.g. Crashing Footfalls hits Shardless Agent), the secondary
///   cascade fires when the secondary spell is cast — which is wired
///   automatically as long as that spell's factory registers its own
///   SpellCastEvent trigger.
/// </summary>
public static class CrashingFootfallsFactory
{
    public const string CardName = "Crashing Footfalls";
    public const string PrintedManaCost = "{1}{R}{G}{W}";
    public const int CascadeSourceManaValue = 4;
    public const int TokenCount = 2;
    public const int TokenPower = 4;
    public const int TokenToughness = 4;

    /// <summary>
    /// Construct Crashing Footfalls with no runtime services. The cascade
    /// trigger is attached to the card's ability list for shape inspection
    /// but is not registered with a TriggerManager. Suitable for
    /// dispatcher / shape-only tests.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Construct Crashing Footfalls with optional trigger-manager wiring
    /// and "you may cast" decision predicate.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">When supplied, the cascade trigger is
    /// registered so a <see cref="SpellCastEvent"/> for this card lands
    /// on the stack automatically.</param>
    /// <param name="willCast">Forwarded to <see cref="CascadeAction.Cascade"/>
    /// — the controller's "you may" decision for the eligible card.
    /// Default = always cast.</param>
    /// <param name="onCascadeResolved">Optional callback invoked with the
    /// <see cref="CascadeAction.CascadeResult"/> when the cascade trigger
    /// resolves. Production callers use this to drive the free-cast of
    /// <see cref="CascadeAction.CascadeResult.Eligible"/> via
    /// <see cref="Costs.CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>
    /// (CR 702.85a). Tests use it to observe trigger firing.</param>
    public static Sorcery Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.85 — Cascade. "When you cast this spell, exile cards from
        // the top of your library until you exile a nonland card whose
        // mana value is less than this spell's mana value …"
        var cascadeCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        // Trigger resolves into a CascadeAction call. Caller-supplied
        // onCascadeResolved (if any) is invoked with the result so the
        // host can drive the optional free-cast through SpellCastFlow.
        var cascadeEffect = new Effect(
            "Crashing Footfalls — Cascade (CR 702.85)",
            () =>
            {
                var result = CascadeAction.Cascade(
                    controller: owner,
                    sourceManaValue: CascadeSourceManaValue,
                    willCast: willCast);
                onCascadeResolved?.Invoke(result);
            });

        var cascadeTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cascadeCondition,
            effects: new IEffect[] { cascadeEffect },
            // Cascade fires while the spell is on the stack (the cast
            // event is published as the spell moves to the stack), so the
            // ability needs to be active in the Stack zone.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Crashing Footfalls uses when
    /// cast — no targets, no modes; on resolution, create two 4/4 green
    /// Rhino Warrior creature tokens with Trample under the caster's
    /// control.
    /// </summary>
    /// <param name="caster">The player casting Crashing Footfalls — token
    /// controller per CR 111.6.</param>
    /// <param name="zoneService">Optional zone service so token-ETB
    /// CardMovedEvent fires (Soul Warden etc.). Pass <c>null</c> for raw
    /// zone moves.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return SpellDefinition.Vanilla(_ => new IEffect[]
        {
            new Effect(
                $"Crashing Footfalls: create {TokenCount} {TokenPower}/{TokenToughness} green Rhino Warrior tokens with trample",
                () => CreateRhinoTokens(caster, zoneService)),
        });
    }

    /// <summary>
    /// Create the two 4/4 green Rhino Warrior creature tokens with Trample
    /// under <paramref name="controller"/>. CR 111 / CR 702.19 (Trample).
    /// </summary>
    public static IReadOnlyList<Creature> CreateRhinoTokens(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Rhino",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Rhino, CardSubtype.Warrior },
            Keywords: new[] { "Trample" });

        var result = new List<Creature>(TokenCount);
        for (int i = 0; i < TokenCount; i++)
        {
            result.Add(TokenFactory.CreateOnBattlefield(spec, controller, zoneService));
        }
        return result;
    }

}
