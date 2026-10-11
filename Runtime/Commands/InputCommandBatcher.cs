using System;

namespace Dik.Input
{
    /// <summary>
    /// Collects commands over a frame so a network bridge can send one packet instead of one RPC per event.
    /// Use from a bridge: Add() in SendToServer, TryFlush() once per frame or network tick.
    /// </summary>
    public sealed class InputCommandBatcher
    {
        byte[] m_Buffer;
        int m_Length;

        public InputCommandBatcher(int initialCapacity = 256)
        {
            m_Buffer = new byte[Math.Max(initialCapacity, InputCommandSerializer.MaxSize)];
        }

        public int Length => m_Length;
        public bool IsEmpty => m_Length == 0;

        public void Add(in InputCommand command)
        {
            var size = InputCommandSerializer.GetSize(command.ValueKind);
            if (m_Length + size > m_Buffer.Length)
                Array.Resize(ref m_Buffer, Math.Max(m_Buffer.Length * 2, m_Length + size));
            m_Length += InputCommandSerializer.Write(command, m_Buffer, m_Length);
        }

        /// <summary>
        /// Returns the pending bytes and clears the batch. The segment is valid until the next Add.
        /// </summary>
        public bool TryFlush(out ArraySegment<byte> packet)
        {
            if (m_Length == 0)
            {
                packet = default;
                return false;
            }
            packet = new ArraySegment<byte>(m_Buffer, 0, m_Length);
            m_Length = 0;
            return true;
        }

        public void Clear() => m_Length = 0;
    }
}
