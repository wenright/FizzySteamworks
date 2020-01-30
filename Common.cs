using UnityEngine;
using System;
using Steamworks;

namespace Mirror.FizzySteam
{
  public abstract class Common
  {
    public bool Active { get; protected set; }
    public EP2PSend[] Channels;

    private const int SEND_INTERNAL = 100;

    protected enum InternalMessages : byte
    {
      CONNECT,
      ACCEPT_CONNECT,
      DISCONNECT
    }

    protected TimeSpan updateInterval { get; private set; } = TimeSpan.FromMilliseconds(35);

    private Callback<P2PSessionRequest_t> callback_OnNewConnection = null;
    private Callback<P2PSessionConnectFail_t> callback_OnConnectFail = null;

    readonly protected byte[] connectMsgBuffer = new byte[] { (byte)InternalMessages.CONNECT };
    readonly protected byte[] acceptConnectMsgBuffer = new byte[] { (byte)InternalMessages.ACCEPT_CONNECT };
    readonly protected byte[] disconnectMsgBuffer = new byte[] { (byte)InternalMessages.DISCONNECT };
    readonly protected byte[] receiveBufferInternal = new byte[1];

    protected void SetMessageUpdateRate(int milliseconds) => updateInterval = TimeSpan.FromMilliseconds(Mathf.Min(1, milliseconds));

    protected Common(EP2PSend[] channels)
    {
      Channels = channels;
      callback_OnNewConnection = Callback<P2PSessionRequest_t>.Create(OnNewConnection);
      callback_OnConnectFail = Callback<P2PSessionConnectFail_t>.Create(OnConnectFail);
    }

    protected void Dispose()
    {
      if (callback_OnNewConnection == null)
      {
        callback_OnNewConnection.Dispose();
        callback_OnNewConnection = null;
      }

      if (callback_OnConnectFail == null)
      {
        callback_OnConnectFail.Dispose();
        callback_OnConnectFail = null;
      }

    }

    protected void OnNewConnection(P2PSessionRequest_t result)
    {
      Debug.Log("OnNewConnection");
      OnNewConnectionInternal(result);
    }

    protected virtual void OnNewConnectionInternal(P2PSessionRequest_t result) { Debug.Log("OnNewConnectionInternal"); }

    protected virtual void OnConnectFail(P2PSessionConnectFail_t result)
    {
      Debug.Log("OnConnectFail " + result);
      throw new Exception("Failed to connect");
    }

    protected void SendInternal(CSteamID host, byte[] msgBuffer)
    {
      if (!SteamManager.Initialized)
      {
        throw new ObjectDisposedException("Steamworks");
      }
      SteamNetworking.SendP2PPacket(host, msgBuffer, (uint)msgBuffer.Length, EP2PSend.k_EP2PSendReliable, SEND_INTERNAL);
    }

    protected bool ReceiveInternal(out uint readPacketSize, out CSteamID clientSteamID)
    {
      if (!SteamManager.Initialized)
      {
        throw new ObjectDisposedException("Steamworks");
      }
      return SteamNetworking.ReadP2PPacket(receiveBufferInternal, 1, out readPacketSize, out clientSteamID, SEND_INTERNAL);
    }

    protected void Send(CSteamID host, byte[] msgBuffer, int channel)
    {
      Debug.Assert(channel <= Channels.Length, $"Channel {channel} not configured for FizzySteamMirror.");
      if (!SteamManager.Initialized)
      {
        throw new ObjectDisposedException("Steamworks");
      }

      SteamNetworking.SendP2PPacket(host, msgBuffer, (uint)msgBuffer.Length, Channels[channel], channel);
    }

    protected bool Receive(out uint readPacketSize, out CSteamID clientSteamID, out byte[] receiveBuffer, int channel)
    {
      if (!SteamManager.Initialized)
      {
        throw new ObjectDisposedException("Steamworks");
      }

      uint packetSize;
      if (SteamNetworking.IsP2PPacketAvailable(out packetSize, channel) && packetSize > 0)
      {
        receiveBuffer = new byte[packetSize];
        return SteamNetworking.ReadP2PPacket(receiveBuffer, packetSize, out readPacketSize, out clientSteamID, channel);
      }

      receiveBuffer = null;
      readPacketSize = 0;
      clientSteamID = CSteamID.Nil;
      return false;
    }

    protected void CloseP2PSessionWithUser(CSteamID clientSteamID)
    {
      if (!SteamManager.Initialized)
      {
        throw new ObjectDisposedException("Steamworks");
      }
      SteamNetworking.CloseP2PSessionWithUser(clientSteamID);
    }
  }
}