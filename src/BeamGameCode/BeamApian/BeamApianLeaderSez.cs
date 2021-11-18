using System.Collections.Generic;
using Newtonsoft.Json;
using GameNet;
using Apian;
using UniLog;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamApianLeaderSez : BeamApian
    {
        public BeamApianLeaderSez(IBeamGameNet _gn,  IBeamAppCore _client) : base(_gn, _client)
        {
            // TODO: LeaderClock needs a way to set the leader. Currently uses group creator.
            ApianClock = new LeaderApianClock(this);
            //ApianClock = new CoopApianClock(this);  // Could use this, but LeaderClock seems mor sensible

            GroupMgr = new LeaderSezGroupManager(this);
        }

        public override (bool, string) CheckQuorum()
        {
            BeamGameInfo bgi = GroupInfo as BeamGameInfo;

            if ( GroupMgr.GetMember(bgi.GroupCreatorId ) == null) // this is wrong. But leaderSez doesn't work anyway
                return (false, $"Creator Peer {bgi.GroupCreatorId} not present");

            return (true, "");
        }

    }
}