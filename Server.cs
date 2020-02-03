using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Server : Common
    {
        public bool Destroyed;
        private event Action<int> OnConnected;
        private event Action<int, byte[], int> OnReceivedData;
        private event Action<int> OnDisconnected;
        private event Action<int, Exception> OnReceivedError;

        private BidirectionalDictionary<CSteamID, int> steamToMirrorIds;
        private int maxConnections;
        private int nextConnectionID;
        private FizzySteamyMirror transport;

        public static Server CreateServer(FizzySteamyMirror transport, int maxConnections)
        {            
            Server s = new Server(transport, maxConnections);

            s.OnConnected += (id) => transport.OnServerConnected?.Invoke(id);
            s.OnDisconnected += (id) => transport.OnServerDisconnected?.Invoke(id);
            s.OnReceivedData += (id, data, channel) => transport.OnServerDataReceived?.Invoke(id, new ArraySegment<byte>(data), channel);
            s.OnReceivedError += (id, exception) => transport.OnServerError?.Invoke(id, exception);

            if (!SteamManager.Initialized)
            {
                Debug.LogError("SteamWorks not initialized");
            }

            return s;
        }

        private Server(FizzySteamyMirror transport, int maxConnections) : base(transport.Channels)
        {
            this.maxConnections = maxConnections;
            steamToMirrorIds = new BidirectionalDictionary<CSteamID, int>();
            nextConnectionID = 1;
            this.transport = transport;
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
                    Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connectionId}");
                    break;
                case InternalMessages.DISCONNECT:
                    if (steamToMirrorIds.Contains(clientSteamID))
                    {
                        OnDisconnected?.Invoke(steamToMirrorIds[clientSteamID]);
                        steamToMirrorIds.Remove(clientSteamID);
                        CloseP2PSessionWithUser(clientSteamID);
                        Debug.Log($"Client with SteamID {clientSteamID} disconnected.");
                    }
                    else
                    {
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
                CSteamID steamID = steamToMirrorIds[connectionId];
                steamToMirrorIds.Remove(connectionId);

                SendInternal(steamID, disconnectMsgBuffer);
                transport.StartCoroutine(WaitDisconnect(steamID));

                return true;
            }
            else
            {
                Debug.LogWarning("Trying to disconnect unknown connection id: " + connectionId);
                return false;
            }
        }

        private IEnumerator WaitDisconnect(CSteamID steamID)
        {
            yield return new WaitForSeconds(0.1f);
            CloseP2PSessionWithUser(steamID);
        }

        public void Shutdown()
        {
            Dispose();
        }

        public bool SendAll(List<int> connectionIds, byte[] data, int channelId)
        {
            bool success = true;
            foreach (int connId in connectionIds)
            {
                if (steamToMirrorIds.Contains(connId))
                {
                    success = success && Send(steamToMirrorIds[connId], data, channelId);
                }
                else
                {
                    Debug.LogError("Trying to send on unknown connection: " + connId);
                    OnReceivedError?.Invoke(connId, new Exception("ERROR Unknown Connection"));
                }
            }

            return success;
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