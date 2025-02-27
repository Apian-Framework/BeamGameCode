using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamGameCode
{
    public class BeamPlace : IApianCoreData
    {
        // PosHash is an int, upper word z, lower word x.
        public static int PosHashMaxSize = 16384; //
        public static long kLifeTimeMs = 15000; // TODO: Maybe should be per-bike and increase with time?

        public int xIdx; // x index into array.
        public int zIdx;
        public IBike bike;
        public long expirationTimeMs; // ApianTime
        protected Ground gInst; // I kinda hate having this here

        public class SerialArgs
        {
            public Dictionary<string,int> bikeIdxDict;
            public SerialArgs(Dictionary<string,int> bid) {bikeIdxDict=bid;}
        };
        public string ApianSerialized(object args)
        {
            SerialArgs sArgs = args as SerialArgs;
            // args.bikeIdxDict is a dictionary to map bikeIds to array indices in the Json for the bikes
            // It makes this Json a lot smaller

            return  JsonConvert.SerializeObject(new object[]{
                sArgs.bikeIdxDict[bike.bikeId],
                xIdx,
                zIdx,
                expirationTimeMs
                });
        }

        public static BeamPlace FromApianJson(string jsonData, List<string> bikeIdList, Dictionary<string, IBike> bikeDict)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);

            BeamPlace p = new BeamPlace();
            p.bike = bikeDict[ bikeIdList[(int)(long)data[0]] ];
            p.xIdx = (int)(long)data[1];
            p.zIdx = (int)(long)data[2];
            p.expirationTimeMs = (long)data[3];

            return p;
        }

        public int PosHash { get => xIdx + zIdx * PosHashMaxSize; }

        public static int MakePosHash(int xIdx, int zIdx) => xIdx + zIdx * PosHashMaxSize;

        public static void PosHashToIdx(int posHash, out int xIdx, out int zIdx)
        {
            zIdx = posHash / PosHashMaxSize;
            xIdx = posHash - zIdx * PosHashMaxSize;
        }

        public static bool AreAdjacent(BeamPlace p1, BeamPlace p2) => Math.Abs(p1.xIdx - p2.xIdx) + Math.Abs(p1.zIdx - p2.zIdx) == 1; // only one is 1


        public Vector2 GetPos(Ground g)
        {
            return PlacePos(g, xIdx,zIdx);
        }

        public static Vector2 PlacePos(Ground g, int x, int z)
        {
            return new Vector2(x*Ground.gridSize + g.minX,z*Ground.gridSize + g.minZ);
        }
    }
}
