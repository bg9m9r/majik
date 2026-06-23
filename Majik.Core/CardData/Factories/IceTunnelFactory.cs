using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ice Tunnel (Kaldheim U/B snow enters-tapped dual).
/// Oracle text (verified against Scryfall 2026-06-23):
///   "({T}: Add {U} or {B}.)
///    This land enters tapped."
/// Type line: "Snow Land — Island Swamp".
///
/// <para>
/// The blue/black member of the Kaldheim snow-dual cycle. Unlike the Coldsnap
/// snow taplands (e.g. <see cref="ArcticFlatsFactory"/>) it carries the
/// <em>basic land subtypes</em> Island + Swamp (CR 205.3i) in addition to the
/// <see cref="CardSupertype.Snow"/> supertype (CR 205.4d). The reminder-text
/// "{T}: Add {U} or {B}" comes from those intrinsic basic-land mana abilities
/// (CR 305.6 / 605.1 — mana abilities don't use the stack); both colours, the
/// Snow supertype, and the Island/Swamp subtypes are declared declaratively in
/// <c>Majik.Core/CardData/Cards/ice-tunnel.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring <see cref="ArcticFlatsFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
/// registration (no bus available) — same posture as
/// <see cref="ArcticFlatsFactory"/>.
/// </para>
/// </summary>
[CardName("Ice Tunnel")]
public static class IceTunnelFactory
{
    public const string Slug = "ice-tunnel";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Ice Tunnel owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Ice Tunnel with optional replacement-bus wiring so
    /// the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as ArcticFlatsFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
