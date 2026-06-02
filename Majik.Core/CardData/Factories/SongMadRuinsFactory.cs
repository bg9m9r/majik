using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Song-Mad Treachery // Song-Mad Ruins (Kamigawa: Neon Dynasty).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {R}."
///
/// Front face — <see cref="SongMadTreacheryFactory"/> (Sorcery {3}{R}{R} —
/// the Threaten "gain control of target creature until end of turn" spell).
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="ValakutAwakeningFactory"/> / <see cref="ValakutStoneforgeFactory"/>
/// (MDFC spell-front + tapland-back). When a player chooses to play the MDFC
/// as a land, <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Song-Mad Ruins"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {R}</b> mana ability are loaded from the
/// embedded JSON definition (<c>song-mad-ruins.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the
/// unconditional ETB-tapped replacement are attached in code (the JSON schema
/// models neither).
///
/// ## References
/// - <see cref="ValakutStoneforgeFactory"/> — identical tapland + {R} mana
///   shape (back face of the Valakut Awakening MDFC pair); this factory
///   directly mirrors it.
/// </summary>
[CardName("Song-Mad Ruins")]
public static class SongMadRuinsFactory
{
    public const string CardName = "Song-Mad Ruins";
    public const string FrontName = "Song-Mad Treachery";
    public const string Slug = "song-mad-ruins";

    /// <summary>
    /// Construct Song-Mad Ruins without a <see cref="ReplacementBus"/>. The
    /// ETB-tapped replacement is omitted; the {T}: Add {R} mana ability (from
    /// JSON) is still wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Song-Mad Ruins with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring (CR 614.1c).
    /// </summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {R} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Song-Mad Ruins is the back face that actually exists on the
        // battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ETB: "This land enters tapped." (CR 614.1c) — unconditional.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
