using System;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prey Upon (Innistrad, {G}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Target creature you control fights target creature you don't control.
///    (Each deals damage equal to its power to the other.)"
///
/// ## Declarative spell schema (fight)
/// <see cref="BuildDefinition"/> declares a single <see cref="FightEffectDef"/>
/// verb in <c>source: "target"</c> mode and routes it through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same
/// CR 701.12 fight verb the activated/spell path shares. The two creatures are
/// declared as two ordered targets: the FIGHTER ("a creature you control",
/// filter <c>creature_you_control</c>) and the OTHER creature ("a creature you
/// don't control", filter <c>creature_you_dont_control</c>). The control
/// scoping is enforced at gather time (CR 109.5); resolution re-checks both are
/// creatures still on the battlefield (CR 608.2b / 701.12c — a fight needs both
/// fighters, so if either is gone the whole fight fizzles).
///
/// At resolution the verb routes through
/// <see cref="Majik.Core.Primitives.Fx.Fight"/> so each creature deals damage
/// equal to its (pre-fight) power to the other simultaneously (CR 701.12a),
/// honouring deathtouch (CR 702.2b) and lifelink (CR 702.15a). Fight is damage
/// but NOT combat damage — no first/double strike, no combat triggers, no
/// trample. The lethal-damage / deathtouch state-based action runs afterward
/// (CR 704).
///
/// Mirrors <see cref="ShatterFactory"/>'s declarative-spell posture (card shape
/// via <see cref="CardDef"/>, resolve behaviour via the shared verb). The card
/// also resolves through <see cref="SpellTemplates.Templates.Damage.FightTemplate"/>
/// at live cast time (the template now likewise routes through
/// <c>Fx.Fight</c>); this factory adds the <c>[CardName]</c> implemented flag
/// plus the declaratively-tested <see cref="BuildDefinition"/>.
/// </summary>
[CardName("Prey Upon")]
public static class PreyUponFactory
{
    public const string CardName = "Prey Upon";
    public const string PrintedManaCost = "{G}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (the fight)
    /// is built on demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature you control fights target creature you don't
    /// control" <see cref="SpellDefinition"/> declaratively (the <c>fight</c>
    /// verb in <c>source: "target"</c> mode).
    /// </summary>
    /// <param name="targetResolver">Accepted for call-site compatibility with
    /// the bespoke spell factories; the declarative path reads the cast flow's
    /// already-resolved targets directly, so the resolver is effectively
    /// identity.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object>? targetResolver = null) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new FightEffectDef
                {
                    Source = "target",
                    ControllerTargetFilter = "creature_you_control",
                    TargetFilter = "creature_you_dont_control",
                },
            });
}
