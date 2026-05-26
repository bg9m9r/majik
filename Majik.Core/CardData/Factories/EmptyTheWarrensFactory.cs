using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Empty the Warrens (Time Spiral, {3}{R}).
///
/// Sorcery. Oracle text:
///   "Create two 1/1 red Goblin creature tokens.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn.)"
///
/// ## Implemented (v1)
/// - Sorcery {3}{R} (Red) card shape with owner / controller wired.
/// - <b>Two 1/1 red Goblin tokens</b> — <see cref="BuildResolveEffect"/>
///   creates exactly two 1/1 red Goblin creature tokens (CR 111.4) under
///   the caster via <see cref="TokenFactory.CreateOnBattlefield"/>. When a
///   live <see cref="ZoneService"/> is supplied the tokens publish
///   <see cref="Majik.Core.Events.CardMovedEvent"/> on entry so
///   downstream ETB triggers (Soul Warden, Goblin Chieftain pump-on-entry
///   readouts, lord-side effects) fire on token arrival.
/// - <b>Storm trigger (CR 702.40)</b> — built via
///   <see cref="StormHelper.Build"/>. Fires on this spell's
///   <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> with
///   <c>activeZones = Stack</c> and copies the spell for each OTHER spell
///   the controller has cast this turn. Storm count is read from
///   <see cref="TurnState.SpellsCastByPlayer"/> at trigger-evaluation
///   time; copies are re-executions of the original spell's effect list
///   via <see cref="Majik.Core.Services.SpellCopier"/>. The observable
///   contract: N copies → 2 + 2N goblin tokens total in play.
///
/// ## Deferred (v1 gaps)
/// - <b>Copies as distinct stack objects</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute the
///   original effect list in place rather than pushing real
///   <see cref="Majik.Core.Spells.ISpell"/> stack items. Acceptable for
///   the printed observable contract (token count); anything subscribing
///   to <see cref="Majik.Core.Domain.DomainEvents.StackObjectAddedEvent"/>
///   for the storm copies won't see them.
/// - <b>No targets</b>: Empty the Warrens has no printed targets; the
///   "new targets for copies" rider in StormHelper / SpellCopier is a
///   no-op here.
/// </summary>
[CardName("Empty the Warrens")]
public static class EmptyTheWarrensFactory
{
    public const string CardName = "Empty the Warrens";
    public const string PrintedManaCost = "{3}{R}";
    public const int GoblinTokenCount = 2;
    public const int GoblinPower = 1;
    public const int GoblinToughness = 1;

    /// <summary>
    /// Construct Empty the Warrens as a Sorcery card with no Storm trigger
    /// registered. Suitable for shape / dispatcher tests. Use the
    /// <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState)"/>
    /// overload for fully-wired storm firing.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Attach the storm trigger structurally (no stack / turn-state
        // wired — shape-only). Same shape as Brain Freeze / Tendrils of
        // Agony.
        var storm = StormHelper.Build(card, owner, stack: null, turnState: null);
        card.AddAbility(storm);

        return card;
    }

    /// <summary>
    /// Construct Empty the Warrens with full storm wiring. The storm
    /// trigger is registered with <paramref name="triggers"/>, reads
    /// spells-cast counts from <paramref name="turnState"/> at trigger-
    /// evaluation time, and creates copies on <paramref name="stack"/>
    /// via <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpell"/>.
    /// </summary>
    public static Sorcery Create(
        Player owner,
        TriggerManager triggers,
        Majik.Core.Stack.Stack stack,
        TurnState turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(turnState);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        var storm = StormHelper.Build(card, owner, stack, turnState);
        card.AddAbility(storm);
        triggers.RegisterTriggeredAbility(storm);

        return card;
    }

    /// <summary>
    /// Build the "create two 1/1 red Goblin creature tokens" resolve
    /// effect. Threading a live <see cref="ZoneService"/> publishes
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> on token entry so
    /// ETB triggers (Soul Warden / Impact Tremors / lord-pump readouts)
    /// see the arrival.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller, ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return new IEffect[]
        {
            new Effect(
                $"{CardName} — create {GoblinTokenCount} 1/1 red Goblin creature tokens",
                () =>
                {
                    for (var i = 0; i < GoblinTokenCount; i++)
                    {
                        CreateGoblinToken(controller, zoneService);
                    }
                }),
        };
    }

    /// <summary>
    /// Build the no-target Empty the Warrens
    /// <see cref="SpellDefinition"/>. <see cref="SpellCastFlow"/> hands the
    /// <see cref="ChosenSpellParams"/> back to <see cref="EffectFactory"/>;
    /// we ignore the (empty) target slot and delegate to
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player controller, ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(controller, zoneService));
    }

    /// <summary>
    /// Create a single 1/1 red Goblin creature token under
    /// <paramref name="controller"/>. CR 105 / CR 111.4 — red is stamped
    /// via <see cref="TokenFactory.TokenSpec.Colors"/>.
    /// </summary>
    private static Creature CreateGoblinToken(Player controller, ZoneService? zoneService)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: GoblinPower,
            Toughness: GoblinToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            // CR 105 / CR 111.4 — "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
