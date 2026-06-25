#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism
{
    /// <summary>
    /// 机制模型转储状态 / Mechanism model dumping status.
    /// </summary>
    public sealed record MechanismModelDumpingStatus(
        string Stage,
        MathUInt64 Ready,
        MathUInt64 Total)
    {
        public static MechanismModelDumpingStatus DumpingConstraints(MathUInt64 ready, MathUInt64 total) =>
            new("DumpingConstraints", ready, total);

        public static MechanismModelDumpingStatus DumpingSymbols(MathUInt64 ready, MathUInt64 total) =>
            new("DumpingSymbols", ready, total);
    }

    /// <summary>
    /// 机制模型转储状态回调委托 / Mechanism model dumping status callback delegate.
    /// </summary>
    public delegate Try MechanismModelDumpingStatusCallBack(MechanismModelDumpingStatus status);
}
