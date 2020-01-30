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
        CancellationTokenSource cancelToken;

        private Client(FizzySteamyMirror transport) : base(transport.Channels)
        {
            OnConnected += () => transport.OnClientConnected?.Invoke();
            OnDisconnected += () => transport.OnClientDisconnected?.Invoke();
            OnReceivedData += (data, channel) => transport.OnClientDataReceived?.Invoke(new ArraySegment<byte>(data), channel);
            OnReceivedError += (exception) => transport.OnClientError?.Invoke(exception);
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Min(1, transport.Timeout));

            SetMessageUpdateRate(transport.messageUpdateRate);
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

                StartInternalLoop();

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
                StartDataLoops();
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
        }

        public void Disconnect()
        {
            if (Active)
            {
                SendInternal(hostSteamID, disconnectMsgBuffer);
                Active = false;
                OnDisconnected?.Invoke();
                Dispose();
                cancelToken.Cancel();

                Task.Delay(100).ContinueWith(t => CloseP2PSessionWithUser(hostSteamID));
            }
            else
            {
                Debug.Log("Tried to disconnect but node is not active.");
            }

        }

        private void SetConnectedComplete() => connectedComplete.SetResult(connectedComplete.Task);
        

        protected override void OnReceiveData(byte[] data, CSteamID clientSteamID, int channel)
        {
            if (clientSteamID != hostSteamID)
            {
                Debug.LogError("Received a message from an unknown");
                return;
            }
            
            OnReceivedData?.Invoke(data, channel);
        }

        protected override void OnNewConnectionInternal(P2PSessionRequest_t result)
        {
            if (hostSteamID == result.m_steamIDRemote)
            {
                SteamNetworking.AcceptP2PSessionWithUser(result.m_steamIDRemote);
            }
            else
            {
                Debug.LogError("P2P Acceptance Request from unknown host ID.");
            }
        }

        protected override void OnReceiveInternalData(InternalMessages type, CSteamID clientSteamID)
        {
            switch (type)
            {
                case InternalMessages.ACCEPT_CONNECT:
                    Active = true;
                    OnConnected?.Invoke();
                    break;
                case InternalMessages.DISCONNECT:
                    if (Active)
                    {
                        Active = false;
                        OnDisconnected?.Invoke();
                    }
                    break;
                default:
                    Debug.Log("Received unknown message type");
                    break;
            }
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