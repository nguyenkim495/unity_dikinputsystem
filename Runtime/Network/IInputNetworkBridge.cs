using System;
using System.Collections.Generic;

namespace Dik.Input
{
    /// <summary>
    /// Connects DikInputPlayer to your netcode (NGO, Mirror, Fish-Net, Photon, custom...).
    /// Put a component implementing this on the same GameObject as DikInputPlayer, or assign
    /// DikInputPlayer.NetworkBridge from code.
    ///
    /// Client: DikInputPlayer calls <see cref="SendToServer"/> for every Replicated command.
    /// Server: your RPC handler decodes the commands and calls DikInputReceiver.Receive on the
    /// server-side copy of the player (see <see cref="InputNetworkBridgeUtility.Deliver(DikInputReceiver, ArraySegment{byte})"/>).
    /// </summary>
    public interface IInputNetworkBridge
    {
        /// <summary>False while not connected or not owner. Commands are dropped (and not predicted) while false.</summary>
        bool CanSend { get; }

        void SendToServer(in InputCommand command);
    }

    public static class InputNetworkBridgeUtility
    {
        [ThreadStatic] static List<InputCommand> s_Buffer;

        /// <summary>Decodes a packet from <see cref="InputCommandBatcher"/> and passes every command to the receiver.</summary>
        public static int Deliver(DikInputReceiver receiver, ArraySegment<byte> packet)
        {
            if (receiver == null)
                return 0;

            var buffer = s_Buffer ?? (s_Buffer = new List<InputCommand>(16));
            buffer.Clear();
            InputCommandSerializer.ReadAll(packet, buffer);
            for (var i = 0; i < buffer.Count; i++)
                receiver.Receive(buffer[i]);
            return buffer.Count;
        }

        public static int Deliver(DikInputReceiver receiver, byte[] packet)
        {
            return packet == null ? 0 : Deliver(receiver, new ArraySegment<byte>(packet));
        }
    }
}
