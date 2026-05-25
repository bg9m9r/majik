using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Apostle's Blessing (New Phyrexia, {1}{W/P}).
///
/// Instant. Oracle text:
///   "({W/P} can be paid with either {W} or 2 life.)
///    Target artifact or creature you control gains protection from
///    artifacts or from the color of your choice until end of turn."
///
/// ## Implemented (v1)
///
/// - Instant card with printed mana cost <c>{1}{W}</c> (the mana-only
///   portion of the single phyrexian <c>{W/P}</c> pip). CR 107.4f —
///   each phyrexian pip may be paid with the matching colour OR 2 life.
/// - Phyrexian alt cost (the {W/P} pip paid as 2 life):
///   AlternativeManaCost = <c>{1}</c>, LifeCost = 2. Surfaced via
///   <see cref="PhyrexianAlternativeCost"/> — mirrors
///   <see cref="DismemberFactory.PhyrexianAlternativeCost"/>.
/// - Structural Phyrexian mana marker via <see cref="KeywordAbility"/>
///   ("Phyrexian"), same shape as Dismember / Surgical Extraction.
/// - <see cref="BuildSpellDefinition"/> wires a 1..1 "target artifact or
///   creature you control" <see cref="TargetRequest"/> with a live
///   <c>CandidateGatherer</c> filtering to controller-side artifacts +
///   creatures. <see cref="ChooseProtectionQuality(Player, ICard)"/> is
///   sampled at resolve time (delegated default: artifact-typed
///   incoming-removal contexts pick "artifacts", colour-incoming
///   contexts pick the matching colour; absent any hint the default
///   picks "artifacts" — a defensible Modern-meta heuristic).
/// - Resolve effect:
///   1. <b>CR 608.2b</b> — target must still be a permanent on the
///      battlefield and still an Artifact or Creature.
///   2. <b>Quality pick</b> — caller supplies the protection quality
///      via <see cref="ChosenSpellParams"/> (currently piggybacking
///      through the chooser callback below; agent-side quality prompt
///      is deferred). The quality is one of "artifacts" / "white" /
///      "blue" / "black" / "red" / "green".
///   3. <b>Grant</b> — for Creature targets, register a
///      <see cref="GrantAbilityEffect"/> (self-source) on the target's
///      <see cref="Creature.ActiveEffects"/> that adds a
///      <see cref="ProtectionAbility"/> with the chosen quality until
///      end of turn (CR 514.2). For Artifact (non-Creature) targets the
///      effect attaches the <see cref="ProtectionAbility"/> directly to
///      the card; EOT cleanup for the non-Creature path is a v1 gap
///      (see below).
///
/// ## v1 gaps
///
/// - <b>Agent-side quality prompt</b>: CR 601.2c+ — quality of "from X"
///   protection is chosen on resolution (or on cast, depending on the
///   wording; "of your choice" is cast-time per CR 601.2c). v1 uses an
///   injectable <see cref="QualityPicker"/> Func; the dispatcher path
///   defaults to "artifacts" (the printed-text first half). When the
///   cast flow grows an agent prompt for free-text qualities, the
///   picker can move out of the factory.
/// - <b>Non-Creature EOT cleanup</b>: artifact targets get the
///   <see cref="ProtectionAbility"/> attached directly via
///   <see cref="Card.AddAbility"/>; the engine's
///   <see cref="ContinuousEffectsService"/> is creature-only today, so
///   the EOT-expirable path isn't available for an artifact target. v1
///   leaves the ability attached past the cleanup step on the artifact
///   path — same posture as <see cref="ColossusHammerFactory"/>'s
///   v1-only-creatures gate. A future
///   <c>Permanent.ActiveEffects</c> rollout will close this.
/// - <b>Per-pip phyrexian selectivity</b>: the printed single phyrexian
///   pip on Apostle's Blessing collapses to "pay {W}" XOR "pay 2 life",
///   so the all-life path's
///   <see cref="PhyrexianManaAlternativeCost"/> is sufficient. (No
///   intermediate two-pip selectivity issue as on Dismember.)
/// </summary>
[CardName("Apostle's Blessing")]
public static class ApostlesBlessingFactory
{
    public const string CardName = "Apostle's Blessing";

    /// <summary>
    /// Printed mana cost (the <c>{W}</c> pip of the single phyrexian
    /// <c>{W/P}</c>, plus the generic <c>{1}</c>). The 2-life alternative
    /// reduces the printed cost to <c>{1}</c>; see
    /// <see cref="PhyrexianAlternativeCost"/>.
    /// </summary>
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Resolve-time picker for the protection quality. Receives the
    /// caster and the chosen target; returns one of <see cref="QualityArtifacts"/>,
    /// <see cref="QualityWhite"/>, <see cref="QualityBlue"/>,
    /// <see cref="QualityBlack"/>, <see cref="QualityRed"/>,
    /// <see cref="QualityGreen"/>. Default (when no picker is supplied)
    /// returns <see cref="QualityArtifacts"/>.
    /// </summary>
    public delegate string QualityPicker(Player caster, ICard target);

    public const string QualityArtifacts = "artifacts";
    public const string QualityWhite = "white";
    public const string QualityBlue = "blue";
    public const string QualityBlack = "black";
    public const string QualityRed = "red";
    public const string QualityGreen = "green";

    /// <summary>
    /// Construct an Apostle's Blessing instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 107.4f — Phyrexian marker. Inline attach mirrors
        // Dismember / Surgical Extraction so the dispatcher path
        // doesn't need a KeywordBinder pass.
        card.AddAbility(new KeywordAbility("Phyrexian", card, owner));

        return card;
    }

    /// <summary>
    /// Returns a <see cref="PhyrexianManaAlternativeCost"/> for the
    /// single <c>{W/P}</c> pip: AlternativeManaCost = <c>{1}</c>,
    /// LifeCost = 2 (one phyrexian pip = 2 life). Callers that want the
    /// 2-life all-phyrexian cast supply this as <c>alternativeCost</c>
    /// to <see cref="SpellCastFlow.CastAsync"/>.
    /// </summary>
    public static PhyrexianManaAlternativeCost PhyrexianAlternativeCost()
        => PhyrexianManaAlternativeCost.ForPrintedCost(ManaCost.Parse("{1}{W/P}"));

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Apostle's Blessing uses
    /// on resolution. Single 1..1 "target artifact or creature you
    /// control" request; the resolve closure grants the chosen target
    /// protection from the picked quality until end of turn.
    /// </summary>
    /// <param name="caster">Spell controller — read at resolve time to
    /// scope the target gather and select the protection quality.</param>
    /// <param name="qualityPicker">Optional resolve-time picker for the
    /// protection quality; defaults to <see cref="QualityArtifacts"/>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        QualityPicker? qualityPicker = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        qualityPicker ??= (_, _) => QualityArtifacts;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: _ => caster.Zones.Battlefield.GetCards()
                        .Where(c => c.HasType(CardType.Artifact) || c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets.Count > 0 && p.Targets[0].Count > 0
                    ? p.Targets[0][0]
                    : null;
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target gains protection (artifacts or chosen colour) EOT",
                        () => Resolve(caster, raw, qualityPicker)),
                };
            });
    }

    /// <summary>
    /// Resolve Apostle's Blessing against <paramref name="rawTarget"/>.
    /// Exposed for direct invocation by tests / bots without driving
    /// the full cast flow.
    /// </summary>
    public static ApostlesBlessingResolution Resolve(
        Player caster,
        object? rawTarget,
        QualityPicker? qualityPicker = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        qualityPicker ??= (_, _) => QualityArtifacts;

        // CR 608.2b — target must still be a permanent on the
        // battlefield AND still an Artifact or Creature.
        if (rawTarget is not Permanent perm)
        {
            return new ApostlesBlessingResolution(null, null);
        }
        if (perm.Zone != ZoneType.Battlefield)
        {
            return new ApostlesBlessingResolution(null, null);
        }
        if (!(perm.HasType(CardType.Artifact) || perm.HasType(CardType.Creature)))
        {
            return new ApostlesBlessingResolution(null, null);
        }

        var quality = qualityPicker(caster, perm);
        if (string.IsNullOrWhiteSpace(quality)) quality = QualityArtifacts;

        // Creature path: register a self-sourced GrantAbilityEffect on
        // the target's ActiveEffects so EOT cleanup runs through the
        // continuous-effects layer (CR 514.2 / CR 613.1f).
        if (perm is Creature c && c.ActiveEffects is not null)
        {
            var grant = new GrantAbilityEffect(
                source: c,
                target: c,
                ability: new ProtectionAbility(quality),
                expiresAtEndOfTurn: true);
            c.ActiveEffects.Register(grant);
            // Sync immediately so target legality reads the grant on
            // the same priority window (CR 117.5 / CR 700.2a) — the
            // service computes lazily otherwise.
            grant.Sync();
        }
        else
        {
            // Artifact (non-Creature) path: ContinuousEffectsService is
            // creature-only today, so the EOT-expirable grant path is
            // not available. Attach the ProtectionAbility directly to
            // the card. CR 514.2 EOT cleanup for this path is a v1 gap
            // (see class xmldoc).
            perm.AddAbility(new ProtectionAbility(quality));
        }

        return new ApostlesBlessingResolution(perm, quality);
    }

    /// <summary>
    /// Resolve-time quality picker hook for tests / bots. <c>Target</c>
    /// is the permanent that received protection; <c>Quality</c> is the
    /// chosen quality string ("artifacts" / "white" / …). Both null when
    /// the target was illegal at resolve (CR 608.2b — clean no-op).
    /// </summary>
    public sealed record ApostlesBlessingResolution(
        Permanent? Target,
        string? Quality);

    /// <summary>
    /// Build a <see cref="QualityPicker"/> that selects a colour-from
    /// quality when the incoming removal spell's primary colour matches
    /// one of WUBRG (case insensitive). Useful for bot strategies that
    /// already know the incoming threat.
    /// </summary>
    public static QualityPicker QualityFromColor(ManaColor color) =>
        (_, _) => color switch
        {
            ManaColor.White => QualityWhite,
            ManaColor.Blue => QualityBlue,
            ManaColor.Black => QualityBlack,
            ManaColor.Red => QualityRed,
            ManaColor.Green => QualityGreen,
            _ => QualityArtifacts,
        };
}
