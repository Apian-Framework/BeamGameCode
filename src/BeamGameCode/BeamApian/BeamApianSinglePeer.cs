using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamApianSinglePeer : BeamApian
    {
        public BeamApianSinglePeer(IBeamGameNet _gn,  BeamAppCore _client) : base(_gn, _client)
        {
            ApianClock = new CoopApianClock(this); // TODO: This is wasteful. Needs a trivial single peer clock.
            GroupMgr = new SinglePeerGroupManager(this);
        }

        public override (bool, string) CheckQuorum()
        {
            return (true, ""); // always true
        }
    }
}