using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamPlayer : IApianCoreData
    {
        public string PeerAddr { get; private set;}
        public string Name { get; private set;}

        public BeamPlayer(string peerAddr, string name)
        {
            PeerAddr = peerAddr;
            Name = name;
        }

        // Custom compact json
        // TODO: set up params to make more compact.
        public static BeamPlayer FromApianJson(string jsonData)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);
            return new BeamPlayer(
                data[0] as string,
                data[1] as string);
        }

        public string ApianSerialized(object args=null)
        {
            return  JsonConvert.SerializeObject(new object[]{
                PeerAddr,
                Name });
        }

    }
}
