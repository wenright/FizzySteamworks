using Steamworks;
using System;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public abstract class Common
    {
        private EP2PSend[] channels;

        private const int SEND_INTERNAL = 100;

        protected enum InternalMessages : byte
        {
            CONNECT,
            ACCEPT_CONNECT,
            DISCONNECT
        }

        private Callback<P2PSessionRequest_t> callback_OnNewConnection = null;
        private Callback<P2PSessionConnectFail_t> callback_OnConnectFail = null;

        readonly protected byte[] connectMsgBuffer = new byte[] { (byte)InternalMessages.CONNECT };
        readonly protected byte[] acceptConnectMsgBuffer = new byte[] { (byte)InternalMessages.ACCEPT_CONNECT };
        readonly protected byte[] disconnectMsgBuffer = new byte[] { (byte)InternalMessages.DISCONNECT };
        readonly protected byte[] receiveBufferInternal = new byte[1];

        protected Common(EP2PSend[] channels)
        {
            Debug.Assert(channels.Length < 100, "FizzySteamyMirror does not support more than 99 channels.");
            this.channels = channels;

            callback_OnNewConnection = Callback<P2PSessionRequest_t>.Create(OnNewConnection);
            callback_OnConnectFail = Callback<P2PSessionConnectFail_t>.Create(OnConnectFail);
        }

        public void Dispose()
        {
            if (callback_OnNewConnection != null)
            {
                callback_OnNewConnection.Dispose();
                callback_OnNewConnection = null;
            }

            if (callback_OnConnectFail != null)
            {
                callback_OnConnectFail.Dispose();
                callback_OnConnectFail = null;
            }
        }

        protected abstract void OnNewConnection(P2PSessionRequest_t result);

        protected virtual void OnConnectFail(P2PSessionConnectFail_t result)
        {
            Debug.Log("OnConnectFail " + result);
            throw new Exception("Failed to connect");
        }

        protected void SendInternal(CSteamID host, byte[] msgBuffer) => SteamNetworking.SendP2PPacket(host, msgBuffer, (uint)msgBuffer.Length, EP2PSend.k_EP2PSendReliable, SEND_INTERNAL);

        private bool ReceiveInternal(out uint readPacketSize, out CSteamID clientSteamID) => SteamNetworking.ReadP2PPacket(receiveBufferInternal, 1, out readPacketSize, out clientSteamID, SEND_INTERNAL);

        protected bool Send(CSteamID host, byte[] msgBuffer, int channel) => SteamNetworking.SendP2PPacket(host, msgBuffer, (uint)msgBuffer.Length, channels[channel], channel);

        private bool Receive(out uint readPacketSize, out CSteamID clientSteamID, out byte[] receiveBuffer, int channel)
        {
            uint packetSize;
            if (SteamNetworking.IsP2PPacketAvailable(out packetSize, channel) && packetSize > 0)
            {
                receiveBuffer = new byte[packetSize];
                return SteamNetworking.ReadP2PPacket(receiveBuffer, packetSize, out readPacketSize, out clientSteamID, channel);
            }

            receiveBuffer = null;
            readPacketSize = 0;
            clientSteamID = CSteamID.Nil;
            return false;
        }

        protected void CloseP2PSessionWithUser(CSteamID clientSteamID) => SteamNetworking.CloseP2PSessionWithUser(clientSteamID);

        public void ReceiveInternal()
        {
            try
            {
                while (ReceiveInternal(out uint readPacketSize, out CSteamID clientSteamID))
                {
                    if (readPacketSize == 1)
                    {
                        OnReceiveInternalData((InternalMessages)receiveBufferInternal[0], clientSteamID);
                    }
                    else
                    {
                        Debug.Log("Incorrect package length on internal channel.");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void ReceiveData()
        {
            try
            {
                byte[] receiveBuffer;
                for (int chNum = 0; chNum < channels.Length; chNum++)
                {
                    while (Receive(out uint readPacketSize, out CSteamID clientSteamID, out receiveBuffer, chNum))
                    {
                        if (readPacketSize > 0)
                        {
                            OnReceiveData(receiveBuffer, clientSteamID, chNum);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        protected abstract void OnReceiveInternalData(InternalMessages type, CSteamID clientSteamID);
        protected abstract void OnReceiveData(byte[] data, CSteamID clientSteamID, int channel);
    }
}