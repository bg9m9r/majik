using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flare of Faith (Modern Horizons 3, {1}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gets +2/+2 until end of turn. If it's a Human, instead
///    it gets +3/+3 and gains indestructible until end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {1}{W} (white, MV 2). Card shape comes
///   from the embedded JSON (<c>flare-of-faith.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> — same load path as
///   <see cref="BlossomingDefenseFactory"/>. The resolve-time body lives in
///   <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
///   carries a target request not expressible in the data-only JSON schema.
/// - <see cref="BuildDefinition"/> wires the resolve effect: a 1..1
///   "target creature" <see cref="TargetRequest"/> (any creature — unlike
///   <see cref="BlossomingDefenseFactory"/> there is no "you control" rider).
///   On resolve (CR 608.2b illegal-target guard first), the printed
///   "instead" replaces the base +2/+2 when the target is a Human
///   (CR 613 effective subtypes, read at resolution):
///   <list type="bullet">
///   <item>Non-Human: register a <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2)
///     (CR 613.1g Layer 7c, CR 514.2 EOT expiry).</item>
///   <item>Human: instead register a <see cref="PumpUntilEndOfTurnEffect"/>
///     (+3, +3) AND a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///     "Indestructible" (CR 702.12 — won't be destroyed by lethal damage or a
///     "destroy" effect). EOT expiry per CR 514.2.</item>
///   </list>
///   The Human test is performed once, at resolution, against the target's
///   <see cref="Permanent.GetEffectiveSubtypes"/> (CR 613 — honours
///   type-changing effects); the +2/+2 and the +3/+3 / Indestructible branches
///   are mutually exclusive ("instead").
///
/// Mirrors <see cref="BlossomingDefenseFactory"/>'s pump + keyword-grant resolve
/// shape; the Human conditional swaps Hexproof-for-Indestructible and bumps the
/// pump magnitude. Belongs to the MH3 "Flare" cycle alongside
/// <see cref="FlareOfDenialFactory"/>, though Flare of Faith carries no
/// alternative cost.
/// </summary>
[CardName("Flare of Faith")]
public static class FlareOfFaithFactory
{
    public const string CardName = "Flare of Faith";
    public const string Slug = "flare-of-faith";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>Base Layer 7c +P/+T magnitude on a non-Human target (CR 613.1g).</summary>
    public const int BasePumpAmount = 2;

    /// <summary>Layer 7c +P/+T magnitude on a Human target — the "instead" branch (CR 613.1g).</summary>
    public const int HumanPumpAmount = 3;

    /// <summary>Granted keyword on the Human branch — CR 702.12 Indestructible.</summary>
    public const string GrantedIndestructible = "Indestructible";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request, no X. On resolution see <see cref="Resolve"/>.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Flare of Faith — target creature gets +2/+2 (or +3/+3 and indestructible if Human) until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613 — read effective subtypes so a creature whose type was changed
        // to/from Human is judged by its current type, not its printed one. The
        // "instead" clause makes the two branches mutually exclusive.
        var isHuman = target.GetEffectiveSubtypes().Contains(CardSubtype.Human);

        if (isHuman)
        {
            // "instead it gets +3/+3 and gains indestructible until end of turn".
            // CR 613.1g — Layer 7c +3/+3; CR 514.2 EOT expiry.
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, HumanPumpAmount, HumanPumpAmount));

            // CR 702.12 — Indestructible; Layer-6 keyword grant, EOT expiry (CR 514.2).
            target.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(target, GrantedIndestructible));
        }
        else
        {
            // Base mode: "+2/+2 until end of turn". CR 613.1g / CR 514.2.
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, BasePumpAmount, BasePumpAmount));
        }
    }
}
