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
/// Named-card factory for Saheeli, Sublime Artificer (War of the Spark,
/// {1}{U/R}{U/R}).
///
/// Legendary Planeswalker — Saheeli. Starting loyalty 5. Oracle text
/// (Scryfall, verified 2026-06-23):
///   "Whenever you cast a noncreature spell, create a 1/1 colorless Servo
///    artifact creature token.
///    −2: Target artifact you control becomes a copy of another target
///    artifact or creature you control until end of turn, except it's an
///    artifact in addition to its other types."
///
/// The base shape (name, Legendary Planeswalker — Saheeli, {1}{U/R}{U/R},
/// loyalty 5) is materialised from the embedded JSON definition
/// (<c>saheeli-sublime-artificer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The static cast-trigger and the
/// −2 loyalty ability are layered on here — the JSON <c>AbilityDefinition</c>
/// schema doesn't express cast triggers, token creation, or copy effects, so
/// they live in the factory (same posture as
/// <see cref="AjaniCallerOfThePrideFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Servo cast trigger (CR 603.1)</b>: "Whenever you cast a noncreature
///   spell, create a 1/1 colorless Servo artifact creature token." A
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> matching
///   when the spell's controller is Saheeli's controller AND the spell's card
///   is NOT a creature (CR 302.1 — same noncreature predicate as Monastery
///   Mentor / Young Pyromancer's "instant or sorcery", widened to all
///   noncreature). On resolution <see cref="TokenFactory.CreateOnBattlefield"/>
///   mints a 1/1 colourless <see cref="CardSubtype.Servo"/> creature token,
///   then additively stamps <see cref="CardType.Artifact"/> so it reports
///   Artifact + Creature — Servo (CR 111.1 — token shell is Creature-only;
///   same Artifact-stamp shape as <see cref="PiaAndKiranNalaarFactory"/>'s
///   Thopters). A planeswalker's static triggered ability still functions while
///   it is on the battlefield (CR 113.6 / 603.1 — the source is the
///   planeswalker, not a creature).
/// - <b>−2: Target artifact you control becomes a copy of another target
///   artifact or creature you control until end of turn, except it's an
///   artifact in addition to its other types (CR 606 + CR 707.2 + CR 613)</b>:
///   registers a <see cref="CopyCharacteristicsEffect"/> via
///   <see cref="CopyCharacteristicsEffect.RegisterCopy"/> with
///   <c>expiresAtEndOfTurn: true</c> on the target artifact (dropped at the
///   cleanup step CR 514.2). The copy source is the chosen artifact/creature.
///   The "except it's an artifact in addition to its other types" rider
///   (CR 707.9b) is honoured by re-stamping <see cref="CardType.Artifact"/> on
///   the copy after the characteristic copy (so a copy of a non-artifact
///   creature stays an artifact). The targets are supplied by the resolvers;
///   null / off-battlefield / not-controlled results no-op while the loyalty
///   change still applies.
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> doesn't declare
///   <see cref="Majik.Core.Targeting.TargetRequest"/>s; the −2's two targets
///   (the artifact you control + the artifact/creature you control it copies)
///   are picked from supplied resolvers rather than via the agent — same gap
///   Ajani / Chandra / Teferi / Karn share.
/// - <b>Token-doubling replacement</b>: the Servo trigger mints the token
///   directly rather than routing through the
///   <see cref="Majik.Core.Effects.TokenCreationIntent"/> replacement bus, so
///   Doubling Season / Anointed Procession don't double the Servos in v1
///   (same posture as Pia and Kiran Nalaar / Ajani −8).
/// </summary>
[CardName("Saheeli, Sublime Artificer")]
public static class SaheeliSublimeArtificerFactory
{
    public const string CardName = "Saheeli, Sublime Artificer";
    public const string Slug = "saheeli-sublime-artificer";
    public const int StartingLoyalty = 5;

    public const string ServoTokenName = "Servo";
    public const int ServoPower = 1;
    public const int ServoToughness = 1;

    /// <summary>
    /// Construct Saheeli with no resolvers / live wiring — the Servo cast
    /// trigger is attached for shape observability and the −2 no-ops (no
    /// targets / no effects service). Loyalty changes still apply. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, copyTargetResolver: null, copySourceResolver: null,
            effects: null, zones: null);

    /// <summary>
    /// Construct Saheeli, Sublime Artificer.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="copyTargetResolver">Returns the "target artifact you
    /// control" the −2 turns into a copy. May be null / yield null — the −2
    /// no-ops.</param>
    /// <param name="copySourceResolver">Returns the "another target artifact or
    /// creature you control" whose characteristics are copied. May be null /
    /// yield null — the −2 no-ops.</param>
    /// <param name="effects">Continuous-effects service the −2's
    /// <see cref="CopyCharacteristicsEffect"/> is registered on. May be null —
    /// the −2 still applies loyalty but records no copy effect.</param>
    /// <param name="zones">ZoneService used to mint Servo tokens so
    /// CardMovedEvent fires (Soul Warden etc.). May be null — tokens enter via
    /// the controller's own battlefield zone directly.</param>
    public static Planeswalker Create(
        Player owner,
        Func<Permanent?>? copyTargetResolver,
        Func<Permanent?>? copySourceResolver,
        ContinuousEffectsService? effects,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Saheeli, {1}{U/R}{U/R}, loyalty 5). The JSON carries
        // no abilities — the cast trigger + −2 are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var saheeli = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Static cast trigger — CR 603.1.
        //   "Whenever you cast a noncreature spell, create a 1/1 colorless
        //    Servo artifact creature token."
        // Predicate: spell controller matches AND the spell is not a Creature
        // (CR 302.1) — the noncreature filter Monastery Mentor uses, on a
        // planeswalker source (CR 113.6 — a planeswalker's triggered ability
        // functions while it is on the battlefield). Servo token is built
        // Creature-only then additively stamped Artifact (CR 111.1).
        // ----------------------------------------------------------------
        var servoCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, saheeli.Controller ?? owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var servoEffect = new Effect(
            $"{CardName}: create 1/1 colourless Servo artifact creature token (whenever you cast a noncreature spell)",
            () =>
            {
                var controller = saheeli.Controller ?? owner;
                CreateServoToken(controller, zones);
            });

        var servoTrigger = new TriggeredAbility(
            source: saheeli,
            controller: owner,
            condition: servoCondition,
            effects: new IEffect[] { servoEffect },
            activeZones: new[] { ZoneType.Battlefield });

        saheeli.AddAbility(servoTrigger);

        // ----------------------------------------------------------------
        // −2: Target artifact you control becomes a copy of another target
        //     artifact or creature you control until end of turn, except it's
        //     an artifact in addition to its other types.
        // CR 606 (loyalty) + CR 707.2 / 613.2 Layer 1 (becomes a copy) +
        // CR 707.9b (the "except it's an artifact in addition to its other
        // types" rider) + CR 514.2 (until-EOT expiry). Same copy primitive as
        // Shifting Woodland, with an extra Artifact stamp so a copy of a
        // non-artifact creature stays an artifact.
        // ----------------------------------------------------------------
        saheeli.AddAbility(new LoyaltyAbility(saheeli, -2, () =>
        {
            var target = copyTargetResolver?.Invoke();
            var copySource = copySourceResolver?.Invoke();
            if (target == null || copySource == null) return;
            if (ReferenceEquals(target, copySource)) return; // "another target"
            if (target.Zone != ZoneType.Battlefield) return;
            if (copySource.Zone != ZoneType.Battlefield) return;
            if (effects == null) return;

            // CR 707.2 — becomes a copy in place until end of turn. RegisterCopy
            // also mirrors the source's printed non-keyword activated/triggered
            // abilities onto the target (re-instantiated bound to it; CR 707.2),
            // dropped at the cleanup step alongside the copy.
            CopyCharacteristicsEffect.RegisterCopy(
                effects,
                target,
                copySource,
                abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind,
                expiresAtEndOfTurn: true);

            // CR 707.9b — "except it's an artifact in addition to its other
            // types". The characteristic copy (Layer 1) overwrites the target's
            // type line with the source's; a Layer-4 AddCardTypeEffect re-adds
            // Artifact ON TOP (CR 613.1d) so a copy of a (non-artifact) creature
            // remains an artifact. Same primitive Phyrexian Metamorph uses for
            // its identical rider — here paired with expiresAtEndOfTurn so the
            // Artifact stamp drops at the same cleanup step as the copy
            // (CR 514.2).
            effects.Register(new AddCardTypeEffect(
                target, CardType.Artifact, expiresAtEndOfTurn: true));
        }));

        return saheeli;
    }

    /// <summary>
    /// CR 111 / 111.6 — create one 1/1 colourless Servo artifact creature token
    /// under <paramref name="controller"/>'s control. The token is built
    /// Creature-only (the token shell) then additively stamped
    /// <see cref="CardType.Artifact"/> (CR 111.1).
    /// </summary>
    public static Creature CreateServoToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: ServoTokenName,
            Power: ServoPower,
            Toughness: ServoToughness,
            Subtypes: new[] { CardSubtype.Servo },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 colorless Servo artifact creature
            // token". Empty colour set = colourless.
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        token.AddCardType(CardType.Artifact);
        return token;
    }
}
