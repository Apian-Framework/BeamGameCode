using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamApianCreatorSez : BeamApian
    {
        public BeamApianCreatorSez(IBeamGameNet _gn,  IBeamAppCore _client) : base(_gn, _client)
        {
            // TODO: LeaderClock needs a way to set the leader. Currently uses group creator.
            ApianClock = new LeaderApianClock(this);
            //ApianClock = new CoopApianClock(this);  // Could use this, but LeaderClock seems mor sensible

            GroupMgr = new CreatorSezGroupManager(this);
        }

        // public override void SendObservation( ApianCoreMessage msg)
        // {
        //     if ( (GroupMgr?.GroupCreatorId != GameNet.LocalP2pId()))
        //     {
        //         // This next line is too verbose for even Debug-level
        //         //Logger.Debug($"SendRequestOrObservation() We are not server, so don't send observations.");
        //         return;
        //     }
        //     base.SendObservation(msg); // let this filter it too
        // }

    }
}