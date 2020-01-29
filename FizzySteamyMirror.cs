using UnityEngine;
using Steamworks;
using System;
using System.Collections.Generic;

namespace Mirror.FizzySteam
{
    [HelpURL("https://github.com/Chykary/FizzySteamyMirror")]
    public class FizzySteamyMirror : Transport
    {
        protected FizzySteam.Client client = new FizzySteam.Client();
        protected FizzySteam.Server server = new FizzySteam.Server();
        public EP2PSend[] channels = new EP2PSend[2] { EP2PSend.k_EP2PSendReliable, EP2PSend.k_EP2PSendUnreliable };

        public int MaxConnections = 16;
        [Tooltip("Timeout for connecting in seconds.")]
        public int Timeout = 25;
        [Tooltip("Message update rate in milliseconds.")]
        public int messageUpdateRate = 35;

        private void Start()
        {
            Common.SetMessageUpdateRate(messageUpdateRate);

            if (channels == null)
            {
                channels = new EP2PSend[2] { EP2PSend.k_EP2PSendReliable, EP2PSend.k_EP2PSendUnreliable };
            }
            channels[0] = EP2PSend.k_EP2PSendReliable;
            Common.channels = channels;
        }

        public FizzySteamyMirror()
        {
            // dispatch the events from the server
            server.OnConnected += (id) => OnServerConnected?.Invoke(id);
            server.OnDisconnected += (id) => OnServerDisconnected?.Invoke(id);
            server.OnReceivedData += (id, data, channel) => OnServerDataReceived?.Invoke(id, new ArraySegment<byte>(data), channel);
            server.OnReceivedError += (id, exception) => OnServerError?.Invoke(id, exception);

            // dispatch events from the client
            client.OnConnected += () => OnClientConnected?.Invoke();
            client.OnDisconnected += () => OnClientDisconnected?.Invoke();
            client.OnReceivedData += (data, channel) => OnClientDataReceived?.Invoke(new ArraySegment<byte>(data), channel);
            client.OnReceivedError += (exception) => OnClientError?.Invoke(exception);
            client.ConnectionTimeout = TimeSpan.FromSeconds(Timeout);

            Debug.Log("FizzySteamyMirror initialized!");
        }

        // client
        public override bool ClientConnected() => client.Connected;
        public override void ClientConnect(string address) => client.Connect(address);
        public override bool ClientSend(int channelId, ArraySegment<byte> segment) => client.Send(segment.Array, channelId);
        public override void ClientDisconnect() => client.Disconnect();

        // server
        public override bool ServerActive() => server.Active;
        public override void ServerStart() => server.Listen(MaxConnections);


        public override Uri ServerUri() => throw new NotSupportedException();

        public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment) => server.Send(connectionIds, segment.Array, channelId);
        public override bool ServerDisconnect(int connectionId) => server.Disconnect(connectionId);
        public override string ServerGetClientAddress(int connectionId) => server.ServerGetClientAddress(connectionId);
        public override void ServerStop() => server.Stop();

        public override void Shutdown()
        {
            client.Disconnect();
            server.Stop();
        }

        public override int GetMaxPacketSize(int channelId)
        {
            if (channelId >= channels.Length)
            {
                channelId = 0;
            }
            EP2PSend sendMethod = channels[channelId];
            switch (sendMethod)
            {
                case EP2PSend.k_EP2PSendUnreliable:
                    return 1200; //UDP like - MTU size.
                case EP2PSend.k_EP2PSendUnreliableNoDelay:
                    return 1200; //UDP like - MTU size.
                case EP2PSend.k_EP2PSendReliable:
                    return 1048576; //Reliable message send. Can send up to 1MB of data in a single message.
                case EP2PSend.k_EP2PSendReliableWithBuffering:
                    return 1048576; //Reliable message send. Can send up to 1MB of data in a single message.
                default:
                    return 1200; //UDP like - MTU size.
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
    }
}