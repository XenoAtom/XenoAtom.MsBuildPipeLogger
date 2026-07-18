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
    /// <summary>
    /// Upper bound for any length prefix read from the pipe (records and base headers). Generous —
    /// real records are far smaller — but prevents a corrupt or hostile stream from triggering a
    /// near-2 GiB allocation. 128 MiB.
    /// </summary>
    public const int MaxFrameLength = 128 * 1024 * 1024;

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
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = reader.ReadByte();
            if (shift == 28 && (b & 0xF0) != 0)
            {
                // The 5th byte can only carry bits 28..31: a continuation bit or any bit beyond
                // bit 31 cannot come from Write7Bit and would silently produce a garbage value.
                break;
            }

            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
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

    /// <summary>Writes a <see cref="DateTime"/> preserving its <see cref="DateTimeKind"/>.</summary>
    public static void WriteDateTime(this BinaryWriter writer, DateTime value)
    {
        writer.Write(value.Ticks);
        writer.Write((byte)value.Kind);
    }
}
