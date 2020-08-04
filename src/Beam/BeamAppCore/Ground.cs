using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Apian;
using UnityEngine;

namespace BeamGameCode
{
    public partial class Ground
    {
        // North is Z, East is X,  Y is up
        public static float gridSize = 10f; // assume a square grid
        public static float minX = -500f;
        public static float maxX = 500f;
        public static float minZ = -500f;
        public static float maxZ = 500f;

        public static readonly int pointsPerAxis = 101;

        public static Vector2 zeroPos = new Vector2(0f, 0f);

        public Ground()
        {

        }

        public static Vector2 NearestGridPoint(Vector2 pos)
        {
            float invGridSize = 1.0f / gridSize;
            return new Vector2( Mathf.Round(pos.x * invGridSize) * gridSize, Mathf.Round(pos.y * invGridSize) * gridSize);
        }

        public static (int,int) NearestGridIndices(Vector2 pos)
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
            return !(xIdx < 0 || yIdx < 0 || xIdx >= pointsPerAxis || yIdx >= pointsPerAxis);
        }
    }
}
