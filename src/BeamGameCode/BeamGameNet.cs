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

    }

    public class BeamGameNet : ApianGameNetBase, IBeamGameNet
    {

        public BeamGameNet() : base()
        {
           // _MsgHandlers[BeamMessage.kBikeDataQuery] = (f,t,s,m) => this._HandleBikeDataQuery(f,t,s,m);
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

    }

}