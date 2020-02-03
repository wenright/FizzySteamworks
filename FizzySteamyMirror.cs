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
        private const string STEAM_SCHEME = "steam";

        private Client client;
        private Server server;

        private Common activeNode;

        [SerializeField]
        public EP2PSend[] Channels = new EP2PSend[1] { EP2PSend.k_EP2PSendReliable };

        [Tooltip("Timeout for connecting in seconds.")]
        public int Timeout = 25;
        [Tooltip("The Steam ID for your application.")]
        public string SteamAppID = "480";
        [Tooltip("Allow or disallow P2P connections to fall back to being relayed through the Steam servers if a direct connection or NAT-traversal cannot be established.")]
        public bool AllowSteamRelay = true;

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

        private void LateUpdate()
        {
            if (activeNode != null)
            {
                activeNode.ReceiveData();
                activeNode.ReceiveInternal();
            }
        }

        public override bool ClientConnected() => ClientActive() && client.Connected;
        public override void ClientConnect(string address)
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("SteamWorks not initialized. Client could not be started.");
                return;
            }

            if (ServerActive())
            {
                Debug.LogError("Transport already running as server!");
                return;
            }

            if (!ClientActive())
            {
                Debug.Log($"Starting client, target address {address}.");

                SteamNetworking.AllowP2PPacketRelay(AllowSteamRelay);
                client = Client.CreateClient(this, address);
                activeNode = client;
            }
            else
            {
                Debug.LogError("Client already running!");
            }
        }

        public override void ClientConnect(Uri uri)
        {
            if (uri.Scheme != STEAM_SCHEME)
                throw new ArgumentException($"Invalid url {uri}, use {STEAM_SCHEME}://SteamID instead", nameof(uri));

            ClientConnect(uri.Host);
        }

        public override bool ClientSend(int channelId, ArraySegment<byte> segment) => client.Send(segment.Array, channelId);
        public override void ClientDisconnect()
        {
            if (ClientActive())
            {
                Shutdown();
            }
        }
        public bool ClientActive() => client != null;


        public override bool ServerActive() => server != null;
        public override void ServerStart()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("SteamWorks not initialized. Server could not be started.");
                return;
            }

            if (ClientActive())
            {
                Debug.LogError("Transport already running as client!");
                return;
            }

            if (!ServerActive())
            {
                Debug.Log("Starting server.");
                SteamNetworking.AllowP2PPacketRelay(AllowSteamRelay);
                server = Server.CreateServer(this, NetworkManager.singleton.maxConnections);
                activeNode = server;
            }
            else
            {
                Debug.LogError("Server already started!");
            }
        }

        public override Uri ServerUri()
        {
            var steamBuilder = new UriBuilder
            {
                Scheme = STEAM_SCHEME,
                Host = SteamUser.GetSteamID().m_SteamID.ToString()
            };

            return steamBuilder.Uri;
        }

        public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment) => ServerActive() && server.SendAll(connectionIds, segment.Array, channelId);
        public override bool ServerDisconnect(int connectionId) => ServerActive() && server.Disconnect(connectionId);
        public override string ServerGetClientAddress(int connectionId) => ServerActive() ? server.ServerGetClientAddress(connectionId) : string.Empty;
        public override void ServerStop()
        {
            if (ServerActive())
            {
                Shutdown();
            }
        }

        public override void Shutdown()
        {
            server?.Shutdown();
            client?.Disconnect();

            server = null;
            client = null;
            activeNode = null;
            Debug.Log("Transport shut down.");
        }

        public override int GetMaxPacketSize(int channelId)
        {
            channelId = Math.Min(channelId, Channels.Length - 1);

            EP2PSend sendMethod = Channels[channelId];
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
            if (activeNode != null)
            {
                Shutdown();
            }
        }
    }
}