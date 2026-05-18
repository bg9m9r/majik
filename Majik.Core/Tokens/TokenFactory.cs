using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Tokens;

/// <summary>
/// CR 111 — builds token creatures on the battlefield. The returned card
/// is marked <see cref="Permanent.IsToken"/> so SBA 704.5d removes it from
/// any zone other than the battlefield.
/// </summary>
public static class TokenFactory
{
    public sealed record TokenSpec(
        string Name,
        int Power,
        int Toughness,
        IReadOnlyList<CardSubtype>? Subtypes = null,
        IReadOnlyList<string>? Keywords = null);

    /// <summary>Create a creature token and put it onto the battlefield under
    /// the given controller. Uses <see cref="ZoneService"/> when supplied so
    /// CardMovedEvent fires (triggers Soul Warden etc.).</summary>
    public static Creature CreateOnBattlefield(
        TokenSpec spec,
        Player controller,
        ZoneService? zones = null)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var token = new Creature(
            spec.Name, manaCost: "",
            power: spec.Power, toughness: spec.Toughness,
            subtypes: spec.Subtypes ?? Array.Empty<CardSubtype>())
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
            HasSummoningSickness = true,
        };

        foreach (var kw in spec.Keywords ?? Array.Empty<string>())
        {
            token.AddAbility(new KeywordAbility(kw, token, controller));
        }

        // Tokens enter the battlefield directly (CR 111.6) — not from the library.
        token.Zone = ZoneType.Library; // sentinel for ZoneService.MoveCard's from-check
        controller.Zones.Library.AddCard(token);

        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.Zone = ZoneType.Battlefield;
            controller.Zones.Battlefield.AddCard(token);
        }

        return token;
    }
}
