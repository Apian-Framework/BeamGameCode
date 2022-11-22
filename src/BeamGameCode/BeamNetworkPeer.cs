using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamNetworkPeer
    {
        public string PeerAddr { get; private set;}
        public string Name { get; private set;}

        public BeamNetworkPeer(string peerAddr, string name)
        {
            PeerAddr = peerAddr;
            Name = name;
        }

        public static BeamNetworkPeer FromApianSerialized(string jsonData)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);
            return new BeamNetworkPeer(
                data[0] as string,
                data[1] as string);
        }

        public string ApianSerialized()
        {
            return  JsonConvert.SerializeObject(new object[]{
                PeerAddr,
                Name });
        }

    }
}
