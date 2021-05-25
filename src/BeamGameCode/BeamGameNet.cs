using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;
using UniLog;
using static UniLog.UniLogger; // for SID()

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
        void JoinExistingGame(BeamGameInfo gameInfo, BeamApian apian, string localData );
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
            return await base.JoinNetworkAsync(chan, beamNetworkHelloData);
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

            base.JoinExistingGroup(gameInfo.GroupInfo, apian, localData);
        }

        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamApian apian, string localData)
        {
            string netName = p2p.GetMainChannel()?.Name;
            if (netName == null)
            {
                logger.Error($"CreateAndJoinGame() - Must join network first"); // TODO: probably ought to assert? Can this be recoverable?
                return;
            }

            base.CreateAndJoinGroup(gameInfo.GroupInfo, apian, localData);

        }

        public void LeaveGame(string gameId) => LeaveGroup(gameId); // ApianGameNet.LeaveGroup()

        protected override IP2pNet P2pNetFactory(string p2pConnectionString)
        {
            // P2pConnectionString is <p2p implmentation name>::<imp-dependent connection string>
            // Names are: p2ploopback, p2predis

            IP2pNet ip2p = null;
            string[] parts = p2pConnectionString.Split(new string[]{"::"},StringSplitOptions.None); // Yikes! This is fugly.

            switch(parts[0].ToLower())
            {
                case "p2predis":
                    ip2p = new P2pRedis(this, parts[1]);
                    break;
                case "p2ploopback":
                    ip2p = new P2pLoopback(this, null);
                    break;
                // case "p2pactivemq":
                //     p2p = new P2pActiveMq(this, parts[1]);
                //     break;
                default:
                    throw( new Exception($"Invalid connection type: {parts[0]}"));
            }

            if (ip2p == null)
                throw( new Exception("p2p Connect failed"));

            return ip2p;
        }

        public override ApianMessage DeserializeApianMessage(string msgType, string msgJSON)
        {
            // TODO: can I do this without decoding it twice?
            // One option would be for the deifnition of ApianMessage to have type and subType,
            // but I'd rather just decode it smarter
            return BeamApianMessageDeserializer.FromJSON(msgType, msgJSON);
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