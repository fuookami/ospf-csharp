#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
namespace Fuookami.Ospf.Core.Plugin.Scip;

internal sealed class ScipNative : IDisposable {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Version();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Create(ref IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Call(IntPtr scip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int NamedCall(IntPtr scip, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetInt(IntPtr scip, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetReal(IntPtr scip, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetBool(IntPtr scip, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetLongint(IntPtr scip, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetNamedStrings(IntPtr scip, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetSetting(IntPtr scip, int setting, int quiet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetSense(IntPtr scip, int sense);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Offset(IntPtr scip, double offset);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateVar(IntPtr scip, ref IntPtr variable, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double lower, double upper, double objective, int type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Add(IntPtr scip, IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Release(IntPtr scip, ref IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateConstraint(IntPtr scip, ref IntPtr constraint, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int count, IntPtr[] variables, double[] coefficients, double lower, double upper);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateQConstraint(
        IntPtr scip,
        ref IntPtr constraint,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int linearCount,
        IntPtr[] linearVariables,
        double[] linearCoefficients,
        int quadraticCount,
        IntPtr[] quadraticVariables1,
        IntPtr[] quadraticVariables2,
        double[] quadraticCoefficients,
        double lower,
        double upper);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Quiet(IntPtr scip, int quiet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double Real(IntPtr scip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long Long(IntPtr scip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Status(IntPtr scip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr BestSolution(IntPtr scip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double SolutionValue(IntPtr scip, IntPtr solution, IntPtr variable);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double SolutionObjective(IntPtr scip, IntPtr solution);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DualSolVal(IntPtr scip, IntPtr cons, out double dualsolval, out int boundconstraint);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int TransformedCons(IntPtr scip, IntPtr cons, out IntPtr transcons);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double DualfarkasLinear(IntPtr scip, IntPtr cons);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateSolution(IntPtr scip, ref IntPtr solution, IntPtr heuristic);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetSolutionValue(IntPtr scip, IntPtr solution, IntPtr variable, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int AddSolutionFree(IntPtr scip, ref IntPtr solution, out int stored);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int EventExecution(IntPtr scip, IntPtr handler, IntPtr eventPointer, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int EventLifecycle(IntPtr scip, IntPtr handler);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int IncludeEventHandler(IntPtr scip, ref IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string description, EventExecution execute, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetEventLifecycle(IntPtr scip, IntPtr handler, EventLifecycle callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CatchEvent(IntPtr scip, ulong eventMask, IntPtr handler, IntPtr data, IntPtr filterPosition);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DropEvent(IntPtr scip, ulong eventMask, IntPtr handler, IntPtr data, int filterPosition);

    private readonly IntPtr _library;
    internal readonly Version Major;
    internal readonly Version Minor;
    internal readonly Version Patch;
    internal readonly Create CreateScip;
    internal readonly Create FreeScip;
    internal readonly Call IncludePlugins;
    internal readonly NamedCall CreateProblem;
    internal readonly SetInt SetIntParameter;
    internal readonly SetReal SetRealParameter;
    internal readonly SetBool SetBoolParameter;
    internal readonly SetLongint SetLongintParameter;
    internal readonly SetNamedStrings SetStringParameter;
    internal readonly SetSetting SetPresolving;
    internal readonly SetSense SetObjectiveSense;
    internal readonly Offset AddObjectiveOffset;
    internal readonly CreateVar CreateVariable;
    internal readonly Add AddVariable;
    internal readonly Release ReleaseVariable;
    internal readonly CreateConstraint CreateLinearConstraint;
    internal readonly CreateQConstraint CreateQuadraticConstraint;
    internal readonly Add AddConstraint;
    internal readonly Release ReleaseConstraint;
    internal readonly Quiet SetQuiet;
    internal readonly Real Infinity;
    internal readonly Call Solve;
    internal readonly Status GetStatus;
    internal readonly BestSolution GetBestSolution;
    internal readonly Status GetSolutionCount;
    internal readonly BestSolution GetSolutions;
    internal readonly SolutionValue GetSolutionValue;
    internal readonly SolutionObjective GetSolutionObjective;
    internal readonly CreateSolution CreateFullSolution;
    internal readonly CreateSolution CreatePartialSolution;
    internal readonly SetSolutionValue SetInitialValue;
    internal readonly AddSolutionFree AddInitialSolution;
    internal readonly Release FreeSolution;
    internal readonly Real GetDualBound;
    /// <summary>约束对偶取值 / Constraint dual solution value reader.</summary>
    internal readonly DualSolVal GetDualSolVal;
    /// <summary>原始约束到转换约束的映射 / Original to transformed constraint mapping.</summary>
    internal readonly TransformedCons GetTransformedCons;
    /// <summary>转换约束的 Farkas 乘子 / Farkas multiplier of a transformed constraint.</summary>
    internal readonly DualfarkasLinear GetDualfarkasLinear;
    internal readonly SetSetting SetHeuristics;
    internal readonly Real GetPrimalBound;
    internal readonly Real GetGap;
    internal readonly Real GetSolvingTime;
    internal readonly Long GetNodes;
    internal readonly Long GetIterations;
    internal readonly Status GetStage;
    internal readonly Call InterruptSolve;
    internal readonly IncludeEventHandler IncludeEvent;
    internal readonly SetEventLifecycle SetEventInit;
    internal readonly SetEventLifecycle SetEventExit;
    internal readonly CatchEvent CatchGlobalEvent;
    internal readonly DropEvent DropGlobalEvent;

    private ScipNative(IntPtr library) {
        _library = library;
        Major = Bind<Version>("SCIPmajorVersion");
        Minor = Bind<Version>("SCIPminorVersion");
        Patch = Bind<Version>("SCIPtechVersion");
        CreateScip = Bind<Create>("SCIPcreate");
        FreeScip = Bind<Create>("SCIPfree");
        IncludePlugins = Bind<Call>("SCIPincludeDefaultPlugins");
        CreateProblem = Bind<NamedCall>("SCIPcreateProbBasic");
        SetIntParameter = Bind<SetInt>("SCIPsetIntParam");
        SetRealParameter = Bind<SetReal>("SCIPsetRealParam");
        SetBoolParameter = Bind<SetBool>("SCIPsetBoolParam");
        SetLongintParameter = Bind<SetLongint>("SCIPsetLongintParam");
        SetStringParameter = Bind<SetNamedStrings>("SCIPsetStringParam");
        SetPresolving = Bind<SetSetting>("SCIPsetPresolving");
        SetObjectiveSense = Bind<SetSense>("SCIPsetObjsense");
        AddObjectiveOffset = Bind<Offset>("SCIPaddOrigObjoffset");
        CreateVariable = Bind<CreateVar>("SCIPcreateVarBasic");
        AddVariable = Bind<Add>("SCIPaddVar");
        ReleaseVariable = Bind<Release>("SCIPreleaseVar");
        CreateLinearConstraint = Bind<CreateConstraint>("SCIPcreateConsBasicLinear");
        CreateQuadraticConstraint = Bind<CreateQConstraint>("SCIPcreateConsBasicQuadratic");
        AddConstraint = Bind<Add>("SCIPaddCons");
        ReleaseConstraint = Bind<Release>("SCIPreleaseCons");
        SetQuiet = Bind<Quiet>("SCIPsetMessagehdlrQuiet");
        Infinity = Bind<Real>("SCIPinfinity");
        Solve = Bind<Call>("SCIPsolve");
        GetStatus = Bind<Status>("SCIPgetStatus");
        GetBestSolution = Bind<BestSolution>("SCIPgetBestSol");
        GetSolutionCount = Bind<Status>("SCIPgetNSols");
        GetSolutions = Bind<BestSolution>("SCIPgetSols");
        GetSolutionValue = Bind<SolutionValue>("SCIPgetSolVal");
        GetSolutionObjective = Bind<SolutionObjective>("SCIPgetSolOrigObj");
        CreateFullSolution = Bind<CreateSolution>("SCIPcreateSol");
        CreatePartialSolution = Bind<CreateSolution>("SCIPcreatePartialSol");
        SetInitialValue = Bind<SetSolutionValue>("SCIPsetSolVal");
        AddInitialSolution = Bind<AddSolutionFree>("SCIPaddSolFree");
        FreeSolution = Bind<Release>("SCIPfreeSol");
        GetDualBound = Bind<Real>("SCIPgetDualbound");
        GetDualSolVal = Bind<DualSolVal>("SCIPgetDualSolVal");
        GetTransformedCons = Bind<TransformedCons>("SCIPgetTransformedCons");
        GetDualfarkasLinear = Bind<DualfarkasLinear>("SCIPgetDualfarkasLinear");
        SetHeuristics = Bind<SetSetting>("SCIPsetHeuristics");
        GetPrimalBound = Bind<Real>("SCIPgetPrimalbound");
        GetGap = Bind<Real>("SCIPgetGap");
        GetSolvingTime = Bind<Real>("SCIPgetSolvingTime");
        GetNodes = Bind<Long>("SCIPgetNNodes");
        GetIterations = Bind<Long>("SCIPgetNLPIterations");
        GetStage = Bind<Status>("SCIPgetStage");
        InterruptSolve = Bind<Call>("SCIPinterruptSolve");
        IncludeEvent = Bind<IncludeEventHandler>("SCIPincludeEventhdlrBasic");
        SetEventInit = Bind<SetEventLifecycle>("SCIPsetEventhdlrInit");
        SetEventExit = Bind<SetEventLifecycle>("SCIPsetEventhdlrExit");
        CatchGlobalEvent = Bind<CatchEvent>("SCIPcatchEvent");
        DropGlobalEvent = Bind<DropEvent>("SCIPdropEvent");
    }

    internal static Result<ScipNative, ErrorCode, Error<ErrorCode>> Load() {
        string file = OperatingSystem.IsWindows() ? "libscip.dll" : OperatingSystem.IsMacOS() ? "libscip.dylib" : "libscip.so";
        string? home = Environment.GetEnvironmentVariable("SCIP_HOME");
        string? homePath = string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, OperatingSystem.IsWindows() ? "bin" : "lib", file);
        return Load(homePath, file);
    }

    internal static Result<ScipNative, ErrorCode, Error<ErrorCode>> Load(string? homePath, string fallback) {
        IntPtr library = IntPtr.Zero;
        try {
            if ((homePath is null || !NativeLibrary.TryLoad(homePath, out library))
                && !NativeLibrary.TryLoad(fallback, out library)) {
                return Failure("未找到 SCIP 原生库 / SCIP native library was not found");
            }
            var native = new ScipNative(library);
            if (native.Major() != 9 || native.Minor() != 2) {
                native.Dispose();
                return Failure("仅支持 SCIP 9.2 原生 ABI / Only the SCIP 9.2 native ABI is supported");
            }
            return Results.Ok(native);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            if (library != IntPtr.Zero) {
                NativeLibrary.Free(library);
            }
            return Failure($"无法加载 SCIP 原生库：{error.Message} / Cannot load the SCIP native library: {error.Message}");
        }
    }

    internal T Bind<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private static Result<ScipNative, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<ScipNative>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, message));

    public void Dispose() => NativeLibrary.Free(_library);
}
