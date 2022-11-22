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
            GroupMgr = new LeaderSezGroupManager(this);
        }

        public override (bool, string) CheckQuorum()
        {
            BeamGameInfo bgi = GroupInfo as BeamGameInfo;

            if ( GroupMgr.GetMember(bgi.GroupCreatorAddr ) == null) // this is wrong. But leaderSez doesn't work anyway
                return (false, $"Creator Peer {bgi.GroupCreatorAddr} not present");

            return (true, "");
        }

    }
}