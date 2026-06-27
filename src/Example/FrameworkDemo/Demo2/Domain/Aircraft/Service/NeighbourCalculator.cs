#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;

/// <summary>
/// 物理邻接计算器。Physical neighbour calculator.
/// </summary>
internal static class PhysicalNeighbourCalculator
{
    public static Result<IReadOnlyList<Neighbour>, ErrorCode, Error<ErrorCode>> Calculate(IReadOnlyList<Deck> decks)
    {
        // TODO: Port from Kotlin PhysicalNeighbourCalculator
        return Results.Ok<IReadOnlyList<Neighbour>>(new List<Neighbour>());
    }
}

/// <summary>
/// 间接物理邻接计算器。Indirect physics neighbour calculator.
/// </summary>
internal static class IndirectPhysicsNeighbourCalculator
{
    public static Result<IReadOnlyList<Neighbour>, ErrorCode, Error<ErrorCode>> Calculate(
        IReadOnlyList<Deck> decks, IReadOnlyList<Neighbour> physicalNeighbours)
    {
        // TODO: Port from Kotlin IndirectPhysicsNeighbourCalculator
        return Results.Ok<IReadOnlyList<Neighbour>>(new List<Neighbour>());
    }
}
