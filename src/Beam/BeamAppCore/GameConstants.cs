using UnityEngine;

namespace BeamGameCode
{
    public enum Heading
    {
        // z is North, x is East
        kNorth = 0,
        kEast = 1,
        kSouth = 2,
        kWest = 3,
        kCount = 4
    }

    public enum TurnDir
    {
        kStraight = 0,
        kLeft = 1,
        kRight = 2,
        kUnset = 3,
    }

    public enum BikeCommand
    {
        kNone = 0,
        kStop = 1,
        kGo =  2,
        kTurbo = 3,

    }

    public enum ScoreEvent // Is "event" a bad name (because C#)?
    {
        kClaimPlace = 0,
        kHitFriendPlace = 1,
        kHitEnemyPlace = 2,
        kOffMap = 3,
    }

    public static class GameConstants
    {
        public static readonly int[] eventScores = {
            11, // claimPlace:   Bike gets this much
            -511, // hitfriend   Bike loses this much, rest of team splits it.
            -1211, // hitEnemy   Bike loses this much, enemy team splits it
            0, // offMap (no score issue - you just die)
        };

        // NOTE: coordinates are LEFT-HANDED! A positive heading rotation, for instance, is clockwise from above.
        private static readonly float[] headingDegrees = {
            0f, 90f, 180f, 270f
        };

        public static Heading ReciprocalHeading(Heading h) => (Heading)(((int)h+2)%(int)Heading.kCount);

        public static float HeadingDegrees(Heading h) => headingDegrees[(int)h%4];

        private static readonly Vector3[] unitOffset3ForHeading = {
            // Unit velocity for given heading
            new Vector3(0, 0, 1),  // N
            new Vector3(1, 0, 0),  // E
            new Vector3(0, 0, -1),  // S
            new Vector3(-1, 0, 0),  // W
        };

        public static Vector3 UnitOffset3ForHeading(Heading h) => unitOffset3ForHeading[(int)h%4];

        private static readonly Vector2[] unitOffset2ForHeading = {
            // Unit velocity for given heading
            new Vector2(0, 1),  // N
            new Vector2(1, 0),  // E
            new Vector2(0, -1),  // S
            new Vector2(-1, 0),  // W
        };

        public static Vector2 UnitOffset2ForHeading(Heading h) => unitOffset2ForHeading[(int)h%4];

        public static string[] turnNames = {"Straight", "Left", "Right", "Unset"};

        private static readonly Heading[][] newHeadForTurn = {
            // newHead = GameConstants.newHeadForTurn[oldHead][turnDir];
            new Heading[] { Heading.kNorth, Heading.kWest, Heading.kEast }, // N
            new Heading[] { Heading.kEast, Heading.kNorth, Heading.kSouth }, // E
            new Heading[] { Heading.kSouth, Heading.kEast, Heading.kWest }, // S
            new Heading[] { Heading.kWest, Heading.kSouth, Heading.kNorth } // W
        };

        public static Heading NewHeadForTurn(Heading h, TurnDir t) => newHeadForTurn[(int)h%4][(int)t%3];
    }
}