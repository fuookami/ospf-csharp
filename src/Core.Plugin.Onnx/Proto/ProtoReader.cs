#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Proto;

/// <summary>
/// **最小的 protobuf 线格式读取器** / A minimal protobuf wire-format reader.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 时的一个**有意的不同选择**。基线用 `onnx-protobuf_2.13` 把 `onnx.proto` 生成成 Kotlin 类；
/// C# 侧没有现成的 ONNX 模型包，两个选项是：
///
/// **① 引用 `Google.Protobuf` 并从 `onnx.proto` 生成 C# 类**——**约三万行生成代码**，而本插件只读十来种字段；
/// **② 手写一个只覆盖所需字段的线格式读取器**——**约两百行，零代码生成**，风险集中在"字段号写错"。
///
/// **选 ②，理由是风险形状：** **字段号写错恰好是可以被判据钉死的那一类**（用手写字节数组按规范编码、逐字段断言），
/// **而三万行生成代码的负担是长期的、且无法用判据证明"我们没有读错"**——它只证明"生成器跑过了"。
///
/// **A deliberate divergence from the baseline.** The baseline generates Kotlin classes from `onnx.proto` via `onnx-protobuf_2.13`; on the C# side there is no
/// ready-made ONNX model package, leaving two options: generate C# classes from `onnx.proto` (~30k generated lines for a plugin that reads a dozen fields), or
/// hand-write a wire reader covering only the needed fields (~200 lines, no codegen) whose risk concentrates in "the field numbers are right".
///
/// **Option two wins on the shape of the risk: a wrong field number is exactly what a criterion can pin (encode by hand per the specification and assert field by
/// field), while 30k generated lines are a permanent burden that no criterion can prove we read correctly — it only proves the generator ran.**
/// </remarks>
public sealed class ProtoReader
{
    private readonly byte[] _buffer;
    private int _position;

    /// <summary>构造读取器 / Construct the reader.</summary>
    /// <param name="buffer">字节 / The bytes.</param>
    public ProtoReader(byte[] buffer) {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>是否读完 / Whether the input is exhausted.</summary>
    public bool Eof => _position >= _buffer.Length;

    /// <summary>
    /// 读下一个字段号与线类型 / Read the next field number and wire type.
    /// </summary>
    /// <param name="fieldNumber">字段号 / The field number.</param>
    /// <param name="wireType">线类型 / The wire type.</param>
    /// <returns>是否读到字段 / Whether a field was read.</returns>
    public bool TryReadTag(out int fieldNumber, out int wireType) {
        if (Eof) {
            fieldNumber = 0;
            wireType = 0;
            return false;
        }
        ulong tag = ReadVarint();
        wireType = (int)(tag & 0x7);
        fieldNumber = (int)(tag >> 3);
        return true;
    }

    /// <summary>读变长整数 / Read a varint.</summary>
    /// <returns>值 / The value.</returns>
    public ulong ReadVarint() {
        ulong result = 0;
        int shift = 0;
        while (true) {
            if (_position >= _buffer.Length) {
                throw new FormatException("protobuf 变长整数被截断 / truncated varint");
            }
            byte current = _buffer[_position++];
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) {
                return result;
            }
            shift += 7;
            if (shift >= 64) {
                throw new FormatException("protobuf 变长整数过长 / varint longer than 64 bits");
            }
        }
    }

    /// <summary>读 32 位定长 / Read a fixed 32-bit value.</summary>
    /// <returns>值 / The value.</returns>
    public uint ReadFixed32() {
        EnsureRemaining(4);
        uint value = (uint)(_buffer[_position] | (_buffer[_position + 1] << 8) | (_buffer[_position + 2] << 16) | (_buffer[_position + 3] << 24));
        _position += 4;
        return value;
    }

    /// <summary>读 64 位定长 / Read a fixed 64-bit value.</summary>
    /// <returns>值 / The value.</returns>
    public ulong ReadFixed64() {
        EnsureRemaining(8);
        ulong value = 0;
        for (int index = 0; index < 8; ++index) {
            value |= (ulong)_buffer[_position + index] << (8 * index);
        }
        _position += 8;
        return value;
    }

    /// <summary>读长度前缀字节 / Read a length-delimited payload.</summary>
    /// <returns>字节 / The bytes.</returns>
    public byte[] ReadBytes() {
        int length = (int)ReadVarint();
        EnsureRemaining(length);
        byte[] value = new byte[length];
        System.Array.Copy(_buffer, _position, value, 0, length);
        _position += length;
        return value;
    }

    /// <summary>读长度前缀字符串 / Read a length-delimited string.</summary>
    /// <returns>字符串 / The string.</returns>
    public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

    /// <summary>读打包的定长 32 位数组 / Read a packed fixed-32 array.</summary>
    /// <returns>值 / The values.</returns>
    /// <remarks>
    /// **`float_data` 是 packed fixed32**——**按单个定长读会把整段偏移算错，而那种偏移错误在数值上表现为"还算合理的浮点数"，**
    /// 不会抛异常。判据用一段已知浮点数组钉住这一点。
    ///
    /// **`float_data` is packed fixed32: reading it as individual fixed values shifts the whole segment, and that shift shows up as "plausible floats"
    /// rather than an exception.** A criterion pins it with a known array.
    /// </remarks>
    public List<float> ReadPackedFloat() {
        byte[] payload = ReadBytes();
        var values = new List<float>(payload.Length / 4);
        for (int offset = 0; offset + 4 <= payload.Length; offset += 4) {
            uint bits = (uint)(payload[offset] | (payload[offset + 1] << 8) | (payload[offset + 2] << 16) | (payload[offset + 3] << 24));
            values.Add(BitConverter.Int32BitsToSingle((int)bits));
        }
        return values;
    }

    /// <summary>读打包的变长整数数组 / Read a packed varint array.</summary>
    /// <returns>值 / The values.</returns>
    public List<long> ReadPackedVarint() {
        byte[] payload = ReadBytes();
        var values = new List<long>();
        int position = 0;
        while (position < payload.Length) {
            ulong result = 0;
            int shift = 0;
            while (true) {
                byte current = payload[position++];
                result |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0) {
                    break;
                }
                shift += 7;
            }
            values.Add((long)result);
        }
        return values;
    }

    /// <summary>跳过未知字段 / Skip an unknown field.</summary>
    /// <param name="wireType">线类型 / The wire type.</param>
    public void SkipField(int wireType) {
        switch (wireType) {
            case 0:
                ReadVarint();
                break;
            case 1:
                _position += 8;
                EnsureRemaining(0);
                break;
            case 2:
                ReadBytes();
                break;
            case 5:
                _position += 4;
                EnsureRemaining(0);
                break;
            default:
                throw new FormatException("未知的 protobuf 线类型 / unknown wire type: " + wireType);
        }
    }

    private void EnsureRemaining(int needed) {
        if (_position + needed > _buffer.Length) {
            throw new FormatException("protobuf 载荷被截断 / truncated payload: need " + needed + ", have " + (_buffer.Length - _position));
        }
    }
}