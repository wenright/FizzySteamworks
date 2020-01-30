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
            s.Listen();
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

            Active = true;
        }

        private async void Listen()
        {
            InternalReceiveLoop();
            await ReceiveLoop();
        }

        protected override void OnNewConnectionInternal(P2PSessionRequest_t result) => SteamNetworking.AcceptP2PSessionWithUser(result.m_steamIDRemote);
        

        private async void InternalReceiveLoop()
        {
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
                            case (byte)InternalMessages.CONNECT:
                                if (steamToMirrorIds.Count >= maxConnections)
                                {
                                    SendInternal(clientSteamID, disconnectMsgBuffer);
                                    continue;
                                    //too many connections, reject
                                }
                                SendInternal(clientSteamID, acceptConnectMsgBuffer);

                                int connectionId = nextConnectionID++;
                                steamToMirrorIds.Add(clientSteamID, connectionId);
                                OnConnected?.Invoke(connectionId);
                                break;
                            case (byte)InternalMessages.DISCONNECT:
                                if(steamToMirrorIds.Contains(clientSteamID))
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
                        }
                    }

                    await Task.Delay(updateInterval);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async Task ReceiveLoop()
        {
            uint readPacketSize;
            CSteamID clientSteamID;

            try
            {
                byte[] receiveBuffer;
                while (Active)
                {
                    for (int i = 0; i < Channels.Length; i++)
                    {
                        while (Receive(out readPacketSize, out clientSteamID, out receiveBuffer, i))
                        {
                            if (readPacketSize == 0)
                            {
                                continue;
                            }

                            if(steamToMirrorIds.Contains(clientSteamID))
                            {
                                int connectionId = steamToMirrorIds[clientSteamID];
                                OnReceivedData?.Invoke(connectionId, receiveBuffer, i);
                            }
                            else
                            {
                                CloseP2PSessionWithUser(clientSteamID);
                                Debug.LogError("Data received from steam client thats not known " + clientSteamID);
                                OnReceivedError?.Invoke(-1, new Exception("ERROR Unknown SteamID"));
                            }
                        }
                    }

                    await Task.Delay(updateInterval);
                }
            }
            catch (Exception e) 
            {
                Debug.LogException(e);
            }
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