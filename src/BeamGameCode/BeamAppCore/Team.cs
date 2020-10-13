using System.Collections.Generic;
using UnityEngine;

namespace BeamGameCode
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
            new Team(TeamID.kSharks, "Sharks", "yellow"),
            new Team(TeamID.kCatfish,"Catfish", "red"),
            new Team(TeamID.kWhales,"Whales", "cyan"),
            new Team(TeamID.kOrcas,"Orcas", "blue"),
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