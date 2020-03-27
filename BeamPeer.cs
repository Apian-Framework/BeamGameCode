﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public class BeamPeer
    {
        public string PeerId { get; private set;}    
        public string Name { get; private set;}
        public Team Team {get; private set;}

        public BeamPeer(string peerId, string name, Team t = null)
        { 
            PeerId = peerId;
            Name = name;
            Team = (t != null) ? t : Team.teamData[(int)UnityEngine.Random.Range(0,Team.teamData.Count)];;
        }

    }
}
