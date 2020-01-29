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

        public static int clientConnectTimeoutMS = 25000;

        private CSteamID hostSteamID = CSteamID.Nil;
        private TaskCompletionSource<Task> connectedComplete;
        private CancellationTokenSource cancelToken;

        public bool Active { get; private set; }

        public async void Connect(string host)
        {
            cancelToken = new CancellationTokenSource();
            // not if already started
            if (Active)
            {
                // exceptions are better than silence
                Debug.LogError("Client already connected or connecting");
                OnReceivedError?.Invoke(new Exception("Client already connected"));
                return;
            }

            Active = true;
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

                if (await Task.WhenAny(connectedCompleteTask, Task.Delay(clientConnectTimeoutMS, cancelToken.Token)) != connectedCompleteTask)
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
                deinitialise();
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
                    await Task.Delay(TimeSpan.FromSeconds(secondsBetweenPolls));
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
                    await Task.Delay(TimeSpan.FromSeconds(secondsBetweenPolls));
                }
            }
            catch (ObjectDisposedException) { }

            Debug.Log("InternalReceiveLoop Stop");
        }

        // send the data or throw exception
        public bool Send(byte[] data, int channelId)
        {
            if (Active)
            {
                Send(hostSteamID, data, channelToSendType(channelId), channelId);
                return true;
            }
            else
            {
                throw new Exception("Not Connected");
                //return false;
            }
        }

    }
}
