using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Malevolent Whispers (Eldritch Moon, {3}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Gain control of target creature until end of turn. Untap that creature.
///    It gets +2/+0 and gains haste until end of turn.
///    Madness {3}{R} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// This is the Threaten / Act of Treason template (CR 613.2 / CR 514.2)
/// extended with a bundled <b>+2/+0 until-end-of-turn pump rider</b> — a
/// declarative field on the shared <see cref="GainControlEffectDef"/>
/// (<c>PowerBonus = 2</c>), consumed by the existing temporary-control
/// resolution in <see cref="CardDefRuntime"/>'s gain_control builder.
///
/// ## Engine wiring — temporary control change + pump (CR 613.2 / 613.1g / 514.2)
///
/// The resolve body is the declarative <c>gain_control</c> verb
/// (<see cref="GainControlEffectDef"/> with <c>Duration = "end_of_turn"</c>,
/// <c>PowerBonus = 2</c>), composed through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>. At resolution
/// it registers a <see cref="TemporaryControlChangeEffect"/> on the live
/// <see cref="ContinuousEffectsService"/> (swapping the target's real
/// controller so it can attack for its new controller), untaps it
/// (CR 701.21), grants it haste until end of turn (CR 302.6), and registers a
/// <see cref="PumpUntilEndOfTurnEffect"/>(+2, +0) (Layer 7c, CR 613.1g). At the
/// cleanup step (CR 514.2) ALL the until-end-of-turn riders end together —
/// control reverts to the owner, haste ends, and the +2/+0 boost ends. CR
/// 608.2b — an illegal target at resolution fizzles the whole spell.
///
/// ## Madness {3}{R} (CR 702.35)
///
/// Madness is served intrinsically by the engine's central discard funnel via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (which already lists
/// "Malevolent Whispers" → {3}{R}); no per-card madness wiring is needed here.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>malevolent-whispers.json</c>). The resolve-time spell behaviour is
/// produced by <see cref="BuildSpellDefinition"/>, mirroring
/// <see cref="SongMadTreacheryFactory"/> (the bare Threaten template) plus the
/// +2/+0 rider.
/// </summary>
[CardName("Malevolent Whispers")]
public static class MalevolentWhispersFactory
{
    public const string CardName = "Malevolent Whispers";
    public const string Slug = "malevolent-whispers";

    /// <summary>Layer 7c +P magnitude of the bundled pump rider (CR 613.1g).</summary>
    public const int PowerBonus = 2;

    /// <summary>
    /// Construct Malevolent Whispers as a Sorcery with owner / controller wired.
    /// Identity comes from the embedded JSON; the resolve body is produced by
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Malevolent Whispers — the
    /// declarative <c>gain_control</c> verb (Threaten template) carrying the
    /// bundled <c>PowerBonus = 2</c> (+2/+0) rider. Requires the live
    /// <see cref="ContinuousEffectsService"/> to register the temporary control
    /// change + untap + haste + pump; without it the control swap is a no-op
    /// (shape-only test path), mirroring the other control factories.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new GainControlEffectDef
                {
                    TargetFilter = "creature",
                    Duration = "end_of_turn",
                    Untap = true,
                    GainsHaste = true,
                    PowerBonus = PowerBonus,
                    ToughnessBonus = 0,
                },
            },
            replacements: null,
            continuous: effects);
    }
}
