using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ruination Guide (Battle for Zendikar, {2}{U}).
///
/// Creature — Eldrazi Drone 3/2 (colorless — Devoid). Oracle text (verified
/// against Scryfall 2026-06-02):
///   "Devoid (This card has no color.)
///    Ingest (Whenever this creature deals combat damage to a player, that
///    player exiles the top card of their library.)
///    Other colorless creatures you control get +1/+0."
///
/// The card's base shape (name, Creature, Eldrazi + Drone subtypes, {2}{U},
/// 3/2) is materialised from the embedded JSON definition
/// (<c>ruination-guide.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Devoid, the Ingest combat
/// trigger, and the colorless anthem are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express Devoid, combat-damage
/// triggers, or continuous static effects (same posture as
/// <see cref="NettleDroneFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Devoid (CR 702.114)</b> — stamped via <see cref="Card.SetDevoid"/> so
///   <see cref="CardColors.GetColors"/> returns empty regardless of the {U}
///   pip, plus a <see cref="KeywordAbility"/> marker for ability-scan
///   discoverability. Same shape as <see cref="NettleDroneFactory"/>.
/// - <b>Ingest (CR 701.34 / CR 510 / CR 603.1)</b> — "Whenever this creature
///   deals combat damage to a player, that player exiles the top card of
///   their library." Wired as a <see cref="TriggeredAbility"/> over
///   <see cref="CombatDamageDealtEvent"/> filtered to this card's instance
///   AND a non-null <see cref="DamageDealtEvent.TargetPlayer"/> (combat damage
///   to a player, not to a creature/planeswalker). On resolution the damaged
///   player exiles the top card of their library. The damaged player is
///   captured off the event in the trigger predicate (CR 603.3 evaluates the
///   condition before the ability hits the stack), then read in the effect —
///   same capture-closure shape as <see cref="RagavanNimblePilfererFactory"/>
///   (minus the Treasure / may-cast-from-exile riders; plain Ingest is just
///   the exile). Empty library = no-op (CR 120.3 — the empty-library
///   state-loss is handled by SBAs, not this effect; you don't lose for
///   failing to exile from an empty library, only for drawing from one).
/// - <b>Colorless anthem (+1/+0)</b> — "Other colorless creatures you control
///   get +1/+0." Registered as a <see cref="ColorlessCreatureAnthemEffect"/>
///   static at Layer 7c (CR 613.7c), scoped to the source's controller and
///   gated on the creature being colorless
///   (<see cref="CardColors.GetColors"/> returns an empty set — CR 105.2c
///   "a colorless object has no color"). Mirrors
///   <see cref="HonorOfThePureFactory"/>'s colour-gated anthem, but the gate
///   is the absence of colour rather than the presence of one. "OTHER"
///   colorless creatures — Ruination Guide is itself Devoid (colorless), so
///   <c>includeSelf: false</c> excludes it from its own buff (CR 613.7c).
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Ruination
///   Guide isn't on the battlefield so the bonus lifts on LTB (CR 614).
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches Devoid + the Ingest
/// trigger structurally (correct card shape for factory-shape / dispatch
/// tests). The trigger is NOT registered with a <see cref="TriggerManager"/>
/// and the anthem is NOT registered against any
/// <see cref="ContinuousEffectsService"/>, so no creatures receive +1/+0.
/// Production callers use the full overload.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered anthem stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source
///   isn't on the battlefield, but a future Prune pass could drop the entry.
///   Same shape as Honor of the Pure / Heartless Summoning.
/// - <b>Control-change re-evaluation</b>: the anthem captures its controller
///   at register time (via <see cref="Permanent.Controller"/> on the source).
///   Mind Control on Ruination Guide won't currently flip the affected side.
///   Same caveat as Honor of the Pure.
/// - <b>Ingest keyword as a first-class engine keyword</b>: Ingest is modelled
///   here as the plain combat-damage trigger its reminder text spells out,
///   not as a reusable <c>Ingest</c> keyword primitive — the engine has no
///   Ingest registry. The <see cref="KeywordAbility"/> marker is still
///   attached for ability-scan discoverability.
/// </summary>
[CardName("Ruination Guide")]
public static class RuinationGuideFactory
{
    public const string CardName = "Ruination Guide";
    public const string Slug = "ruination-guide";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>+1/+0 to each OTHER colorless creature the controller controls.</summary>
    public const int AnthemPower = 1;
    public const int AnthemToughness = 0;

    /// <summary>CR 702.114 — Devoid keyword marker string.</summary>
    public const string DevoidKeyword = "Devoid";

    /// <summary>CR 701.34 — Ingest keyword marker string.</summary>
    public const string IngestKeyword = "Ingest";

    /// <summary>
    /// Construct Ruination Guide with no live wiring. Devoid + the Ingest
    /// combat-damage trigger are attached structurally; the trigger is NOT
    /// registered with a <see cref="TriggerManager"/> and the colorless anthem
    /// is NOT registered against any <see cref="ContinuousEffectsService"/>.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Ruination Guide.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// colorless +1/+0 anthem against. May be null — no live bonus.</param>
    /// <param name="triggers">Trigger manager for registration. May be null —
    /// the Ingest trigger attaches structurally but isn't enrolled.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {2}{U}, 3/2). The JSON carries no
        // abilities — Devoid / Ingest / anthem are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors returns
        // empty regardless of the {U} pip; attach the KeywordAbility marker
        // for ability-scan discoverability. Same shape as Nettle Drone.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // CR 701.34 — Ingest marker for ability-scan discoverability. The
        // behaviour itself is the plain combat-damage trigger wired below.
        card.AddAbility(new KeywordAbility(IngestKeyword, card, owner));

        // ----------------------------------------------------------------
        // Ingest — "Whenever this creature deals combat damage to a player,
        // that player exiles the top card of their library." CR 510 /
        // CR 603.1. The damaged player is captured off the event in the
        // predicate (CR 603.3 — the condition is evaluated as the ability
        // would trigger, before it hits the stack) so the resolved effect
        // exiles from the correct library. Empty library = no-op (CR 120.3 —
        // failing to exile from an empty library is not itself a loss).
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var ingestEffect = new Effect(
            $"{CardName}: damaged player exiles the top card of their library (Ingest)",
            () =>
            {
                var victim = capturedDamaged;
                if (victim == null) return;

                var top = victim.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;

                victim.Zones.Library.RemoveCard(top);
                victim.Zones.Exile.AddCard(top);
                top.SetZone(ZoneType.Exile);
            });

        var ingestTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false; // "to a player" only
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { ingestEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ingestTrigger);
        triggers?.RegisterTriggeredAbility(ingestTrigger);

        // ----------------------------------------------------------------
        // "Other colorless creatures you control get +1/+0." CR 613.7c —
        // Layer 7c P/T modification scoped to the controller's battlefield,
        // gated on the creature being colorless (CR 105.2c — empty color
        // set). includeSelf: false honours "OTHER" (Ruination Guide is itself
        // colorless via Devoid). Requires a live ContinuousEffectsService to
        // take effect; the shape-only path leaves it unregistered.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new ColorlessCreatureAnthemEffect(
                source: card,
                power: AnthemPower,
                toughness: AnthemToughness));
        }

        return card;
    }
}

/// <summary>
/// CR 613.7c — "Other colorless creatures you control get +P/+T" anthem.
/// Mirror image of <see cref="ControllerCreatureAnthemEffect"/>'s
/// <c>requiredColor</c> gate: where that gate keys on the PRESENCE of a colour
/// ("White creatures you control"), this keys on the ABSENCE of all colour
/// ("colorless creatures you control" — CR 105.2c). Colocated with its sole
/// user, the same posture <see cref="ControllerCreatureAnthemEffect"/> takes
/// next to Heartless Summoning.
///
/// <para>While the source is on the battlefield, every COLORLESS creature
/// controlled by the source's controller (excluding the source itself)
/// receives the P/T delta. <see cref="IsActive"/> short-circuits when the
/// source leaves the battlefield (CR 614), so the bonus lifts on LTB.</para>
/// </summary>
public sealed class ColorlessCreatureAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly int _power;
    private readonly int _toughness;

    /// <summary>Construct a colorless-creature anthem.</summary>
    /// <param name="source">The permanent generating the effect (Ruination
    /// Guide).</param>
    /// <param name="power">P delta.</param>
    /// <param name="toughness">T delta.</param>
    public ColorlessCreatureAnthemEffect(Permanent source, int power, int toughness)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _power = power;
        _toughness = toughness;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        // "OTHER colorless creatures" — never buff Ruination Guide itself.
        if (ReferenceEquals(creature, _source)) return false;
        // CR 105.2c — a colorless object has no color. Use the printed/static
        // colour derivation (CardColors.GetColors, which honours Devoid)
        // rather than GetEffectiveColors(): the latter re-enters the layer
        // service (Compute → AppliesTo → GetEffectiveColors) and would recurse
        // while the layers are mid-evaluation. Same posture as
        // ControllerCreatureAnthemEffect's colour gate.
        // Deferred (v1 gap): a Layer-5 colour changer (a creature turned a
        // colour by another effect) is not reflected by this gate.
        if (CardColors.GetColors(creature).Count != 0) return false;
        return true;
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="ColorlessCreatureAnthemEffect"/>
    /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
    /// All filtering reads clonedSource.Controller live (correctly remapped).
    /// preserves: _power, _toughness; source → clonedSource.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new ColorlessCreatureAnthemEffect(clonedSource, _power, _toughness);
}
