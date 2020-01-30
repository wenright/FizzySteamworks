using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public abstract class Common
    {
        public bool Error { get; protected set; }

        private EP2PSend[] channels;
        private TimeSpan[] updateIntervals;

        private const int SEND_INTERNAL = 100;
        private const uint SEND_INTERNAL_INTERVAL = 200;

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

        private List<Task> receiveLoops;
        private CancellationTokenSource cts;

        protected Common(SteamChannel[] channels)
        {
            Debug.Assert(channels.Length < 100, "FizzySteamyMirror does not support more than 99 channels.");
            this.channels = channels.Select(x => x.Type).ToArray();
            updateIntervals = channels.Select(x => TimeSpan.FromMilliseconds(Mathf.Max(1, x.UpdateInterval))).ToArray();

            callback_OnNewConnection = Callback<P2PSessionRequest_t>.Create(OnNewConnection);
            callback_OnConnectFail = Callback<P2PSessionConnectFail_t>.Create(OnConnectFail);
            receiveLoops = new List<Task>();
            cts = new CancellationTokenSource();
        }

        public void Shutdown()
        {
            cts.Cancel();

            if (callback_OnNewConnection == null)
            {
                callback_OnNewConnection.Dispose();
                callback_OnNewConnection = null;
            }

            if (callback_OnConnectFail == null)
            {
                callback_OnConnectFail.Dispose();
                callback_OnConnectFail = null;
            }            
        }

        protected abstract void OnNewConnection(P2PSessionRequest_t result);

        protected virtual void OnConnectFail(P2PSessionConnectFail_t result)
        {
            Debug.Log("OnConnectFail " + result);
            Error = true;
            throw new Exception("Failed to connect");            
        }

        protected void SendInternal(CSteamID host, byte[] msgBuffer) => SteamNetworking.SendP2PPacket(host, msgBuffer, (uint)msgBuffer.Length, EP2PSend.k_EP2PSendReliable, SEND_INTERNAL);

        private bool ReceiveInternal(out uint readPacketSize, out CSteamID clientSteamID) => SteamNetworking.ReadP2PPacket(receiveBufferInternal, 1, out readPacketSize, out clientSteamID, SEND_INTERNAL);        

        protected void Send(CSteamID host, byte[] msgBuffer, int channel)
        {
            Debug.Assert(channel <= channels.Length, $"Channel {channel} not configured for FizzySteamMirror.");
            SteamNetworking.SendP2PPacket(host, msgBuffer, (uint)msgBuffer.Length, channels[channel], channel);
        }

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

        protected void StartInternalLoop()
        {
            receiveLoops.Add(Task.Factory.StartNew(() => InternalReceiveLoop(cts.Token)));
        }

        protected void StartDataLoops()
        {
            for (int i = 0; i < channels.Length; i++)
            {
                receiveLoops.Add(Task.Factory.StartNew(() => ReceiveLoop(cts.Token, i)));
            }
        }

        private async Task InternalReceiveLoop(CancellationToken t)
        {
            uint readPacketSize;
            CSteamID clientSteamID;
            TimeSpan updateInterval = TimeSpan.FromMilliseconds(SEND_INTERNAL_INTERVAL);

            try
            {
                while (!t.IsCancellationRequested)
                {
                    while (ReceiveInternal(out readPacketSize, out clientSteamID))
                    {
                        Debug.Log("InternalReceiveLoop - data");

                        if (!t.IsCancellationRequested)
                        {
                            OnReceiveInternalData((InternalMessages)receiveBufferInternal[0], clientSteamID);
                        }
                    }

                    await Task.Delay(updateInterval);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Error = true;
            }
        }

        private async Task ReceiveLoop(CancellationToken t, int channelNum)
        {
            uint readPacketSize;
            CSteamID clientSteamID;
            TimeSpan delay = updateIntervals[channelNum];

            try
            {
                byte[] receiveBuffer;
                while (!t.IsCancellationRequested)
                {
                    while (Receive(out readPacketSize, out clientSteamID, out receiveBuffer, channelNum))
                    {
                        if (readPacketSize == 0)
                        {
                            continue;
                        }

                        if (!t.IsCancellationRequested)
                        {
                            OnReceiveData(receiveBuffer, clientSteamID, channelNum);
                        }
                    }

                    await Task.Delay(delay);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Error = true;
            }
        }

        protected abstract void OnReceiveInternalData(InternalMessages type, CSteamID clientSteamID);
        protected abstract void OnReceiveData(byte[] data, CSteamID clientSteamID, int channel);
    }
}