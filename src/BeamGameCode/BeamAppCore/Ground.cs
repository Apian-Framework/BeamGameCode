using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamGameCode
{
    public partial class Ground : IApianCoreData
    {
        // North is Z, East is X,  Y is up
        public static float gridSize = 10f; // 10x10 squares

        public int BlockCntX;
        public int BlockCntZ;
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;

        public  int xAxisPoints => (int)((maxX - minX) / gridSize) + 1; // number of points along the axis
        public  int zAxisPoints => (int)((maxZ - minZ) / gridSize) + 1;

        public static Vector2 zeroPos = new Vector2(0f, 0f);

        protected Ground() {} // FIXME hide default constructor - just while changing some constsnts to isntance properties

        public Ground(int blockCntX, int blockCntZ)
        {
            BlockCntX = blockCntX;
            BlockCntZ = blockCntZ;

            maxX = gridSize * blockCntX/2;
            minX = -maxX;
            maxZ = gridSize * blockCntZ / 2;
            minZ = -maxZ;
        }

        // Custom compact json
        public static Ground FromApianJson(string jsonData)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);

            return new Ground(
                (int)(long)data[0],  // have to cast to long first, then int
                (int)(long)data[1]);
        }

        public string ApianSerialized(object args=null)
        {
            return  JsonConvert.SerializeObject(new object[]{
                BlockCntX,
                BlockCntZ });
        }

        public static Vector2 NearestGridPoint(Vector2 pos)
        {
            float invGridSize = 1.0f / gridSize;
            return new Vector2( Mathf.Round(pos.x * invGridSize) * gridSize, Mathf.Round(pos.y * invGridSize) * gridSize);
        }

        public (int,int) NearestGridIndices(Vector2 pos)
        {
            Vector2 gridPos  = NearestGridPoint(pos);
            return ((int)Mathf.Floor((gridPos.x - minX) / gridSize) , (int)Mathf.Floor((gridPos.y - minZ) / gridSize ));
        }

        public bool PointIsOnMap(Vector2 pt)
        {
            int xIdx = (int)Mathf.Floor((pt.x - minX) / gridSize );
            int yIdx = (int)Mathf.Floor((pt.y - minZ) / gridSize );
            return IndicesAreOnMap(xIdx, yIdx);
        }

        public bool IndicesAreOnMap(int xIdx, int yIdx)
        {
            return !(xIdx < 0 || yIdx < 0 || xIdx >= xAxisPoints || yIdx >= zAxisPoints);
        }
    }
}
