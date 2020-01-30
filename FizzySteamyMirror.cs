using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Mirror.FizzySteam
{
    [RequireComponent(typeof(SteamManager))]
    [HelpURL("https://github.com/Chykary/FizzySteamyMirror")]
    public class FizzySteamyMirror : Transport
    {
        private Client client;
        private Server server;

        [SerializeField]
        public SteamChannel[] Channels = new SteamChannel[1] { new SteamChannel() { Type = EP2PSend.k_EP2PSendReliable, UpdateInterval = 35 } };

        [Tooltip("Timeout for connecting in seconds.")]
        public int Timeout = 25;
        [Tooltip("The Steam ID for your application.")]
        public string SteamAppID = "480";

        private void Awake()
        {
            if (File.Exists("steam_appid.txt"))
            {
                string content = File.ReadAllText("steam_appid.txt");
                if (content != SteamAppID)
                {
                    File.WriteAllText("steam_appid.txt", SteamAppID.ToString());
                    Debug.Log($"Updating steam_appid.txt. Previous: {content}, new SteamAppID {SteamAppID}");
                }
            }
            else
            {
                File.WriteAllText("steam_appid.txt", SteamAppID.ToString());
                Debug.Log($"New steam_appid.txt written with SteamAppID {SteamAppID}");
            }

            Debug.Assert(Channels != null && Channels.Length > 0, "No channel configured for FizzySteamMirror.");
        }

        // client
        public override bool ClientConnected() => client != null && client.Connected;
        public override void ClientConnect(string address)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("SteamWorks not initialized. Client could not be started.");
                return;
            }

            if (client == null)
            {
                client = Client.CreateClient(this, address);
            }
            else
            {
                Debug.LogError("Client already running!");
            }
        }
        public override bool ClientSend(int channelId, ArraySegment<byte> segment) => client.Send(segment.Array, channelId);
        public override void ClientDisconnect() => client?.Disconnect();


        // server
        public override bool ServerActive() => server != null && !server.Error;
        public override void ServerStart()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("SteamWorks not initialized. Server could not be started.");
                return;
            }

            if (server != null && server.Error)
            {
                Debug.Log("Cleaning up old server node with errors.");
                server.Shutdown();
                server = null;
            }

            if (server == null)
            {
                server = Server.CreateServer(this, NetworkManager.singleton.maxConnections);
            }
            else
            {
                Debug.LogError("Server already started!");
            }
        }


        public override Uri ServerUri() => throw new NotSupportedException();

        public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment) => ServerActive() && server.SendAll(connectionIds, segment.Array, channelId);
        public override bool ServerDisconnect(int connectionId) => ServerActive() && server.Disconnect(connectionId);
        public override string ServerGetClientAddress(int connectionId) => ServerActive() ? server.ServerGetClientAddress(connectionId) : string.Empty;
        public override void ServerStop()
        {
            if (server != null)
            {
                Debug.Log("Shutting down server.");
                server.Shutdown();
                server = null;
            }
            else
            {
                Debug.Log("No server active, did not stop a server.");
            }
        }

        public override void Shutdown()
        {
            server?.Shutdown();
            client?.Disconnect();

            server = null;
            client = null;
        }

        public override int GetMaxPacketSize(int channelId)
        {
            channelId = Math.Min(channelId, Channels.Length - 1);

            EP2PSend sendMethod = Channels[channelId].Type;
            switch (sendMethod)
            {
                case EP2PSend.k_EP2PSendUnreliable:
                case EP2PSend.k_EP2PSendUnreliableNoDelay:
                    return 1200;
                case EP2PSend.k_EP2PSendReliable:
                case EP2PSend.k_EP2PSendReliableWithBuffering:
                    return 1048576;
                default:
                    throw new NotSupportedException();
            }
        }

        public override bool Available()
        {
            try
            {
                return SteamManager.Initialized;
            }
            catch
            {
                return false;
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    } 

    [Serializable]
    public class SteamChannel
    {
        [Tooltip("Channel Type.")]
        public EP2PSend Type;

        [Tooltip("Message Update Intervall in Milliseconds.")]
        [Range(1, 20000)]
        public int UpdateInterval;
    }
}