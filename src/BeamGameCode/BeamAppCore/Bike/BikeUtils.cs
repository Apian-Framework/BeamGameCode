using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeamGameCode
{
    public class DirAndScore { public TurnDir turnDir; public int score; };

    public static class BikeUtils
    {
        public static Vector2 UpcomingGridPoint(Vector2 curPos, Heading curHead)
        {
            // it's either the current closest point (if direction to it is the same as heading)
            // or is the closest point + gridSize*unitOffsetForHeading[curHead] if closest point is behind us
            Vector2 point = Ground.NearestGridPoint(curPos);
            if (Vector2.Dot(GameConstants.UnitOffset2ForHeading(curHead), point - curPos) < 0)
            {
                point += GameConstants.UnitOffset2ForHeading(curHead) * Ground.gridSize;
            }
            return point;
        }


        public static int ScoreForPoint(Ground g, Vector2 point, BeamPlace place)
        {
            return g.PointIsOnMap(point) ? (place == null ? 5 : 1) : 0; // 5 pts for a good place, 1 for a claimed one, zero for off-map
        }

        public static List<Vector2> PossiblePointsForPointAndHeading(Vector2 curPtPos, Heading curHead)
        {
            // returns a list of grid positions where you could go next if you are headed for one with the given heading
            // The entries correspond to turn directions (none, left, right)
            // TODO use something like map() ?
            return new List<Vector2> {
                curPtPos + GameConstants.UnitOffset2ForHeading(GameConstants.NewHeadForTurn(curHead, TurnDir.kStraight))*Ground.gridSize,
                curPtPos + GameConstants.UnitOffset2ForHeading(GameConstants.NewHeadForTurn(curHead, TurnDir.kLeft))*Ground.gridSize,
                curPtPos + GameConstants.UnitOffset2ForHeading(GameConstants.NewHeadForTurn(curHead, TurnDir.kRight))*Ground.gridSize,
            };
        }

        public static TurnDir TurnTowardsPos(Vector2 targetPos, Vector2 curPos, Heading curHead)
        {
            Vector2 bearing = targetPos - curPos;
            float turnAngleDeg = Vector2.SignedAngle(GameConstants.UnitOffset2ForHeading(curHead), bearing);
            //Debug.Log(string.Format("Pos: {0}, Turn Angle: {1}", curPos, turnAngleDeg));
            return turnAngleDeg > 45f ? TurnDir.kLeft : (turnAngleDeg < -45f ? TurnDir.kRight : TurnDir.kStraight);
        }
        public static MoveNode BuildMoveTree(BeamCoreState g, Vector2 curPos, Heading curHead, int depth, List<Vector2> otherBadPos = null)
        {
            Vector2 nextPos = UpcomingGridPoint(curPos, curHead);
            MoveNode root = MoveNode.GenerateTree(g, nextPos, curHead, 1, otherBadPos);
            return root;
        }

        public static List<DirAndScore> TurnScores(MoveNode moveTree)
        {
            return moveTree.next.Select(n => new DirAndScore { turnDir = n.dir, score = n.BestScore() }).ToList();
        }

        public class MoveNode
        {
            public TurnDir dir; // the turn direction that got to here (index in parent's "next" list)
            public Vector2 pos;
            public BeamPlace place;
            public int score;
            public List<MoveNode> next; // length 3

            public MoveNode(BeamCoreState gData, Vector2 p, Heading head, TurnDir d, int depth, List<Vector2> otherClaimedPos)
            {
                pos = p;
                dir = d; // for later lookup
                place = gData.GetPlace(p);
                score = ScoreForPoint(gData.Ground, pos, place);
                if (score == 0 && otherClaimedPos.Any(op => op.Equals(pos))) // TODO: make prettier
                    score = 1; // TODO: use named scoring constants
                next = depth < 1 ? null : BikeUtils.PossiblePointsForPointAndHeading(pos, head)
                        .Select((pt, childTurnDir) => new MoveNode(gData,
                        pos + GameConstants.UnitOffset2ForHeading(GameConstants.NewHeadForTurn(head, (TurnDir)childTurnDir)) * Ground.gridSize,
                        head,
                        (TurnDir)childTurnDir,
                        depth - 1,
                        otherClaimedPos))
                        .ToList();
            }

            public static MoveNode GenerateTree(BeamCoreState gd, Vector2 rootPos, Heading initialHead, int depth, List<Vector2> otherBadPos)
            {
                return new MoveNode(gd, rootPos, initialHead, TurnDir.kStraight, depth, otherBadPos);
            }

            public int BestScore()
            {
                // Express this trivially until I understand th epossibilities
                if (score == 0) // This kills you, why look further?
                    return 0;

                if (next == null) // no kids
                    return score;

                // return score + next.Select( n => n.BestScore()).OrderBy(i => i).Last();
                return score + next.Select(n => n.BestScore()).Where(i => i > 0).Sum();
            }

        }
    }
}