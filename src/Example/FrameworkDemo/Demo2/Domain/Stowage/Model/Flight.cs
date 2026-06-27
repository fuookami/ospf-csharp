#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 航班。Flight information for stowage planning.
/// </summary>
/// <param name="FlightNo">航班号 / Flight number</param>
/// <param name="Source">始发站 / Source station</param>
/// <param name="Destination">目的站 / Destination station</param>
public sealed record Flight(
    Infrastructure.FlightNo FlightNo,
    Infrastructure.IATACode Source,
    Infrastructure.IATACode Destination);
