using Steamworks;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Client : Common
    {
        private event Action<Exception> OnReceivedError;
        private event Action<byte[], int> OnReceivedData;
        private event Action OnConnected;
        private event Action OnDisconnected;

        private TimeSpan ConnectionTimeout;

        private CSteamID hostSteamID = CSteamID.Nil;
        private TaskCompletionSource<Task> connectedComplete;
        private CancellationTokenSource cancelToken;

        private Client(FizzySteamyMirror transport) : base(transport.Channels)
        {
            OnConnected += () => transport.OnClientConnected?.Invoke();
            OnDisconnected += () => transport.OnClientDisconnected?.Invoke();
            OnReceivedData += (data, channel) => transport.OnClientDataReceived?.Invoke(new ArraySegment<byte>(data), channel);
            OnReceivedError += (exception) => transport.OnClientError?.Invoke(exception);
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Min(1, transport.Timeout));
        }

        public static Client CreateClient(FizzySteamyMirror transport, string host)
        {
            Client c = new Client(transport);
            c.Connect(host);
            return c;
        }

        public async void Connect(string host)
        {
            cancelToken = new CancellationTokenSource();

            if (Active)
            {
                Debug.LogError("Client already connected.");
                OnReceivedError?.Invoke(new Exception("Client already connected"));
                return;
            }

            try
            {
                hostSteamID = new CSteamID(Convert.ToUInt64(host));

                InternalReceiveLoop();

                connectedComplete = new TaskCompletionSource<Task>();

                OnConnected += SetConnectedComplete;
                CloseP2PSessionWithUser(hostSteamID);

                //Send a connect message to the steam client - this requests a connection with them
                SendInternal(hostSteamID, connectMsgBuffer);

                Task connectedCompleteTask = connectedComplete.Task;

                if (await Task.WhenAny(connectedCompleteTask, Task.Delay(ConnectionTimeout, cancelToken.Token)) != connectedCompleteTask)
                {
                    //Timed out waiting for connection to complete
                    OnConnected -= SetConnectedComplete;

                    Exception e = new Exception("Timed out while connecting");
                    OnReceivedError?.Invoke(e);
                    throw e;
                }

                OnConnected -= SetConnectedComplete;

                await ReceiveLoop();
            }
            catch (FormatException)
            {
                Debug.LogError("Failed to connect ERROR passing steam ID address");
                OnReceivedError?.Invoke(new Exception("ERROR passing steam ID address"));
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to connect " + ex);
                OnReceivedError?.Invoke(ex);
            }
            finally
            {
                Disconnect();
            }

        }

        public async void Disconnect()
        {
            if (Active)
            {
                SendInternal(hostSteamID, disconnectMsgBuffer);
                Active = false;
                OnDisconnected?.Invoke();
                Dispose();
                cancelToken.Cancel();

                await Task.Delay(100);
                CloseP2PSessionWithUser(hostSteamID);
            }
            else
            {
                Debug.Log("Tried to disconnect but node is not active.");
            }

        }

        private void SetConnectedComplete()
        {
            connectedComplete.SetResult(connectedComplete.Task);
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
                    for (int i = 0; i < Channels.Length; i++)
                    {
                        while (Receive(out readPacketSize, out clientSteamID, out receiveBuffer, i))
                        {
                            if (readPacketSize == 0)
                            {
                                continue;
                            }
                            if (clientSteamID != hostSteamID)
                            {
                                Debug.LogError("Received a message from an unknown");
                                continue;
                            }
                            // we received some data,  raise event
                            OnReceivedData?.Invoke(receiveBuffer, i);
                        }
                    }
                    //not got a message - wait a bit more
                    await Task.Delay(updateInterval);
                }
            }
            catch (ObjectDisposedException) { }

            Debug.Log("ReceiveLoop Stop");
        }

        protected override void OnNewConnectionInternal(P2PSessionRequest_t result)
        {
            Debug.Log("OnNewConnectionInternal in client");

            if (hostSteamID == result.m_steamIDRemote)
            {
                SteamNetworking.AcceptP2PSessionWithUser(result.m_steamIDRemote);
            }
            else
            {
                Debug.LogError("");
            }
        }

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
                        if (readPacketSize != 1)
                        {
                            continue;
                        }
                        if (clientSteamID != hostSteamID)
                        {
                            Debug.LogError("Received an internal message from an unknown");
                            continue;
                        }
                        switch (receiveBufferInternal[0])
                        {
                            case (byte)InternalMessages.ACCEPT_CONNECT:
                                Active = true;
                                OnConnected?.Invoke();
                                break;
                            case (byte)InternalMessages.DISCONNECT:
                                if (Active)
                                {
                                    Active = false;
                                    OnDisconnected?.Invoke();
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

        public bool Send(byte[] data, int channelId)
        {
            if (Active)
            {
                Send(hostSteamID, data, channelId);
                return true;
            }
            else
            {
                Debug.Log("Could not send - not connected.");
                return false;
            }
        }
    }
}