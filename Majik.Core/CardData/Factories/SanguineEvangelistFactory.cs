using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanguine Evangelist (Outlaws of Thunder Junction,
/// {2}{W}). Creature — Vampire Cleric, 2/1. Oracle text (verified against
/// Scryfall):
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+0 until end of turn.)
///    When this creature enters or dies, create a 1/1 black Bat creature
///    token with flying."
///
/// ## Shape source
/// Card identity (name, {2}{W}, 2/1, Creature — Vampire Cleric) is loaded from
/// <c>Majik.Core/CardData/Cards/sanguine-evangelist.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The Battle cry keyword + pump trigger
/// and the enters-or-dies token rider are layered on in code below — the JSON
/// <c>AbilityDefinition</c> schema does not express attack triggers, ETB / dies
/// triggers, or token creation.
///
/// ## Implemented (v1)
/// - 2/1 white Vampire Cleric at {2}{W}, owner / controller wired (CR 105 —
///   white from the {W} pip, carried by the JSON shape). NOTE: the card is
///   white; the Bat token it makes is BLACK (CR 111.4 — token colour is
///   declared by the creating effect, not inherited from the source).
/// - <b>Battle cry (CR 702.92 / 702.92a)</b> — a <see cref="KeywordAbility"/>
///   "Battle cry" marker (the printed keyword line) plus an
///   <see cref="Triggers.OnAttackSelf"/> <see cref="TriggeredAbility"/> that,
///   on resolution, registers a <see cref="PumpUntilEndOfTurnEffect"/> of
///   +1/+0 (CR 514.2 cleanup expiry) on every OTHER attacking creature. The
///   "each other attacking creature" set is read from the supplied
///   <paramref name="attackingCreaturesSource"/> closure (same source-closure
///   shape as <see cref="HeroOfBladeholdFactory"/> /
///   <see cref="HonoredCropCaptainFactory"/> — the engine doesn't yet expose a
///   global "currently attacking creatures" view from inside an effect
///   closure). The pump is registered on each target's own
///   <see cref="Creature.ActiveEffects"/>; the Evangelist itself is skipped
///   ("each OTHER attacking creature", CR 702.92a).
/// - <b>"When this creature enters or dies, create a 1/1 black Bat creature
///   token with flying"</b> — wired as TWO <see cref="TriggeredAbility"/>s
///   sharing one effect body (same posture as
///   <see cref="MoggWarMarshalFactory"/>):
///   <list type="number">
///     <item>ETB trigger via <see cref="Triggers.OnEnterBattlefieldSelf"/>
///       (CR 603.6a), active in <see cref="ZoneType.Battlefield"/>.</item>
///     <item>Dies trigger via <see cref="Triggers.OnDies"/> (CR 603.6c /
///       CR 700.4), active in <see cref="ZoneType.Battlefield"/> +
///       <see cref="ZoneType.Graveyard"/> (Mogg War Marshal / Wurmcoil
///       posture so the zone-guard still matches after
///       <see cref="ZoneService"/> stamps the card's Zone = Graveyard before
///       publishing <see cref="CardMovedEvent"/>).</item>
///   </list>
///   The shared body creates one 1/1 BLACK Bat creature token with Flying
///   (CR 111.4) under the entering / dying card's controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. Token creation routes
///   through <paramref name="zoneService"/> when supplied so the new token's
///   ETB <see cref="CardMovedEvent"/> publishes for downstream ETB triggers.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Triggers attached for shape
///   observability; not registered with any <see cref="TriggerManager"/>;
///   battle-cry pump is a no-op (no attackers source). Suitable for dispatcher
///   / structural tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?, Func{IReadOnlyList{Creature}}?)"/>
///   — fully wired.
///
/// ## Deferred (v1 gaps)
/// - <b>Battle-cry attacker view</b>: the pump reads attackers from the
///   injected closure; when null it no-ops (same posture as
///   <see cref="HeroOfBladeholdFactory"/>).
/// - <b>Dies trigger control binding</b>: the dies-trigger creates the token
///   under the original <paramref name="owner"/>, not the
///   last-controller-before-death (same simplification as
///   <see cref="MoggWarMarshalFactory"/> / Wurmcoil Engine).
/// </summary>
[CardName("Sanguine Evangelist")]
public static class SanguineEvangelistFactory
{
    public const string CardName = "Sanguine Evangelist";
    public const string Slug = "sanguine-evangelist";

    /// <summary>+1/+0 to each other attacking creature (CR 702.92a).</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 0;

    /// <summary>1/1 black Bat token with flying (CR 111.4).</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Sanguine Evangelist with no live runtime wiring. The Battle
    /// cry marker is always attached; the attack trigger and both
    /// enters-or-dies triggers are attached to the card shape for
    /// observability but NOT registered with any <see cref="TriggerManager"/>;
    /// the battle-cry pump is a no-op (no attackers source) and token creation
    /// uses raw zone manipulation. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Sanguine Evangelist with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the battle-cry attack trigger and
    /// both enters-or-dies triggers register so the matching events queue them
    /// on the stack automatically (CR 603.3).</param>
    /// <param name="zoneService">When supplied, token creation routes through
    /// <see cref="ZoneService"/> so the Bat token's ETB
    /// <see cref="CardMovedEvent"/> publishes for downstream ETB triggers.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list, called at battle-cry resolution. May be null —
    /// the pump is then a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.92 — Battle cry keyword marker so ICard.Abilities reflects the
        // printed line and Scryfall keyword parsing matches. The functional
        // pump is the trigger below.
        card.AddAbility(new KeywordAbility("Battle cry", card, owner));

        // CR 702.92a — "Whenever this creature attacks, each other attacking
        // creature gets +1/+0 until end of turn."
        var battleCryEffect = new Effect(
            $"{CardName}: Battle cry — each other attacking creature +1/+0 EOT",
            () =>
            {
                if (attackingCreaturesSource == null) return;
                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    // "each OTHER attacking creature" (CR 702.92a) — skip self.
                    if (ReferenceEquals(atk, card)) continue;
                    // Each creature computes P/T from its own service; without
                    // one the grant silently no-ops (same posture as
                    // HonoredCropCaptainFactory's pump).
                    if (atk.ActiveEffects == null) continue;
                    atk.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(atk, PumpPower, PumpToughness));
                }
            });

        var battleCryTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { battleCryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(battleCryTrigger);
        triggers?.RegisterTriggeredAbility(battleCryTrigger);

        // ----------------------------------------------------------------
        // Shared "create a 1/1 black Bat creature token with flying" effect.
        // Used by both the ETB trigger and the dies trigger so the resolution
        // bodies are identical. CR 111.4 — token colour (BLACK) is declared by
        // the creating effect, not inherited from the white Evangelist.
        // ----------------------------------------------------------------
        IEffect MakeTokenEffect(string when) => new Effect(
            $"{CardName} ({when}): create 1/1 black Bat token with flying",
            () =>
            {
                var controller = card.Controller ?? owner;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Bat",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Bat },
                    Keywords: new[] { "Flying" },
                    Colors: new[] { ManaColor.Black });
                TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
            });

        // CR 603.6a — ETB trigger. "When this creature enters" matches a
        // CardMovedEvent for this card transitioning to Battlefield.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { MakeTokenEffect("ETB") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // CR 603.6c / CR 700.4 — dies trigger. ActiveZones =
        // Battlefield + Graveyard (Mogg War Marshal / Wurmcoil posture) so the
        // zone-guard still matches after ZoneService stamps Zone = Graveyard
        // before publishing the CardMovedEvent.
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { MakeTokenEffect("dies") },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
