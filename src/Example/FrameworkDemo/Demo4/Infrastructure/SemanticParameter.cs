#nullable enable

using System;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;

/// <summary>
/// 包装 3 字符 IATA 机场代码的值类型。
/// Value type wrapping a 3-character IATA airport code.
/// </summary>
public readonly record struct IATA {
    /// <summary>IATA 代码 / The IATA code.</summary>
    public string Code { get; }

    /// <summary>构造函数，验证代码长度。/ Constructor that validates code length.</summary>
    public IATA(string code) {
        if (code.Length != 3) throw new ArgumentException("IATA code must be exactly 3 characters.", nameof(code));
        Code = code;
    }

    /// <inheritdoc/>
    public override string ToString() => Code;
}

/// <summary>
/// 包装 4 字符 ICAO 机场代码的值类型。
/// Value type wrapping a 4-character ICAO airport code.
/// </summary>
public readonly record struct ICAO {
    /// <summary>ICAO 代码 / The ICAO code.</summary>
    public string Code { get; }

    /// <summary>构造函数，验证代码长度。/ Constructor that validates code length.</summary>
    public ICAO(string code) {
        if (code.Length != 4) throw new ArgumentException("ICAO code must be exactly 4 characters.", nameof(code));
        Code = code;
    }

    /// <inheritdoc/>
    public override string ToString() => Code;
}

/// <summary>
/// 包装飞机类型名称的值类型。
/// Value type wrapping an aircraft type name.
/// </summary>
public readonly record struct AircraftTypeName(string Name) {
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// 包装飞机类型代码的值类型。
/// Value type wrapping an aircraft type code.
/// </summary>
public readonly record struct AircraftTypeCode(string Code) {
    /// <inheritdoc/>
    public override string ToString() => Code;
}

/// <summary>
/// 包装飞机子类型名称的值类型。
/// Value type wrapping an aircraft minor type name.
/// </summary>
public readonly record struct AircraftMinorTypeName(string Name) {
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// 包装飞机子类型代码的值类型。
/// Value type wrapping an aircraft minor type code.
/// </summary>
public readonly record struct AircraftMinorTypeCode(string Code) {
    /// <inheritdoc/>
    public override string ToString() => Code;
}

/// <summary>
/// 包装机翼飞机类型代码的值类型。
/// Value type wrapping a wing aircraft type code.
/// </summary>
public readonly record struct WingAircraftTypeCode(string Code) {
    /// <inheritdoc/>
    public override string ToString() => Code;
}

/// <summary>
/// 包装飞机注册号的值类型。
/// Value type wrapping an aircraft register number.
/// </summary>
public readonly record struct AircraftRegisterNumber(string No) {
    /// <inheritdoc/>
    public override string ToString() => No;
}

/// <summary>
/// 包装舱位标识符的值类型。
/// Value type wrapping a passenger class identifier.
/// </summary>
public readonly record struct PassengerClassId(string Cls) {
    /// <inheritdoc/>
    public override string ToString() => Cls;
}

/// <summary>
/// 包装飞行员职级编号的值类型。
/// Value type wrapping a pilot rank number.
/// </summary>
public readonly record struct PilotRankNo(string No) {
    /// <inheritdoc/>
    public override string ToString() => No;
}

/// <summary>
/// 包装飞行员代码的值类型。
/// Value type wrapping a pilot code.
/// </summary>
public readonly record struct PilotCode(string Code) {
    /// <inheritdoc/>
    public override string ToString() => Code;
}

/// <summary>
/// 包装机组成员职级编号的值类型。
/// Value type wrapping a crew member rank number.
/// </summary>
public readonly record struct CrewManRankNo(string No) {
    /// <inheritdoc/>
    public override string ToString() => No;
}

/// <summary>
/// 包装工号的值类型。
/// Value type wrapping a worker number.
/// </summary>
public readonly record struct WorkerNo(string No) {
    /// <inheritdoc/>
    public override string ToString() => No;
}
