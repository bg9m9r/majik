using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.Cards.MultiFace;

/// <summary>
/// Carrier object passed into <see cref="IFaceTransform.Apply"/> /
/// <see cref="IFaceTransform.Revert"/> giving the transform plug-in
/// the engine services it needs to perform zone moves, attach
/// abilities, emit events, etc.
///
/// All fields are nullable so test fixtures can construct a minimal
/// context with just the pieces they exercise.
/// </summary>
public sealed record FaceContext(
    Majik.Core.Domain.Aggregates.Game? Game = null,
    ZoneService? ZoneService = null,
    Player? ActingPlayer = null);
