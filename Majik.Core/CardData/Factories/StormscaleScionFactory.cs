using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormscale Scion (Tarkir: Dragonstorm,
/// {4}{R}{R}). Creature — Dragon 4/4. Oracle text (verified against
/// Scryfall):
///   "Flying
///    Other Dragons you control get +1/+1.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn. Copies become tokens.)"
///
/// The card's base shape (name, type, Dragon subtype, {4}{R}{R}, 4/4) is
/// materialised from the embedded JSON definition
/// (<c>stormscale-scion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Flying keyword, Dragon-lord anthem, Storm) are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't yet express keyword
/// markers, lord statics, or Storm, so they live in the factory (same
/// posture as the other JSON-backed cards whose behaviour outgrows the
/// schema).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>
///   and <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/>
///   surface the evasion / block-legality properties. Same shape as
///   <see cref="WallOfSwordsFactory"/>.
/// - <b>Lord static (CR 613.7c / 613.1g)</b>: "Other Dragons you control
///   get +1/+1." Wired via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: Dragon, power: 1, toughness: 1, includeSelf:
///   false, allPlayers: false</c> — controller-scoped (opponents' Dragons
///   are unaffected); <c>includeSelf: false</c> honours the printed
///   "Other". Identical shape to <see cref="ElvishArchdruidFactory"/>'s
///   Elf anthem. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
/// - <b>Storm (CR 702.40 / 707.10b)</b>: an on-cast
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>,
///   gated to this card, <c>activeZones = Stack</c> (Storm functions on the
///   stack — CR 702.40a). The storm count is read at condition-evaluation
///   time from <see cref="TurnState.SpellsCastByPlayer"/> (minus this spell
///   itself, recovering CR 702.40a's "other spells" count — same counting
///   convention as <see cref="Majik.Core.Keywords.StormHelper"/>). Because
///   Stormscale Scion is a <i>permanent</i> spell, "Copies become tokens"
///   (CR 707.10b) is the operative clause: each copy enters as a token
///   copy of the Scion rather than re-executing a resolve effect list (a
///   permanent spell has none). The resolve-time effect therefore mints
///   <c>stormCount</c> token copies of the Scion — 4/4 red flying Dragons —
///   on the controller's battlefield via <see cref="TokenFactory"/>
///   (same token-copy primitive as
///   <see cref="KikiJikiMirrorBreakerFactory"/>).
///
/// ## Why not <see cref="Majik.Core.Keywords.StormHelper"/>?
/// StormHelper copies a spell by re-executing its <see cref="IEffect"/>
/// list (the load-bearing semantic for instants/sorceries like Empty the
/// Warrens / Brain Freeze). A creature spell carries no token-creation
/// effect — its "resolution" is the engine putting the card onto the
/// battlefield, not an effect in the spell's effect list. So Stormscale
/// Scion needs the "Copies become tokens" path (CR 707.10b) instead: a
/// bespoke storm trigger that mints token copies of the source. The count
/// logic mirrors StormHelper exactly.
///
/// ## Deferred (v1 gaps)
/// - <b>Copies as distinct stack objects</b>: the token copies are minted
///   directly at trigger resolution rather than pushed as real
///   permanent-spell stack objects that then resolve into tokens.
///   Acceptable for the printed observable contract (N-1 token Scions for
///   N spells cast this turn). Anything subscribing to
///   <see cref="StackObjectAddedEvent"/> for the storm copies won't see
///   them — same posture as <see cref="Majik.Core.Services.SpellCopier"/>.
/// - <b>Token-copy fidelity</b>: the token is built from a fixed
///   <see cref="TokenFactory.TokenSpec"/> (name / 4-4 / Dragon / Flying /
///   red) rather than a full copiable-values snapshot of the source
///   (CR 707.2). The Scion has no characteristic-defining quirks, so the
///   fixed spec reproduces every printed copiable value; the token Scions
///   themselves carry Flying and (if a layers service is live) buff one
///   another's anthem like the original — same lossy-copy posture as
///   <see cref="KikiJikiMirrorBreakerFactory"/>.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="LordStaticEffect.IsActive"/> short-circuits when
///   the Scion isn't on the battlefield so the bonus lifts correctly (same
///   posture as <see cref="ElvishArchdruidFactory"/>).
/// </summary>
[CardName("Stormscale Scion")]
public static class StormscaleScionFactory
{
    public const string CardName = "Stormscale Scion";
    public const string Slug = "stormscale-scion";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Stormscale Scion with no live wiring. Flying + the Storm
    /// trigger are attached (the storm trigger fires structurally but mints
    /// no tokens with a null stack / turn-state); the lord anthem is NOT
    /// registered (no continuous-effects service). Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, stack: null, turnState: null);

    /// <summary>
    /// Construct Stormscale Scion with a layers service for the Dragon
    /// anthem but no storm wiring (storm trigger still attaches
    /// structurally). Convenience overload for lord-static tests.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, stack: null, turnState: null);

    /// <summary>
    /// Construct a fully-wired Stormscale Scion.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// Dragon +1/+1 anthem against. Pass null to skip the anthem.</param>
    /// <param name="stack">Stack the storm copies are minted alongside.
    /// Accepted for parity with the StormHelper shape; the token-mint path
    /// doesn't push onto it (see factory remarks). May be null.</param>
    /// <param name="turnState">Per-turn ledger consulted at
    /// condition-evaluation time for the spells-cast count. May be null —
    /// the storm count then resolves to zero (no copies) but the trigger
    /// still fires structurally.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Majik.Core.Stack.Stack? stack,
        TurnState? turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dragon subtype, {4}{R}{R}, 4/4). The JSON carries no abilities —
        // Flying / anthem / Storm are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities
        // surfaces evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Lord static — CR 613.7c (P/T) + CR 613.1g (controller scope).
        //   "Other Dragons you control get +1/+1."
        // allPlayers: false → controller-scoped. includeSelf: false honours
        // the printed "Other".
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Dragon,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        // ----------------------------------------------------------------
        // Storm — CR 702.40 / 707.10b. On-cast trigger over SpellCastEvent
        // gated to this card; functions on the stack (CR 702.40a). For each
        // OTHER spell cast this turn, the copy becomes a token (CR 707.10b):
        // we mint that many token copies of the Scion. Count convention
        // mirrors StormHelper (total spells cast by controller minus this
        // one).
        // ----------------------------------------------------------------
        card.AddAbility(BuildStorm(card, owner, stack, turnState));

        return card;
    }

    /// <summary>
    /// Build the Storm on-cast triggered ability. Mirrors
    /// <see cref="Majik.Core.Keywords.StormHelper"/>'s count logic, but the
    /// resolve effect mints token copies of the Scion (CR 707.10b — "Copies
    /// become tokens") rather than re-executing a spell effect list.
    /// </summary>
    private static TriggeredAbility BuildStorm(
        Creature card,
        Player controller,
        Majik.Core.Stack.Stack? stack,
        TurnState? turnState)
    {
        var capturedStormCount = 0;

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 702.40a — "When you cast this spell" gate.
            if (!ReferenceEquals(e.Spell.Card, card)) return false;

            // CR 702.40a — count OTHER spells cast this turn. The cast of
            // this spell has already been tallied into SpellsCastByPlayer
            // (TurnDriver's typed SpellCastEvent handler runs before the
            // TriggerManager's SubscribeAll handler — see StormHelper's
            // note on EventBus.Publish ordering), so subtract 1.
            var total = turnState?.SpellsCastByPlayer(controller) ?? 0;
            capturedStormCount = Math.Max(0, total - 1);
            return true;
        });

        var copyEffect = new Effect(
            "Storm — copy this spell for each other spell cast this turn; copies become tokens (CR 702.40 / 707.10b)",
            () =>
            {
                if (capturedStormCount <= 0) return;

                var bfController = card.Controller ?? controller;

                // CR 707.2 — reproduce the Scion's copiable values. The
                // Scion has no CDA / quirks, so a fixed spec reproduces
                // every printed copiable value (name, 4/4, Dragon, Flying,
                // red). Same token-copy primitive as Kiki-Jiki.
                var spec = new TokenFactory.TokenSpec(
                    Name: CardName,
                    Power: Power,
                    Toughness: Toughness,
                    Subtypes: new[] { CardSubtype.Dragon },
                    Keywords: new[] { "Flying" },
                    Colors: new[] { ManaColor.Red });

                for (var i = 0; i < capturedStormCount; i++)
                {
                    // zones: null — no live ZoneService threaded through the
                    // factory's wiring overload; TokenFactory falls back to
                    // direct battlefield placement (CR 111.6).
                    TokenFactory.CreateOnBattlefield(spec, bfController, zones: null);
                }
            });

        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: new IEffect[] { copyEffect },
            // Storm "functions on the stack" (CR 702.40a).
            activeZones: new[] { ZoneType.Stack });
    }
}
