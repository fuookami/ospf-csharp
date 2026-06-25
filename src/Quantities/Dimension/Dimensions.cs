#nullable enable

namespace Fuookami.Ospf.Quantities.Dimension;
/// <summary>
/// 预定义物理量纲 / Predefined physical dimensions.
/// </summary>
public static class Dimensions {
    // ============================================================================
    // 基本物理量 / Fundamental Quantities
    // ============================================================================

    /// <summary>长度 / Length.</summary>
    public static readonly DerivedQuantity Length = new(Dims.L, "length", "L");
    /// <summary>质量 / Mass.</summary>
    public static readonly DerivedQuantity Mass = new(Dims.M, "mass", "m");
    /// <summary>时间 / Time.</summary>
    public static readonly DerivedQuantity Time = new(Dims.T, "time", "t");
    /// <summary>电流 / Current.</summary>
    public static readonly DerivedQuantity Current = new(Dims.I, "current", "I");
    /// <summary>温度 / Temperature.</summary>
    public static readonly DerivedQuantity Temperature = new(Dims.Theta, "temperature", "T");
    /// <summary>物质的量 / Amount of substance.</summary>
    public static readonly DerivedQuantity AmountOfSubstance = new(Dims.N, "amount of substance", "N");
    /// <summary>发光强度 / Luminous intensity.</summary>
    public static readonly DerivedQuantity LuminousIntensity = new(Dims.J, "luminous intensity", "l");
    /// <summary>平面角 / Plane angle.</summary>
    public static readonly DerivedQuantity PlaneAngle = new(Dims.rad, "plane angle", "rad");
    /// <summary>立体角 / Solid angle.</summary>
    public static readonly DerivedQuantity SolidAngle = new(Dims.sr, "solid angle", "sr");
    /// <summary>信息 / Information.</summary>
    public static readonly DerivedQuantity Information = new(Dims.B, "information", "i", QuantityDomain.Discrete);

    // ============================================================================
    // 基本导出量 / Basic Derived Quantities
    // ============================================================================

    /// <summary>面积 / Area (S = L^2).</summary>
    public static readonly DerivedQuantity Area = new(Length * Length, "area", "S");
    /// <summary>体积 / Volume (V = L^3).</summary>
    public static readonly DerivedQuantity Volume = new(Area * Length, "volume", "V");
    /// <summary>频率 / Frequency (f = t^-1).</summary>
    public static readonly DerivedQuantity Frequency = new(Time.Negate(), "frequency", "f");

    // ============================================================================
    // 力学量 / Mechanics
    // ============================================================================

    /// <summary>速度 / Velocity (v = L/t).</summary>
    public static readonly DerivedQuantity Velocity = new(Length / Time, "velocity", "v");
    /// <summary>角速度 / Angular velocity (w = rad/t).</summary>
    public static readonly DerivedQuantity AngularVelocity = new(PlaneAngle / Time, "angular velocity", "w");
    /// <summary>波数 / Wave number (k = L^-1).</summary>
    public static readonly DerivedQuantity WaveNumber = new(Length * -1, "wave number", "k");
    /// <summary>加速度 / Acceleration (a = v/t).</summary>
    public static readonly DerivedQuantity Acceleration = new(Velocity / Time, "acceleration", "a");
    /// <summary>角加速度 / Angular acceleration (alpha = w/t).</summary>
    public static readonly DerivedQuantity AngularAcceleration = new(AngularVelocity / Time, "angular acceleration", null);
    /// <summary>动量 / Momentum (P = mv).</summary>
    public static readonly DerivedQuantity Momentum = new(Mass * Velocity, "momentum", "M");
    /// <summary>角动量 / Angular momentum.</summary>
    public static readonly DerivedQuantity AngularMomentum = new(Mass * AngularVelocity, "angular momentum", null);
    /// <summary>转动惯量 / Moment of inertia (I = mL^2).</summary>
    public static readonly DerivedQuantity MomentOfInertia = new(Mass * (Length * 2), "moment of inertia", "I");
    /// <summary>力 / Force (F = ma).</summary>
    public static readonly DerivedQuantity Force = new(Mass * Acceleration, "force", "F");
    /// <summary>压强 / Pressure (p = F/S).</summary>
    public static readonly DerivedQuantity Pressure = new(Force / Area, "pressure", "p");
    /// <summary>应力 / Stress.</summary>
    public static readonly DerivedQuantity Stress = new(Pressure, "stress", null);
    /// <summary>冲量 / Impulse (I = Ft).</summary>
    public static readonly DerivedQuantity Impulse = new(Force * Time, "impulse", null);
    /// <summary>力矩 / Torque (tau = FL).</summary>
    public static readonly DerivedQuantity Torque = new(Force * Length, "torque", null);
    /// <summary>质量密度 / Mass density (rho = m/V).</summary>
    public static readonly DerivedQuantity MassDensity = new(Mass / Volume, "mass density", null);
    /// <summary>比体积 / Specific volume (v = V/m).</summary>
    public static readonly DerivedQuantity SpecificVolume = new(Volume / Mass, "specific volume", null);
    /// <summary>能量 / Energy (E = FL).</summary>
    public static readonly DerivedQuantity Energy = new(Force * Length, "energy", "E");
    /// <summary>熵 / Entropy (S = E/theta).</summary>
    public static readonly DerivedQuantity Entropy = new(Energy / Temperature, "entropy", "S");
    /// <summary>摩尔浓度 / Molarity (c = N/V).</summary>
    public static readonly DerivedQuantity Molarity = new(AmountOfSubstance / Volume, "molarity", "c");
    /// <summary>摩尔体积 / Molar volume (Vm = V/N).</summary>
    public static readonly DerivedQuantity MolarVolume = new(Volume / AmountOfSubstance, "molar volume", null);
    /// <summary>比熵 / Specific entropy.</summary>
    public static readonly DerivedQuantity SpecificEntropy = new(Entropy / Mass, "specific entropy", null);
    /// <summary>摩尔能量 / Molar energy.</summary>
    public static readonly DerivedQuantity MolarEnergy = new(Energy / AmountOfSubstance, "molar energy", null);
    /// <summary>比能 / Specific energy.</summary>
    public static readonly DerivedQuantity SpecificEnergy = new(Energy / Mass, "specific energy", null);
    /// <summary>能量密度 / Energy density.</summary>
    public static readonly DerivedQuantity EnergyDensity = new(Energy / Volume, "energy density", null);
    /// <summary>热容 / Heat capacity.</summary>
    public static readonly DerivedQuantity HeatCapacity = new(Energy / Temperature, "heat capacity", null);
    /// <summary>表面张力 / Surface tension.</summary>
    public static readonly DerivedQuantity SurfaceTension = new(Force / Length, "surface tension", null);
    /// <summary>功率 / Power (P = Fv).</summary>
    public static readonly DerivedQuantity Power = new(Force * Velocity, "power", "P");
    /// <summary>功率密度 / Power density.</summary>
    public static readonly DerivedQuantity PowerDensity = new(Power / Area, "power density", null);
    /// <summary>热导率 / Thermal conductivity.</summary>
    public static readonly DerivedQuantity ThermalConductivity = new(Power / (Length * Temperature), "thermal conductivity", null);
    /// <summary>动力粘度 / Dynamic viscosity.</summary>
    public static readonly DerivedQuantity DynamicViscosity = new(Pressure * Time, "dynamic viscosity", null);
    /// <summary>运动粘度 / Kinematic viscosity.</summary>
    public static readonly DerivedQuantity KinematicViscosity = new(DynamicViscosity / MassDensity, "kinematic viscosity", null);
    /// <summary>摩尔质量 / Molar mass.</summary>
    public static readonly DerivedQuantity MolarMass = new(Mass / AmountOfSubstance, "molar mass", null);
    /// <summary>线密度 / Linear density.</summary>
    public static readonly DerivedQuantity LinearDensity = new(Mass / Length, "linear density", null);
    /// <summary>面密度 / Surface density.</summary>
    public static readonly DerivedQuantity SurfaceDensity = new(Mass / Area, "surface density", null);
    /// <summary>作用量 / Action.</summary>
    public static readonly DerivedQuantity Action = new(Energy * Time, "action", null);
    /// <summary>流量 / Flow rate.</summary>
    public static readonly DerivedQuantity FlowRate = new(Area * Velocity, "flow rate", null);

    // ============================================================================
    // 电磁学量 / Electromagnetism
    // ============================================================================

    /// <summary>电荷 / Electric charge (Q = It).</summary>
    public static readonly DerivedQuantity ElectricCharge = new(Current * Time, "electric charge", "Q");
    /// <summary>电流密度 / Electric current density.</summary>
    public static readonly DerivedQuantity ElectricCurrentDensity = new(Current / Area, "electric current density", null);
    /// <summary>电势 / Electric potential (U = P/I).</summary>
    public static readonly DerivedQuantity ElectricPotential = new(Power / Current, "electric potential", "U");
    /// <summary>电压 / Voltage.</summary>
    public static readonly DerivedQuantity Voltage = new(ElectricPotential, "voltage", "U");
    /// <summary>电阻 / Resistance (R = U/I).</summary>
    public static readonly DerivedQuantity Resistance = new(Voltage / Current, "resistance", "R");
    /// <summary>电导 / Conductance (G = R^-1).</summary>
    public static readonly DerivedQuantity Conductance = new(Resistance.Reciprocal(), "conductance", "G");
    /// <summary>电导率 / Conductivity.</summary>
    public static readonly DerivedQuantity Conductivity = new(Conductance / Length, "conductivity", null);
    /// <summary>电容 / Capacitance (F = Q/U).</summary>
    public static readonly DerivedQuantity Capacitance = new(ElectricCharge / Voltage, "capacitance", "F");
    /// <summary>介电常数 / Permittivity.</summary>
    public static readonly DerivedQuantity Permittivity = new(Capacitance / Length, "permittivity", null);
    /// <summary>电场强度 / Electric field strength.</summary>
    public static readonly DerivedQuantity ElectricFieldStrength = new(Voltage / Length, "electric field strength", null);
    /// <summary>电感 / Inductance.</summary>
    public static readonly DerivedQuantity Inductance = new(Voltage / Current * Time, "inductance", null);
    /// <summary>磁感应强度 / Magnetic field density.</summary>
    public static readonly DerivedQuantity MagneticFieldDensity = new(Force / (Length * Current), "magnetic field density", null);
    /// <summary>磁场强度 / Magnetic field intensity.</summary>
    public static readonly DerivedQuantity MagneticFieldIntensity = new(Current / Length, "magnetic field intensity", null);
    /// <summary>磁通量 / Magnetic flux.</summary>
    public static readonly DerivedQuantity MagneticFlux = new(MagneticFieldDensity * Area, "magnetic flux", null);
    /// <summary>磁导率 / Magnetic permeability.</summary>
    public static readonly DerivedQuantity MagneticPermeability = new(Inductance / Length, "magnetic permeability", null);

    // ============================================================================
    // 光学量 / Optics
    // ============================================================================

    /// <summary>光通量 / Luminous flux.</summary>
    public static readonly DerivedQuantity LuminousFlux = new(LuminousIntensity * SolidAngle, "luminous flux", null);
    /// <summary>照度 / Illuminance.</summary>
    public static readonly DerivedQuantity Illuminance = new(LuminousFlux / Area, "illuminance", null);
    /// <summary>亮度 / Luminance.</summary>
    public static readonly DerivedQuantity Luminance = new(LuminousIntensity / Area, "luminance", null);

    // ============================================================================
    // 放射学量 / Radiology
    // ============================================================================

    /// <summary>放射性活度 / Activity.</summary>
    public static readonly DerivedQuantity Activity = new(Time.Reciprocal(), "activity", null);
    /// <summary>吸收剂量 / Absorbed dose.</summary>
    public static readonly DerivedQuantity AbsorbedDose = new(Energy / Mass, "absorbed dose", null);

    // ============================================================================
    // 其他 / Other
    // ============================================================================

    /// <summary>催化活性 / Catalytic activity.</summary>
    public static readonly DerivedQuantity CatalyticActivity = new(AmountOfSubstance / Time, "catalytic activity", null);
    /// <summary>带宽 / Bandwidth.</summary>
    public static readonly DerivedQuantity Bandwidth = new(Information / Time, "bandwidth", "B");
}
