using System.Net.WebSockets;
using System;
using System.Collections.Generic;
using Apian;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public static class BeamApianFactory
    {
        public static readonly List<string> ApianGroupTypes = new List<string>()
        {
            SinglePeerGroupManager.kGroupType,
            CreatorSezGroupManager.kGroupType,
            LeaderSezGroupManager.kGroupType
        };

        public static BeamApian Create(string apianGroupType, IBeamGameNet beamGameNet, BeamAppCore appCore)
        {
            BeamApian result;
            switch (apianGroupType)
            {
            case SinglePeerGroupManager.kGroupType:
                result = new BeamApianSinglePeer(beamGameNet, appCore);
                break;
            case CreatorSezGroupManager.kGroupType:
                result =  new BeamApianCreatorSez(beamGameNet, appCore);
                break;
            case LeaderSezGroupManager.kGroupType:
                result =  new BeamApianLeaderSez(beamGameNet, appCore);
                break;
            default:
                UniLogger.GetLogger("Apian").Warn($"BeamApianFactory.Create() Unknown GroupType: {apianGroupType}");
                result = null;
                break;
            }
            return result;
        }
    }

}