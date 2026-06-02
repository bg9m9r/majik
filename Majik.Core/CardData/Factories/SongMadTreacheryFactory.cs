using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Song-Mad Treachery // Song-Mad Ruins (Kamigawa: Neon Dynasty, {3}{R}{R}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Gain control of target creature until end of turn. Untap that creature.
///    It gains haste until end of turn."
///
/// This is the Threaten / Act of Treason template (CR 805 standard wording).
/// Back face — <see cref="SongMadRuinsFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {R}.").
///
/// ## Engine wiring — temporary control change (CR 613.2 / CR 514.2)
///
/// The resolve body is the declarative <c>gain_control</c> verb
/// (<see cref="GainControlEffectDef"/> with <c>Duration = "end_of_turn"</c>),
/// composed through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>. At resolution
/// it registers a <see cref="TemporaryControlChangeEffect"/> on the live
/// <see cref="ContinuousEffectsService"/> (swapping the target's real
/// controller so it can attack for its new controller), untaps it, and grants
/// it haste until end of turn (CR 302.6). At the cleanup step (CR 514.2) the
/// control reverts to the owner and the haste grant ends — both handled by the
/// continuous-effects service's end-of-turn expiry. CR 608.2b — an illegal
/// target at resolution fizzles the whole spell.
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Two-factory cast-either-face dispatch — same architecture as
/// <see cref="ValakutAwakeningFactory"/> / <see cref="ValakutStoneforgeFactory"/>
/// (MDFC spell-front + tapland-back). Casting the front face resolves
/// "Song-Mad Treachery" → this factory → a <see cref="Sorcery"/> carrying the
/// gain-control effect. Playing the back face resolves "Song-Mad Ruins" →
/// <see cref="SongMadRuinsFactory"/> → a tapland.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>song-mad-treachery.json</c>). The <see cref="MdfcState"/> face tracker
/// and the resolve-time spell behaviour are attached in code.
/// </summary>
[CardName("Song-Mad Treachery")]
public static class SongMadTreacheryFactory
{
    public const string CardName = "Song-Mad Treachery";
    public const string BackName = "Song-Mad Ruins";
    public const string Slug = "song-mad-treachery";

    /// <summary>
    /// Construct the front face of Song-Mad Treachery as a Sorcery with owner /
    /// controller wired and the <see cref="MdfcState"/> face tracker attached
    /// (starts on the front face, with a castable LAND back face). Identity
    /// comes from the embedded JSON; the resolve body is produced by
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at cast time and materializes a fresh
        // back-face land instance when chosen.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                SongMadRuinsFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Song-Mad Treachery's front
    /// face — the declarative <c>gain_control</c> verb (Threaten template).
    /// Requires the live <see cref="ContinuousEffectsService"/> to register the
    /// temporary control change + haste grant; without it the control swap is a
    /// no-op (shape-only test path), mirroring the other control factories.
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
                },
            },
            replacements: null,
            continuous: effects);
    }
}
