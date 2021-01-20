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
        public BeamApianSinglePeer(IBeamGameNet _gn,  IBeamAppCore _client) : base(_gn, _client)
        {
            GroupMgr = new SinglePeerGroupManager(this);
        }


    }

}