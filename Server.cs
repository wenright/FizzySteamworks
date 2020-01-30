using Steamworks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Server : Common
    {
        private event Action<int> OnConnected;
        private event Action<int, byte[], int> OnReceivedData;
        private event Action<int> OnDisconnected;
        private event Action<int, Exception> OnReceivedError;

        private BidirectionalDictionary<CSteamID, int> steamToMirrorIds;
        private int maxConnections;
        private int nextConnectionID;

        public static Server CreateServer(FizzySteamyMirror transport, int maxConnections)
        {
            Server s = new Server(transport, maxConnections);
            if (SteamManager.Initialized)
            {
                s.Listen();
            }
            else
            {
                s.Error = true;
                Debug.LogError("SteamWorks not initialized");
            }

            return s;
        }

        private Server(FizzySteamyMirror transport, int maxConnections) : base(transport.Channels)
        {
            this.maxConnections = maxConnections;
            SetMessageUpdateRate(transport.messageUpdateRate);
            steamToMirrorIds = new BidirectionalDictionary<CSteamID, int>();
            nextConnectionID = 0;

            OnConnected += (id) => transport.OnServerConnected?.Invoke(id);
            OnDisconnected += (id) => transport.OnServerDisconnected?.Invoke(id);
            OnReceivedData += (id, data, channel) => transport.OnServerDataReceived?.Invoke(id, new ArraySegment<byte>(data), channel);
            OnReceivedError += (id, exception) => transport.OnServerError?.Invoke(id, exception);
        }

        private void Listen()
        {
            StartInternalLoop();
            StartDataLoops();
        }

        protected override void OnNewConnection(P2PSessionRequest_t result) => SteamNetworking.AcceptP2PSessionWithUser(result.m_steamIDRemote);

        protected override void OnReceiveInternalData(InternalMessages type, CSteamID clientSteamID)
        {
            switch (type)
            {
                case InternalMessages.CONNECT:
                    if (steamToMirrorIds.Count >= maxConnections)
                    {
                        SendInternal(clientSteamID, disconnectMsgBuffer);
                        return;
                    }
                    SendInternal(clientSteamID, acceptConnectMsgBuffer);

                    int connectionId = nextConnectionID++;
                    steamToMirrorIds.Add(clientSteamID, connectionId);
                    OnConnected?.Invoke(connectionId);
                    break;
                case InternalMessages.DISCONNECT:
                    if (steamToMirrorIds.Contains(clientSteamID))
                    {
                        steamToMirrorIds.Remove(clientSteamID);
                        OnDisconnected?.Invoke(steamToMirrorIds[clientSteamID]);
                        CloseP2PSessionWithUser(clientSteamID);
                    }
                    else
                    {
                        Debug.LogError("Trying to disconnect a client thats not known SteamID " + clientSteamID);
                        OnReceivedError?.Invoke(-1, new Exception("ERROR Unknown SteamID"));
                    }

                    break;
                default:
                    Debug.Log("Received unknown message type");
                    break;
            }
        }

        protected override void OnReceiveData(byte[] data, CSteamID clientSteamID, int channel)
        {
            if (steamToMirrorIds.Contains(clientSteamID))
            {
                int connectionId = steamToMirrorIds[clientSteamID];
                OnReceivedData?.Invoke(connectionId, data, channel);
            }
            else
            {
                CloseP2PSessionWithUser(clientSteamID);
                Debug.LogError("Data received from steam client thats not known " + clientSteamID);
                OnReceivedError?.Invoke(-1, new Exception("ERROR Unknown SteamID"));
            }
        }

        public bool Disconnect(int connectionId)
        {
            if (steamToMirrorIds.Contains(connectionId))
            {
                Disconnect(steamToMirrorIds[connectionId], connectionId);
                return true;
            }
            else
            {
                Debug.LogWarning("Trying to disconnect unknown connection id: " + connectionId);
                return false;
            }
        }

        private async void Disconnect(CSteamID steamID, int connId)
        {
            steamToMirrorIds.Remove(connId);

            SendInternal(steamID, disconnectMsgBuffer);

            //Wait a short time before calling steams disconnect function so the message has time to go out
            await Task.Delay(100);
            
            OnDisconnected?.Invoke(connId);
            CloseP2PSessionWithUser(steamID);
        }

        public bool SendAll(List<int> connectionIds, byte[] data, int channelId)
        {
            foreach(int connId in connectionIds)
            {
                if (steamToMirrorIds.Contains(connId))
                {
                    Send(steamToMirrorIds[connId], data, channelId);
                }
                else
                {
                    Debug.LogError("Trying to send on unknown connection: " + connId);
                    OnReceivedError?.Invoke(connId, new Exception("ERROR Unknown Connection"));
                }
            }

            return true;
        }

        public string ServerGetClientAddress(int connectionId)
        {
            if (steamToMirrorIds.Contains(connectionId))
            {
                return steamToMirrorIds[connectionId].ToString();
            }
            else
            {
                Debug.LogError("Trying to get info on unknown connection: " + connectionId);
                OnReceivedError?.Invoke(connectionId, new Exception("ERROR Unknown Connection"));
                return string.Empty;
            }
        }
    }
}