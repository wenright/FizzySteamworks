using Steamworks;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class Client : Common
    {
        public event Action<Exception> OnReceivedError;
        public event Action<byte[], int> OnReceivedData;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(25);

        private CSteamID hostSteamID = CSteamID.Nil;
        private TaskCompletionSource<Task> connectedComplete;
        private CancellationTokenSource cancelToken;

        public bool Connected { get; private set; }

        public async void Connect(string host)
        {
            cancelToken = new CancellationTokenSource();

            if (Connected)
            {
                Debug.LogError("Client already connected.");
                OnReceivedError?.Invoke(new Exception("Client already connected"));
                return;
            }

            initialise();

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
            if (Connected)
            {
                SendInternal(hostSteamID, disconnectMsgBuffer);
                Connected = false;
                OnDisconnected?.Invoke();
                Dispose();
                cancelToken.Cancel();

                //Wait a short time before calling steams disconnect function so the message has time to go out
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

                while (Connected)
                {
                    for (int i = 0; i < channels.Length; i++)
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

        //start a async loop checking for internal messages and processing them. This includes internal connect negotiation and disconnect requests so runs outside "connected"
        private async void InternalReceiveLoop()
        {
            Debug.Log("InternalReceiveLoop Start");

            uint readPacketSize;
            CSteamID clientSteamID;

            try
            {
                while (Connected)
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
                                Connected = true;
                                OnConnected?.Invoke();
                                break;
                            case (byte)InternalMessages.DISCONNECT:
                                if (Connected)
                                {
                                    Connected = false;
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

        // send the data or throw exception
        public bool Send(byte[] data, int channelId)
        {
            if (Connected)
            {
                Send(hostSteamID, data, channelToSendType(channelId), channelId);
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
