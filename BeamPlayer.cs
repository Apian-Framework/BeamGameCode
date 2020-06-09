﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamBackend
{
    public class BeamPlayer : IApianStateData
    {
        public string PeerId { get; private set;}
        public string Name { get; private set;}

        public BeamPlayer(string peerId, string name)
        {
            PeerId = peerId;
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
                PeerId,
                Name });
        }

    }
}
