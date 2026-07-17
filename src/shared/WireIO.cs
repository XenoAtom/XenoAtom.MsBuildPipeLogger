// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Low-level primitives for the XenoAtom pipe wire format, shared by the logger (writer) and the
/// receiver (reader). The format is deliberately simple and self-describing so it stays independent
/// of the MSBuild binary-log format and therefore of the MSBuild version on either side.
/// </summary>
internal static class WireIO
{
    /// <summary>Writes a non-negative integer using a 7-bit variable-length encoding.</summary>
    public static void Write7Bit(this BinaryWriter writer, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            writer.Write((byte)(v | 0x80));
            v >>= 7;
        }

        writer.Write((byte)v);
    }

    /// <summary>Reads a 7-bit variable-length encoded integer written by <see cref="Write7Bit"/>.</summary>
    public static int Read7Bit(this BinaryReader reader)
    {
        var result = 0;
        var shift = 0;
        while (shift < 35)
        {
            var b = reader.ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new FormatException("Malformed 7-bit encoded integer on the pipe stream.");
    }

    /// <summary>Writes a possibly-<see langword="null"/> string as a presence flag followed by the value.</summary>
    public static void WriteNullable(this BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(value);
        }
    }

    /// <summary>Reads a string written by <see cref="WriteNullable(BinaryWriter,string)"/>.</summary>
    public static string? ReadNullableString(this BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadString() : null;

    /// <summary>Writes a <see cref="DateTime"/> preserving its <see cref="DateTimeKind"/>.</summary>
    public static void WriteDateTime(this BinaryWriter writer, DateTime value)
    {
        writer.Write(value.Ticks);
        writer.Write((byte)value.Kind);
    }

    /// <summary>Reads a <see cref="DateTime"/> written by <see cref="WriteDateTime"/>.</summary>
    public static DateTime ReadDateTime(this BinaryReader reader)
    {
        var ticks = reader.ReadInt64();
        var kind = (DateTimeKind)reader.ReadByte();
        return new DateTime(ticks, kind);
    }
}
