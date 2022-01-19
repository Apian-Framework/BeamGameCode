using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;
using UniLog;
using static UniLog.UniLogger; // for SID()

#if !SINGLE_THREADEDxxx
using System.Threading.Tasks;
#endif

namespace BeamGameCode
{
    public interface IBeamGameNet : IApianGameNet
    {
        //void CreateBeamNet(BeamGameNet.BeamNetCreationData createData);
        void JoinBeamNet(string netName, BeamNetworkPeer localPeer);
        Task<PeerJoinedNetworkData> JoinBeamNetAsync(string netName, BeamNetworkPeer localPeer);
        void LeaveBeamNet();

        BeamGameInfo CreateBeamGameInfo( string gameName, string apianGroupType);
        void CreateAndJoinGame(BeamGameInfo gameInfo, BeamApian apian, string localData);
        Task<PeerJoinedGroupData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData);
        void JoinExistingGame(BeamGameInfo gameInfo, BeamApian apian, string localData );
        Task<PeerJoinedGroupData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData );
        void LeaveGame(string gameId);

        void SendBikeCreateDataReq(string groupId, IBike ib);
        void SendBikeCommandReq(string groupId, IBike bike, BikeCommand cmd);
        void SendBikeTurnReq(string groupId, IBike bike, long gameTime, TurnDir dir, Vector2 nextPt);

    }

    public class BeamGameNet : ApianGameNetBase, IBeamGameNet
    {
        public class BeamNetCreationData {}

        public const int kBeamNetworkChannelInfo = 0;
        public const int kBeamGameChannelInfo = 1;
        public const int kBeamChannelInfoCount = 2;

        protected P2pNetChannelInfo[] beamChannelData =  {
            //   name, id, dropMs, pingMs, missingMs, syncMs, maxPeers
            new P2pNetChannelInfo(null, null, 10000, 3000, 5000,     0, 0 ), // Main network channel (no clock sync)
            new P2pNetChannelInfo(null, null, 15000, 4000, 4500, 15000, 0 )  // gameplay channels - should drop from main channel before it happens here
        };

        public BeamGameNet() : base()
        {
           // _MsgHandlers[BeamMessage.kBikeDataQuery] = (f,t,s,m) => this._HandleBikeDataQuery(f,t,s,m);
        }

        // Game P2pNetwork stuff
        // public void CreateBeamNet(BeamNetCreationData createData)
        // {
        //     base.CreateNetwork(createData);
        // }

        public void JoinBeamNet(string netName, BeamNetworkPeer localPeer )
        {
            P2pNetChannelInfo chan = new P2pNetChannelInfo(beamChannelData[kBeamNetworkChannelInfo]);
            chan.name = netName;
            chan.id = netName;
            string beamNetworkHelloData = JsonConvert.SerializeObject(localPeer);
            base.JoinNetwork(chan, beamNetworkHelloData);
        }

        public async Task<PeerJoinedNetworkData> JoinBeamNetAsync(string netName, BeamNetworkPeer localPeer )
        {
            P2pNetChannelInfo chan = new P2pNetChannelInfo(beamChannelData[kBeamNetworkChannelInfo]);
            chan.name = netName;
            chan.id = netName;
            string beamNetworkHelloData = JsonConvert.SerializeObject(localPeer);
            return await JoinNetworkAsync(chan, beamNetworkHelloData);
        }

        public void LeaveBeamNet() => LeaveNetwork();

        public BeamGameInfo CreateBeamGameInfo(string gameName, string apianGroupType)
        {
           string netName = p2p.GetMainChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"CreateBeamGameInfo() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return null;
            }

            P2pNetChannelInfo groupChanInfo = new P2pNetChannelInfo(beamChannelData[kBeamGameChannelInfo]);
            groupChanInfo.name = gameName;
            groupChanInfo.id = $"{netName}/{gameName}";

            ApianGroupInfo groupInfo = new ApianGroupInfo(apianGroupType, groupChanInfo, LocalP2pId(), gameName);

            groupInfo.GroupParams["MaxPlayers"] = "4";

            return new BeamGameInfo(groupInfo);
        }

        public void JoinExistingGame(BeamGameInfo gameInfo, BeamApian apian, string localData )
        {
            string netName = p2p.GetMainChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"JoinExistingGame() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return;
            }
            base.JoinExistingGroup(gameInfo, apian, localData);
        }

        public async Task<PeerJoinedGroupData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData )
        {
            return await base.JoinExistingGroupAsync(gameInfo, apian, localData);
        }

        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamApian apian, string localData)
        {
            string netName = p2p.GetMainChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"CreateAndJoinGame() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return;
            }

            base.CreateAndJoinGroup(gameInfo, apian, localData);

        }

        public async Task<PeerJoinedGroupData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData)
        {
            return await base.CreateAndJoinGroupAsync(gameInfo, apian, localData);
        }


        public void LeaveGame(string gameId) => LeaveGroup(gameId); // ApianGameNet.LeaveGroup()

        protected override IP2pNet P2pNetFactory(string p2pConnectionString)
        {
            // P2pConnectionString is <p2p implmentation name>::<imp-dependent connection string>
            // Names are: p2ploopback, p2predis

            IP2pNet ip2p = null;
            string[] parts = p2pConnectionString.Split(new string[]{"::"},StringSplitOptions.None); // Yikes! This is fugly.

            switch(parts[0])
            {
                case "p2predis":
                    ip2p = new P2pRedis(this, parts[1]);
                    break;
                case "p2ploopback":
                    ip2p = new P2pLoopback(this, null);
                    break;
                case "p2pmqtt":
                    ip2p = new P2pMqtt(this, parts[1]);
                    break;
                // case "p2pactivemq":
                //     p2p = new P2pActiveMq(this, parts[1]);
                //     break;
                default:
                    throw( new Exception($"Invalid connection type: {parts[0]}"));
            }

            // TODO: Since C# ctors can't fail and return null we don;t have a generic
            // "It didn;t work" path. As it stands, the P2pNet ctor will throw and we'll crash.
            // That's probably OK for now - but this TODO is here to remind me later.

            return ip2p;
        }

        // Requests from BeamApplciation

        public void SendBikeCreateDataReq(string groupId, IBike ib)
        {
            logger.Info($"PostBikeCreateData(): {SID(ib.bikeId)}");
            BeamApian apian = ApianInstances[groupId] as BeamApian;
            apian.SendBikeCreateReq(apian.CurrentRunningApianTime(), ib);
        }

        public void SendBikeCommandReq(string groupId, IBike bike, BikeCommand cmd)
        {
            // TODO: BikeCommand is a bad name - these are NOT Apian Commands
            BeamApian apian = ApianInstances[groupId] as BeamApian;
            apian.SendBikeCommandReq(apian.CurrentRunningApianTime(), bike, cmd, (bike as BaseBike).UpcomingGridPoint(bike.basePosition));
        }

       public void SendBikeTurnReq(string groupId, IBike bike, long gameTime, TurnDir dir, Vector2 nextPt)
        {
            // nextPt is not strinctly necessary, but gets used to make sure
            // message recipeitns agree where the  bike is
            BeamApian apian = ApianInstances[groupId] as BeamApian;
            apian.SendBikeTurnReq(gameTime, bike, dir, nextPt);
        }

    }

}