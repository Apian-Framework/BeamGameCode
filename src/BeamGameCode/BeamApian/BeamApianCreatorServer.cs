using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamApianCreatorServer : BeamApian
    {
        public BeamApianCreatorServer(IBeamGameNet _gn,  IBeamAppCore _client) : base(_gn, _client)
        {
            GroupMgr = new LeaderSezGroupManager(this);
        }

        public override void SendObservation( ApianCoreMessage msg)
        {
            if ( (GroupMgr?.GroupCreatorId != GameNet.LocalP2pId()))
            {
                // This next line is too verbose for even Debug-level
                //Logger.Debug($"SendRequestOrObservation() We are not server, so don't send observations.");
                return;
            }
            base.SendObservation(msg); // let this filter it too
        }

    }
}