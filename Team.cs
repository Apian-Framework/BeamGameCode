using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public enum TeamID {
        kSharks = 0,
        kCatfish = 1,
        kWhales = 2,
        kOrcas = 3
    }

    public class Team
    {
        public static readonly List<Team> teamData = new List<Team>() {
            new Team(TeamID.kSharks, "Sharks", "0xffff00"), // yellow
            new Team(TeamID.kCatfish,"Catfish", "0xff0000"), // red
            new Team(TeamID.kWhales,"Whales", "0x00ffff"), // cyan
            new Team(TeamID.kOrcas,"Orcas", "0x0000ff")  // blue                       
        };

        public TeamID TeamID;
        public string Name;
        public string Color;

        public Team(TeamID theId, string name, string color) 
        {
            TeamID = theId;
            Name = name;
            Color = color;
        }
    }
}