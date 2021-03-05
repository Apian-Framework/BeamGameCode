using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;

namespace BeamGameCode
{
    public interface IBeamGameNet : IApianGameNet
    {
        void CreateBeamNet(BeamGameNet.BeamNetCreationData createData);
        void JoinBeamNet(string netName);
        void CreateAndJoinGame(string gameName, BeamApian apian, string localData);
        void JoinExistingGame(ApianGroupInfo gameInfo, BeamApian apian, string localData );

        void SendBikeCreateDataReq(string groupId, IBike ib);
        void SendBikeCommandReq(string groupId, IBike bike, BikeCommand cmd);
        void SendBikeTurnReq(string groupId, IBike bike, long gameTime, TurnDir dir, Vector2 nextPt);

    }

    public class BeamGameNet : ApianGameNetBase, IBeamGameNet
    {
        public class BeamNetCreationData {}

        public BeamGameNet() : base()
        {
           // _MsgHandlers[BeamMessage.kBikeDataQuery] = (f,t,s,m) => this._HandleBikeDataQuery(f,t,s,m);
        }

        // Game P2pNetwork stuff
        public void CreateBeamNet(BeamNetCreationData createData)
        {
            base.CreateNetwork(createData);
        }

        public void JoinBeamNet(string netName )
        {
            // // TODO: clean this crap up!! &&&&&
            int pingMs = 2500;
            int dropMs = 5000;
            int timingMs = 15000;
            P2pNetChannelInfo chan = new P2pNetChannelInfo(netName, netName, dropMs, pingMs, timingMs);
            string beamNetworkHelloData = $"PeerId: {LocalP2pId()}"; // TODO: do something useful with this
            base.JoinNetwork(chan, beamNetworkHelloData);
        }


        public void JoinExistingGame(ApianGroupInfo gameInfo, BeamApian apian, string localData )
        {
            base.JoinExistingGroup(gameInfo, apian, localData);
        }


        public void CreateAndJoinGame(string gameName, BeamApian apian, string localData)
        {

        }

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
            logger.Info($"PostBikeCreateData(): {ib.bikeId}");
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