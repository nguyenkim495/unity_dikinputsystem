using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Dik.Input
{
    /// <summary>
    /// Compact binary format for <see cref="InputCommand"/> that works with any netcode.
    /// Layout: action(2) trigger(1) kind(1) tick(4) + 0..4 floats depending on the value kind (8 to 24 bytes).
    /// Little-endian.
    /// </summary>
    public static class InputCommandSerializer
    {
        public const int HeaderSize = 8;
        public const int MaxSize = HeaderSize + 16;

        [StructLayout(LayoutKind.Explicit)]
        struct FloatBits
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public uint UInt;
        }

        public static int GetSize(InputValueKind kind) => HeaderSize + GetFloatCount(kind) * 4;

        /// <summary>Writes the command and returns the number of bytes written.</summary>
        public static int Write(in InputCommand command, byte[] buffer, int offset)
        {
            var size = GetSize(command.ValueKind);
            if (buffer == null || offset < 0 || offset + size > buffer.Length)
                throw new ArgumentException("Buffer too small for InputCommand.");

            var p = offset;
            WriteUInt16(buffer, ref p, command.ActionIndex);
            buffer[p++] = (byte)command.Trigger;
            buffer[p++] = (byte)command.ValueKind;
            WriteUInt32(buffer, ref p, command.Tick);

            var floats = GetFloatCount(command.ValueKind);
            for (var i = 0; i < floats; i++)
                WriteFloat(buffer, ref p, command.Value[i]);

            return size;
        }

        /// <summary>Reads one command and returns the number of bytes consumed, or 0 if the data is truncated or invalid.</summary>
        public static int Read(byte[] buffer, int offset, int count, out InputCommand command)
        {
            command = default;
            if (buffer == null || count < HeaderSize || offset < 0 || offset + count > buffer.Length)
                return 0;

            var p = offset;
            command.ActionIndex = ReadUInt16(buffer, ref p);
            command.Trigger = (InputTrigger)buffer[p++];
            command.ValueKind = (InputValueKind)buffer[p++];
            command.Tick = ReadUInt32(buffer, ref p);

            if (command.ValueKind > InputValueKind.Integer)
                return 0;

            var size = GetSize(command.ValueKind);
            if (count < size)
                return 0;

            var value = Vector4.zero;
            var floats = GetFloatCount(command.ValueKind);
            for (var i = 0; i < floats; i++)
                value[i] = ReadFloat(buffer, ref p);
            command.Value = value;

            return size;
        }

        public static byte[] ToBytes(in InputCommand command)
        {
            var bytes = new byte[GetSize(command.ValueKind)];
            Write(command, bytes, 0);
            return bytes;
        }

        /// <summary>Decodes a packet holding one or more commands (e.g. from <see cref="InputCommandBatcher"/>).</summary>
        public static int ReadAll(ArraySegment<byte> packet, List<InputCommand> results)
        {
            var read = 0;
            if (packet.Array == null)
                return read;

            var p = packet.Offset;
            var end = packet.Offset + packet.Count;
            while (p < end)
            {
                var consumed = Read(packet.Array, p, end - p, out var command);
                if (consumed == 0)
                    break;
                results.Add(command);
                p += consumed;
                read++;
            }
            return read;
        }

        static int GetFloatCount(InputValueKind kind)
        {
            switch (kind)
            {
                case InputValueKind.Float:
                case InputValueKind.Integer: return 1;
                case InputValueKind.Vector2: return 2;
                case InputValueKind.Vector3: return 3;
                case InputValueKind.Quaternion: return 4;
                default: return 0;
            }
        }

        static void WriteUInt16(byte[] b, ref int p, ushort v)
        {
            b[p++] = (byte)v;
            b[p++] = (byte)(v >> 8);
        }

        static void WriteUInt32(byte[] b, ref int p, uint v)
        {
            b[p++] = (byte)v;
            b[p++] = (byte)(v >> 8);
            b[p++] = (byte)(v >> 16);
            b[p++] = (byte)(v >> 24);
        }

        static void WriteFloat(byte[] b, ref int p, float v) => WriteUInt32(b, ref p, new FloatBits { Float = v }.UInt);

        static ushort ReadUInt16(byte[] b, ref int p)
        {
            var v = (ushort)(b[p] | (b[p + 1] << 8));
            p += 2;
            return v;
        }

        static uint ReadUInt32(byte[] b, ref int p)
        {
            var v = (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
            p += 4;
            return v;
        }

        static float ReadFloat(byte[] b, ref int p) => new FloatBits { UInt = ReadUInt32(b, ref p) }.Float;
    }
}
