using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using Steamworks;

namespace Mirror.FizzySteam
{
    internal class SteamClient
    {
        public bool Disconnecting = false;
        public CSteamID steamID;
        public int connectionID;

        public SteamClient(CSteamID steamID, int connectionID)
        {
            this.steamID = steamID;
            this.connectionID = connectionID;
        }
    }

    internal class SteamConnectionMap : IEnumerable<KeyValuePair<int, SteamClient>>
    {
        private readonly Dictionary<CSteamID, SteamClient> fromSteamID = new Dictionary<CSteamID, SteamClient>();
        private readonly Dictionary<int, SteamClient> fromConnectionID = new Dictionary<int, SteamClient>();

        public int Count => fromSteamID.Count;

        public SteamClient Add(CSteamID steamID, int connectionID)
        {
            var newClient = new SteamClient(steamID, connectionID);
            fromSteamID.Add(steamID, newClient);
            fromConnectionID.Add(connectionID, newClient);
            return newClient;
        }

        public void Remove(SteamClient steamClient)
        {
            fromSteamID.Remove(steamClient.steamID);
            fromConnectionID.Remove(steamClient.connectionID);
        }

        public bool Contains(int connectionId) => fromConnectionID.ContainsKey(connectionId);

        public SteamClient this[CSteamID key]
        {
            get => fromSteamID[key];
        }

        public SteamClient this[int key]
        {
            get => fromConnectionID[key];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<KeyValuePair<int, SteamClient>> GetEnumerator() => fromConnectionID.GetEnumerator();
    }

    public class Server : Common
    {
        public bool Active { get; private set; }

        public event Action<int> OnConnected;
        public event Action<int, byte[], int> OnReceivedData;
        public event Action<int> OnDisconnected;
        public event Action<int, Exception> OnReceivedError;

        private SteamConnectionMap steamConnectionMap;
        private int maxConnections;
        private int nextConnectionID;

        public async void Listen(int maxConnections)
        {
            if (Active)
            {
                Debug.Log("Server already listening.");
                return;
            }

            Debug.Log("Listen Start");
            steamConnectionMap = new SteamConnectionMap();
            nextConnectionID = 0;

            initialise();
            Active = true;
            this.maxConnections = maxConnections;

            InternalReceiveLoop();

            await ReceiveLoop();

            Debug.Log("Listen Stop");
        }

        protected override void OnNewConnectionInternal(P2PSessionRequest_t result)
        {
            Debug.Log("OnNewConnectionInternal in server");
            SteamNetworking.AcceptP2PSessionWithUser(result.m_steamIDRemote);
        }


        //start a async loop checking for internal messages and processing them. This includes internal connect negotiation and disconnect requests so runs outside "connected"
        private async void InternalReceiveLoop()
        {
            Debug.Log("InternalReceiveLoop Start");

            uint readPacketSize;
            CSteamID clientSteamID;

            try
            {
                while (Active)
                {
                    while (ReceiveInternal(out readPacketSize, out clientSteamID))
                    {
                        Debug.Log("InternalReceiveLoop - data");
                        if (readPacketSize != 1)
                        {
                            continue;
                        }
                        Debug.Log("InternalReceiveLoop - received " + receiveBufferInternal[0]);
                        switch (receiveBufferInternal[0])
                        {
                            //requesting to connect to us
                            case (byte)InternalMessages.CONNECT:
                                if (steamConnectionMap.Count >= maxConnections)
                                {
                                    SendInternal(clientSteamID, disconnectMsgBuffer);
                                    continue;
                                    //too many connections, reject
                                }
                                SendInternal(clientSteamID, acceptConnectMsgBuffer);

                                int connectionId = nextConnectionID++;
                                steamConnectionMap.Add(clientSteamID, connectionId);
                                OnConnected?.Invoke(connectionId);
                                break;

                            //asking us to disconnect
                            case (byte)InternalMessages.DISCONNECT:
                                try
                                {
                                    SteamClient steamClient = steamConnectionMap[clientSteamID];
                                    steamConnectionMap.Remove(steamClient);
                                    OnDisconnected?.Invoke(steamClient.connectionID);
                                    CloseP2PSessionWithUser(steamClient.steamID);
                                }
                                catch (KeyNotFoundException)
                                {
                                    //we have no idea who this connection is
                                    Debug.LogError("Trying to disconnect a client thats not known SteamID " + clientSteamID);
                                    OnReceivedError?.Invoke(-1, new Exception("ERROR Unknown SteamID"));
                                }

                                break;
                        }
                    }

                    //not got a message - wait a bit more
                    await Task.Delay(updateInterval);
                }
            }
            catch (ObjectDisposedException) { }

            Debug.Log("InternalReceiveLoop Stop");
        }

        private async Task ReceiveLoop()
        {
            Debug.Log("ReceiveLoop Start");

            uint readPacketSize;
            CSteamID clientSteamID;

            try
            {
                byte[] receiveBuffer;
                while (Active)
                {
                    for (int i = 0; i < channels.Length; i++)
                    {
                        while (Receive(out readPacketSize, out clientSteamID, out receiveBuffer, i))
                        {
                            if (readPacketSize == 0)
                            {
                                continue;
                            }

                            try
                            {
                                int connectionId = steamConnectionMap[clientSteamID].connectionID;
                                // we received some data,  raise event
                                OnReceivedData?.Invoke(connectionId, receiveBuffer, i);
                            }
                            catch (KeyNotFoundException)
                            {
                                CloseP2PSessionWithUser(clientSteamID);
                                //we have no idea who this connection is
                                Debug.LogError("Data received from steam client thats not known " + clientSteamID);
                                OnReceivedError?.Invoke(-1, new Exception("ERROR Unknown SteamID"));
                            }
                        }
                    }
                    //not got a message - wait a bit more
                    await Task.Delay(updateInterval);
                }
            }
            catch (ObjectDisposedException) { }

            Debug.Log("ReceiveLoop Stop");
        }

        public void Stop()
        {
            Debug.LogWarning("Server Stop");

            if (!Active)
            {
                Debug.Log("Trying to stop but server is not active.");
                return;
            }

            Active = false;
            Dispose();
            Debug.Log("Server Stop Finished");
        }

        public bool Disconnect(int connectionId)
        {
            if (steamConnectionMap.Contains(connectionId))
            {
                SteamClient steamClient = steamConnectionMap[connectionId];
                Disconnect(steamClient);
                return true;
            }
            else
            {
                //we have no idea who this connection is
                Debug.LogWarning("Trying to disconnect unknown connection id: " + connectionId);
                return false;
            }
        }

        private async void Disconnect(SteamClient steamClient)
        {
            if (!steamClient.Disconnecting)
            {
                return;
            }

            SendInternal(steamClient.steamID, disconnectMsgBuffer);
            steamClient.Disconnecting = true;

            //Wait a short time before calling steams disconnect function so the message has time to go out
            await Task.Delay(100);
            steamConnectionMap.Remove(steamClient);
            OnDisconnected?.Invoke(steamClient.connectionID);
            CloseP2PSessionWithUser(steamClient.steamID);
        }

        public bool Send(List<int> connectionIds, byte[] data, int channelId)
        {
            for (int i = 0; i < connectionIds.Count; i++)
            {
                if (steamConnectionMap.Contains(connectionIds[i]))
                {
                    SteamClient steamClient = steamConnectionMap[connectionIds[i]];
                    Send(steamClient.steamID, data, channelId);
                }
                else
                {
                    Debug.LogError("Trying to send on unknown connection: " + connectionIds[i]);
                    OnReceivedError?.Invoke(connectionIds[i], new Exception("ERROR Unknown Connection"));
                }
            }

            return true;
        }

        public string ServerGetClientAddress(int connectionId)
        {
            if (steamConnectionMap.Contains(connectionId))
            {
                SteamClient steamClient = steamConnectionMap[connectionId];
                return steamClient.steamID.ToString();
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