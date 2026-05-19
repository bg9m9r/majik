using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Engine-internal mutation helpers for <see cref="ICard"/>. External
/// consumers never reach these — the public <see cref="ICard"/> surface
/// is read-only, and direct mutation goes through the engine's services
/// (e.g. <see cref="Services.ZoneService"/>).
/// </summary>
internal static class CardInternalExtensions
{
    public static void SetZone(this ICard card, ZoneType zone)
    {
        ((Card)card).Zone = zone;
    }

    public static void SetOwner(this ICard card, Player? owner)
    {
        ((Card)card).Owner = owner;
    }

    public static void SetController(this ICard card, Player? controller)
    {
        ((Card)card).Controller = controller;
    }
}
