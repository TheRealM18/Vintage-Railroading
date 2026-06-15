using System;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VintageRailroading.Track;

namespace VintageRailroading
{
    /// <summary>
    /// Payload that ships the whole serialized TrackNetwork to clients.
    /// </summary>
    [ProtoContract]
    public class TrackNetworkPacket
    {
        [ProtoMember(1)]
        public byte[] Data;
    }

    /// <summary>
    /// Holds the world's TrackNetwork and persists it to the savegame as one
    /// blob. Other systems reach it via
    /// api.ModLoader.GetModSystem&lt;TrackNetworkManager&gt;().Network.
    ///
    /// The server is authoritative and loads/saves the network. It also SYNCS the
    /// network to clients over a network channel (on join + on demand), because the
    /// client needs the node/segment data to build train geometry locally. Without
    /// this, the client's Network is empty -> BuildGeometry returns null -> trains
    /// never render movement.
    /// </summary>
    public class TrackNetworkManager : ModSystem
    {
        private const string SaveKey = "vintagerailroading:network";
        private const string ChannelName = "vintagerailroading:tracknet";

        private ICoreServerAPI _sapi;
        private ICoreClientAPI _capi;
        private IServerNetworkChannel _serverChannel;
        private IClientNetworkChannel _clientChannel;

        public TrackNetwork Network { get; private set; } = new TrackNetwork();

        public override double ExecuteOrder() => 0.2;

        // Register the channel on BOTH sides.
        public override void Start(ICoreAPI api)
        {
            api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<TrackNetworkPacket>();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            _serverChannel = api.Network.GetChannel(ChannelName);
            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
            // Send the network to each player when they finish joining.
            api.Event.PlayerJoin += OnPlayerJoin;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            _capi = api;
            _clientChannel = api.Network.GetChannel(ChannelName)
                .SetMessageHandler<TrackNetworkPacket>(OnNetworkFromServer);
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            SendNetworkTo(player);
        }

        /// <summary>Server: serialize + send the current network to one player.</summary>
        public void SendNetworkTo(IServerPlayer player)
        {
            if (Network == null) return;
            try
            {
                var pkt = new TrackNetworkPacket { Data = SerializerUtil.Serialize(Network) };
                _serverChannel.SendPacket(pkt, player);
            }
            catch (Exception e)
            {
                VrrDebug.LogError(_sapi, "failed to send track network: {0}", e.Message);
            }
        }

        /// <summary>Server: re-broadcast the network to everyone (call after edits).</summary>
        public void BroadcastNetwork()
        {
            if (Network == null || _serverChannel == null) return;
            try
            {
                var pkt = new TrackNetworkPacket { Data = SerializerUtil.Serialize(Network) };
                _serverChannel.BroadcastPacket(pkt);
            }
            catch (Exception e)
            {
                VrrDebug.LogError(_sapi, "failed to broadcast track network: {0}", e.Message);
            }
        }

        private void OnNetworkFromServer(TrackNetworkPacket pkt)
        {
            if (pkt?.Data == null) return;
            try
            {
                Network = SerializerUtil.Deserialize<TrackNetwork>(pkt.Data) ?? new TrackNetwork();
                Network.BuildIndex();
                VrrDebug.Log(_capi, "received track network from server (nodes/segments synced).");
            }
            catch (Exception e)
            {
                VrrDebug.LogError(_capi, "failed to apply synced track network: {0}", e.Message);
            }
        }

        private void OnSaveGameLoaded()
        {
            byte[] data = _sapi.WorldManager.SaveGame.GetData(SaveKey);
            if (data == null)
            {
                Network = new TrackNetwork();
            }
            else
            {
                try
                {
                    Network = SerializerUtil.Deserialize<TrackNetwork>(data) ?? new TrackNetwork();
                }
                catch (Exception e)
                {
                    VrrDebug.LogError(_sapi, "failed to load track network, starting fresh: {0}", e.Message);
                    Network = new TrackNetwork();
                }
            }
            Network.BuildIndex();
        }

        private void OnGameWorldSave()
        {
            if (Network == null) return;
            try
            {
                _sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(Network));
            }
            catch (Exception e)
            {
                VrrDebug.LogError(_sapi, "failed to save track network: {0}", e.Message);
            }
        }
    }
}