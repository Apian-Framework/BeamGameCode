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
            //ApianClock = new LeaderApianClock(this);
            ApianClock = new CoopApianClock(this);  // Could use this, but LeaderClock seems mor sensible

            GroupMgr = new CreatorSezGroupManager(this);
        }

        public override (bool, string) CheckQuorum()
        {
            BeamGameInfo bgi = GroupInfo as BeamGameInfo;

            if ( GroupMgr.GetMember(bgi.GroupCreatorId) == null)
                return (false, $"Creator Peer {bgi.GroupCreatorId} not present");

            return (true, "");
        }
    }
}