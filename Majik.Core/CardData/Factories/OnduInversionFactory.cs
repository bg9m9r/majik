using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Ondu Inversion // Ondu Skyruins (Zendikar Rising, {6}{W}{W}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Destroy all nonland permanents."
///
/// Back face — <see cref="OnduSkyruinsFactory"/> (Land —
/// "This land enters tapped." / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="EmeriasCallFactory"/> / <see cref="EmeriaShatteredSkyclaveFactory"/>
/// (ZNR sorcery-front + tapland-back MDFC). The front-face card carries a
/// castable <see cref="MdfcFace.Land"/> back-face descriptor on its
/// <see cref="MdfcState"/> so <see cref="Majik.Core.Game.MdfcCastFlow"/> can
/// offer the controller a face choice at play time and materialize a fresh
/// back-face land instance (Ondu Skyruins) when chosen. No transform happens —
/// only the chosen face exists (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>ondu-inversion.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time sweep behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor mass destruction).
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{6}{W}{W}</c>, mono-white (two {W} pips).
/// - <see cref="MdfcState"/> attached (front = "Ondu Inversion",
///   back = "Ondu Skyruins") with a castable <see cref="MdfcFace.Land"/>
///   back face; starts on the front face.
/// - No modes, no X, no target requests — an untargeted board wipe.
/// - <see cref="BuildResolveEffect(IReadOnlyList{Player})"/>: for every
///   supplied player, snapshot the battlefield, filter to permanents that are
///   NOT a <see cref="CardType.Land"/>, and route each through
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7 —
///   destroyed permanents go to their owner's graveyard). Same nonland-sweep
///   shape as <see cref="WrathOfTheSkiesFactory"/> minus the mana-value
///   ceiling, and the same multi-player snapshot posture as
///   <see cref="WrathOfGodFactory"/>.
/// - Single-arg dispatcher path produces the card shape only — the resolve
///   effect / spell definition are built on demand (same posture as
///   <see cref="EmeriasCallFactory"/> / <see cref="WrathOfGodFactory"/>).
///
/// ## Destroy semantics
///
/// Printed text is plain "Destroy" with NO "can't be regenerated" rider, so
/// the sweep uses <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>:
/// Indestructible (CR 702.12) cancels the destroy and any active regeneration
/// shield (CR 701.15) is honoured — same gating as
/// <see cref="WrathOfTheSkiesFactory"/> (and unlike Wrath of God / Damnation,
/// which print the no-regen rider and pass <c>DestroyNoRegeneration</c>).
///
/// ## References
///
/// - <see cref="EmeriasCallFactory"/> — companion ZNR MDFC front face with the
///   same castable-land-back MdfcState shape.
/// - <see cref="WrathOfTheSkiesFactory"/> — destroy-each-nonland-permanent
///   resolve body (this card is the X = ∞ / unconditional case).
/// </summary>
[CardName("Ondu Inversion")]
public static class OnduInversionFactory
{
    public const string CardName = "Ondu Inversion";
    public const string BackName = "Ondu Skyruins";
    public const string Slug = "ondu-inversion";

    /// <summary>
    /// Construct Ondu Inversion as a Sorcery (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached, carrying a castable
    /// land back face (Ondu Skyruins). The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at play time and materializes a fresh
        // back-face land instance (wired to its ETB "enters tapped"
        // replacement via the supplied ReplacementBus) when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                OnduSkyruinsFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build Ondu Inversion's resolve effect — destroy each nonland permanent
    /// on every supplied player's battlefield. Each victim is routed to its
    /// owner's graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/>
    /// (CR 701.7). Battlefields are snapshotted up front because
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> mutates the source zone
    /// in place.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c> for
    /// a controller-only sweep.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName}: destroy all nonland permanents.", () =>
            {
                foreach (var pl in allPlayers)
                {
                    // Snapshot — MoveToGraveyard mutates the source
                    // battlefield in place.
                    var nonland = pl.Zones.Battlefield.GetCards()
                        .OfType<Card>()
                        .Where(c => !c.HasType(CardType.Land))
                        .ToList();

                    foreach (var c in nonland)
                    {
                        // "Destroy all nonland permanents." — plain Destroy,
                        // no "can't be regenerated" rider. Indestructible
                        // (CR 702.12) / regeneration (CR 701.15) honoured.
                        OracleSpellBinder.MoveToGraveyard(
                            c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Ondu Inversion uses on
    /// resolution. No modes, no X, no <see cref="TargetRequest"/>s — the
    /// printed effect is an untargeted board wipe (CR 701.7 + CR 109.5 —
    /// "all [things]").
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// scans. Typically <c>Game.Players</c>.</param>
    public static SpellDefinition BuildSpellDefinition(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(allPlayers));
    }
}
