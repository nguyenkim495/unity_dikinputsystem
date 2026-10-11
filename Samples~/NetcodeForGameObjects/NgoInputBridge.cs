// Example bridge for Unity Netcode for GameObjects 1.8+ / 2.x (com.unity.netcode.gameobjects).
// It is a template: adapt it to your netcode (Mirror: [Command]; Fish-Net: [ServerRpc]; etc.).
using System;
using Dik.Input;
using Unity.Netcode;
using UnityEngine;

namespace Dik.Input.Samples
{
    /// <summary>
    /// Put on the networked player prefab next to DikInputPlayer + DikInputReceiver.
    /// Keep PlayerInput DISABLED on the prefab: this script enables it only for the owner, so remote copies never
    /// grab local devices.
    ///
    /// Flow:
    ///   Owner client: device -> DikInputPlayer -> SendToServer -> batch -> SubmitInputRpc
    ///   Server:       SubmitInputRpc -> DikInputReceiver.Receive -> gameplay (authoritative)
    ///   Host:         DikInput.NetworkMode = Host, so commands go straight to the local receiver.
    /// </summary>
    public sealed class NgoInputBridge : NetworkBehaviour, IInputNetworkBridge
    {
        [SerializeField] DikInputPlayer m_Player;
        [SerializeField] DikInputReceiver m_Receiver;

        readonly InputCommandBatcher m_Batcher = new InputCommandBatcher();

        public bool CanSend => IsSpawned && IsOwner;

        public override void OnNetworkSpawn()
        {
            if (IsServer && IsClient)
                DikInput.NetworkMode = InputNetworkMode.Host;
            else if (IsServer)
                DikInput.NetworkMode = InputNetworkMode.Server;
            else
                DikInput.NetworkMode = InputNetworkMode.Client;

            DikInput.TickSource = () => (uint)NetworkManager.LocalTime.Tick;

            if (m_Player != null)
            {
                m_Player.NetworkBridge = this;
                m_Player.PlayerInput.enabled = IsOwner;
                m_Player.enabled = IsOwner;
            }
        }

        public override void OnNetworkDespawn()
        {
            // Remote client left: release whatever it was holding on the server.
            if (IsServer && m_Receiver != null)
                m_Receiver.ReleaseAll();
        }

        public void SendToServer(in InputCommand command) => m_Batcher.Add(command);

        void LateUpdate()
        {
            if (!CanSend || !m_Batcher.TryFlush(out var packet))
                return;

            var bytes = new byte[packet.Count];
            Buffer.BlockCopy(packet.Array, packet.Offset, bytes, 0, packet.Count);
            SubmitInputRpc(bytes);
        }

        [Rpc(SendTo.Server, RequireOwnership = true)]
        void SubmitInputRpc(byte[] packet)
        {
            InputNetworkBridgeUtility.Deliver(m_Receiver, packet);
        }
    }
}
