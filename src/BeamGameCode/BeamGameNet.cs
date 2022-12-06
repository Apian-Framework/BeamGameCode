using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;
using UniLog;
using static UniLog.UniLogger; // for SID()

#if !SINGLE_THREADED
using System.Threading.Tasks;
#endif

namespace BeamGameCode
{
    public interface IBeamGameNet : IApianGameNet
    {
        void JoinBeamNet(string netName, BeamNetworkPeer localPeer);
        void LeaveBeamNet();

        BeamGameInfo CreateBeamGameInfo( string gameName, string apianGroupType, GroupMemberLimits limits);
        void CreateAndJoinGame(BeamGameInfo gameInfo, BeamApian apian, string localData, bool joinAsValidator);
        void JoinExistingGame(BeamGameInfo gameInfo, BeamApian apian, string localData, bool joinAsValidator);
        void LeaveGame(string gameId);

        void SendBikeCreateDataReq(string groupId, IBike ib);
        void SendBikeCommandReq(string groupId, IBike bike, BikeCommand cmd);
        void SendBikeTurnReq(string groupId, IBike bike, long gameTime, TurnDir dir, Vector2 nextPt);

        // MultiThreaded - or at last uses System/Threading
#if !SINGLE_THREADED
        Task<PeerJoinedNetworkData> JoinBeamNetAsync(string netName, BeamNetworkPeer localPeer);
        Task<PeerJoinedGroupData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData, int timeoutMs, bool joinAsValidator);
        Task<PeerJoinedGroupData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData, int timeoutMs, bool joinAsValidator);
#endif
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
        }


        public void JoinBeamNet(string netName, BeamNetworkPeer localPeer )
        {
            P2pNetChannelInfo chan = new P2pNetChannelInfo(beamChannelData[kBeamNetworkChannelInfo]);
            chan.name = netName;
            chan.id = netName; // TODO: ID and name should be different
            string beamNetworkHelloData = JsonConvert.SerializeObject(localPeer);
            base.JoinNetwork(chan, beamNetworkHelloData);
        }

        public void LeaveBeamNet() => LeaveNetwork();

        public BeamGameInfo CreateBeamGameInfo(string gameName, string apianGroupType, GroupMemberLimits memberLimits)
        {
           string netName = p2p.GetNetworkChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"CreateBeamGameInfo() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return null;
            }

            P2pNetChannelInfo groupChanInfo = new P2pNetChannelInfo(beamChannelData[kBeamGameChannelInfo]);
            groupChanInfo.name = gameName;
            groupChanInfo.id = $"{netName}/{gameName}";  // FIXME: use IDs instead of names

            ApianGroupInfo groupInfo = new ApianGroupInfo(apianGroupType, groupChanInfo, LocalPeerAddr(), gameName, memberLimits);

            return new BeamGameInfo(groupInfo);
        }

        public void JoinExistingGame(BeamGameInfo gameInfo, BeamApian apian, string localData, bool joinAsValidator)
        {
            string netName = p2p.GetNetworkChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"JoinExistingGame() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return;
            }
            base.JoinExistingGroup(gameInfo, apian, localData, joinAsValidator);
        }

        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamApian apian, string localData, bool joinAsValidator)
        {
            string netName = p2p.GetNetworkChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"CreateAndJoinGame() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return;
            }

            base.CreateAndJoinGroup(gameInfo, apian, localData, joinAsValidator);
        }

#if !SINGLE_THREADED
        public async Task<PeerJoinedNetworkData> JoinBeamNetAsync(string netName, BeamNetworkPeer localPeer )
        {
            P2pNetChannelInfo chan = new P2pNetChannelInfo(beamChannelData[kBeamNetworkChannelInfo]);
            chan.name = netName;
            chan.id = netName;
            string beamNetworkHelloData = JsonConvert.SerializeObject(localPeer);
            return await JoinNetworkAsync(chan, beamNetworkHelloData);
        }

        public async Task<PeerJoinedGroupData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData, int timeoutMs, bool joinAsValidator)
        {
            return await base.JoinExistingGroupAsync(gameInfo, apian, localData, timeoutMs,  joinAsValidator);
        }

        public async Task<PeerJoinedGroupData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamApian apian, string localData, int timeoutMs, bool joinAsValidator)
        {
            return await base.CreateAndJoinGroupAsync(gameInfo, apian, localData, timeoutMs, joinAsValidator);
        }
#endif

        public void LeaveGame(string gameId) => LeaveGroup(gameId); // ApianGameNet.LeaveGroup()

        protected override IP2pNet P2pNetFactory(string localPeerAddress, string p2pConnectionString)
        {
            // P2pConnectionString is <p2p implmentation name>::<imp-dependent connection string>
            // Names are: p2ploopback, p2predis


            IP2pNetCarrier carrier = null;

            string[] parts = p2pConnectionString.Split(new string[]{"::"},StringSplitOptions.None); // Yikes! This is fugly.

            switch(parts[0])
            {
                case "p2predis":
                    carrier = new P2pRedis(parts[1]);
                    break;
                case "p2ploopback":
                    carrier = new P2pLoopback(null);
                    break;
                case "p2pmqtt":
                    carrier = new P2pMqtt(parts[1]);
                    break;
                // case "p2pactivemq":
                //     carrier = new P2pActiveMq(parts[1]);
                //     break;
                default:
                    throw( new Exception($"Invalid connection type: {parts[0]}"));
            }

            IP2pNet ip2p = new P2pNetBase(this, carrier, localPeerAddress);

            // TODO: Since C# ctors can't fail and return null we don;t have a generic
            // "It didn;t work" path. As it stands, the P2pNet ctor will throw and we'll crash.
            // That's probably OK for now - but this TODO is here to remind me later.

            return ip2p;
        }

        // Requests from BeamApplciation

        public void SendBikeCreateDataReq(string groupId, IBike ib)
        {
            logger.Info($"SendBikeCreateDataReq(): {SID(ib.bikeId)}");
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

       public void SendPauseReq(string groupId, string reason, string pauseId)
        {
            BeamApian apian = ApianInstances[groupId] as BeamApian;
            apian.SendPauseReq(apian.CurrentRunningApianTime(), reason, pauseId);
        }

       public void SendResumeReq(string groupId, string pauseId)
        {
            BeamApian apian = ApianInstances[groupId] as BeamApian;
            apian.SendResumeReq(apian.CurrentRunningApianTime(), pauseId);
        }


    }

}