using System.Runtime.ConstrainedExecution;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;
using Apian;


namespace BeamGameCode
{
    public class BaseBike : IBike, IApianCoreData
    {
        // NOTE: 2D position: x => east, y => north (in 3-space z is north and y is up)
        public const int kStartScore = 2000;
        public static readonly float length = 2.0f;
        public static readonly float defaultSpeed =  15.0f;

        // Constant for bike lifetime
        public string bikeId {get; private set;}
        public string peerId {get; private set;}
        public string name {get; private set;}
        public Team team {get; private set;}
        public string ctrlType {get; private set;}

        // State vars
        public int score {get; set;}
        public float speed { get; private set;} = 0;

        // position and heading have both "base" entries and sometimes "optimistic" entries
        // the idea is that if we locally see the time when a remote bike would pass by a grid point
        // and we have a "pendingTurn" entry for it - we should go ahead and make use of that information
        // rather than waiting for the place[Hit|Claimed]Command.
        // On the other hand, if at about that time an ApianCheckpontRequest arrives we really want to
        // be sending the base data, not the optimistic stuff, since otherwise the state hash won't be
        // correct for that epoch


        public long baseTime {get; private set;}
        public Vector2 basePosition {get; private set;} = Vector2.zero; // always on the grid
        public Heading baseHeading { get; private set;} = Heading.kNorth;
        public TurnDir basePendingTurn { get; private set;} = TurnDir.kUnset; // set this and turn will start at next grid point

        // TODO: If this works well, move these properties into a class
        public long optTime {get; private set;}
        public Vector2 optPosition {get; private set;} = Vector2.zero; // always on the grid
        public Heading optHeading { get; private set;} = Heading.kNorth;
        public TurnDir optPendingTurn { get; private set;} = TurnDir.kUnset; // set this and turn will start at next grid point

        public BeamCoreState gameData = null;
        public UniLogger logger;


        public BaseBike( BeamCoreState gData, string _id, string _peerId, string _name, Team _team, string ctrl, long initialTime, Vector2 initialPos, Heading head)
        {
            gameData = gData;
            bikeId = _id;
            peerId = _peerId;
            name = _name;
            team = _team;
            basePosition = initialPos;
            baseTime = initialTime;
            baseHeading = head;
            ctrlType = ctrl;
            score = kStartScore;
            logger = UniLogger.GetLogger("BaseBike");
        }


        public class SerialArgs
        {
            public Dictionary<string,int> peerIdxDict;
            public long curApianTime;
            public long cpTimeStamp;
            public SerialArgs(Dictionary<string,int> pid, long at, long ts) {peerIdxDict=pid; curApianTime=at; cpTimeStamp=ts;}
        };

        private long _RoundToNearest(long interval, long inVal)
        {
            return ((inVal+interval/2) / interval) * interval;
        }

        public string ApianSerialized(object args)
        {
            SerialArgs sArgs = args as SerialArgs;

            // args.peerIdxDict is a dictionary to map peerIds to array indices in the Json for the peers
            // It makes this Json a lot smaller

            // We can deal (mostly) with time differences from one machine to another by
            // replacing the bike's position with the position of the last gridpoint crossed
            // and the apianTime when it was there (assuming current speed.) Or it's
            //  actual position and 0 if the bike's not moving.


            // // WHile I'm debugging. These are in grid-index space (tomake it easier to compare withthe places list)
            // float indexX =   (position.x - Ground.minX) / Ground.gridSize;
            // float indexZ =   (position.y - Ground.minZ) / Ground.gridSize;

            return  JsonConvert.SerializeObject(new object[]{
                    bikeId,
                    sArgs.peerIdxDict[peerId],
                    name,
                    team.TeamID,
                    ctrlType,
                    (long)(basePosition.x * 1000f), // integer mm
                    (long)(basePosition.y * 1000f),
                    baseTime, // Do I need to round this? Shouldn't have to, but: _RoundToNearest(100, timeAtPoint);
                    baseHeading,
                    speed,
                    score,
                    basePendingTurn
                 });
        }

        public static BaseBike FromApianJson(string jsonData, BeamCoreState gData, List<string> peerIdList, long timeStamp)
        {
            object[] data = JsonConvert.DeserializeObject<object[]>(jsonData);

            Vector2 lastGridPos = new Vector2((long)data[5]*.001f, (long)data[6]*.001f);
            long lastGridTime = (long)data[7];
            Heading head = (Heading)(long)data[8];

            float speed = (float)(double)data[9];
            int score = (int)(long)data[10];
            TurnDir pendingTurn = (TurnDir)(long)data[11];

            float secsSinceGrid = (timeStamp - lastGridTime ) * .001f;

            gData.Logger.Info($"BaseBike FromApianJson() - Id: {(string)data[0]} curTime: {timeStamp} timeAtPos: {lastGridTime} lastGridPos: {lastGridPos.ToString()}");

            BaseBike bb = new BaseBike(
                gData,
                (string)data[0], // bikeId
                peerIdList[(int)(long)data[1]], // peerId
                (string)data[2], // _name
                Team.teamData[(int)(long)data[3]], // Team
                (string)data[4],  // ctrl,
                lastGridTime, // timeAtPosition
                lastGridPos, // current pos
                head); //  head)

            bb.speed = speed;
            bb.score = score;
            bb.basePendingTurn = pendingTurn;

            return bb;
        }


        // Commands from outside

        // public void Loop(float secs, long frameTimeMs)
        // {
        //     //logger.Debug($"Loop(). Bike: {bikeId} Speed: {speed})");
        //     _updatePosition(secs, frameTimeMs);
        // }

        // public Vector2 Position(long curMs)
        // {
        //     if (optTime == 0)
        //     {
        //         // use base
        //         float deltaSecs = (curMs - baseTime) * .001f;
        //         return basePosition +  GameConstants.UnitOffset2ForHeading(baseHeading) * (deltaSecs * speed);
        //     }
        //     float deltaSecs2 = (curMs - optTime) * .001f;
        //     return optPosition +  GameConstants.UnitOffset2ForHeading(optHeading) * (deltaSecs2 * speed);
        // }

        public BikeDynState DynamicState(long curTimeMs)
        {
            long gridTime = optTime == 0 ? baseTime : optTime;
            Vector2 gridPos = optTime == 0 ? basePosition : optPosition;
            Heading curHead = optTime == 0 ? baseHeading : optHeading;
            float deltaSecs= (curTimeMs - gridTime) * .001f;
            Vector2 bikePos =  gridPos +  GameConstants.UnitOffset2ForHeading(curHead) * (deltaSecs * speed);
            TurnDir curPendingTurn = optTime == 0 ? basePendingTurn : optPendingTurn;
            return new BikeDynState(bikePos, curHead, speed, score, curPendingTurn);
        }

        public void Loop(long apianTime)
        {
            _checkPosition(apianTime);
        }

        public void AddScore(int val) => score += val;

        public void ApplyTurn(TurnDir dir, Heading entryHeading, Vector2 nextPt,  long cmdTime, BeamMessage.BikeState reportedState)
        {
            // TODO: &&&& reported state really should not be there.

            Vector2 testPt = UpcomingGridPoint(basePosition); // use the last logged position
            if (!testPt.Equals(nextPt))
            {
                logger.Warn($"ApplyTurn(): {(nextPt.ToString())} is the wrong upcoming point for bike: {bikeId}");
                logger.Warn($"We think it should be {(testPt.ToString())}");
                logger.Warn($"Reported State:\n{JsonConvert.SerializeObject(reportedState)}");
                logger.Warn($"Actual State:\n{JsonConvert.SerializeObject(new BeamMessage.BikeState(this)) }");

            }
            basePendingTurn = dir;
        }

        public void ApplyCommand(BikeCommand cmd, Vector2 nextPt, long cmdTime)
        {
            if (!UpcomingGridPoint(basePosition).Equals(nextPt))
                logger.Warn($"ApplyCommand(): wrong upcoming point for bike: {bikeId}");

            switch(cmd)
            {
            case BikeCommand.kStop:
                speed = 0;
                break;
            case BikeCommand.kGo:
                speed = defaultSpeed;
                break;
            default:
                logger.Warn($"ApplyCommand(): Unknown BikeCommand: {cmd}");
                break;
            }
        }

        private void _checkPosition(long apianTime)
        {
            if (baseTime == 0) // TODO: get rid of this one the changeover is complete
                logger.Error($"_checkPosition() Bike: {bikeId} TimeAtPosition UNITITIALIZED!");

            float secs = (apianTime - baseTime) * .001f;

            if (secs <= 0 || speed == 0) // nothing can have happened
                return;

            Vector2 upcomingPoint = UpcomingGridPoint(basePosition); // using the last logged position
            float timeToPoint = Vector2.Distance(basePosition, upcomingPoint) / speed;

            Vector2 newPos = basePosition;
            Heading newHead = baseHeading;

            // Note that this assumes that secs < timeToCrossAGrid
            // TODO: should it handle longer times?
            if (secs >= timeToPoint)
            {
                //logger.Verbose($"_checkPosition() Bike: {bikeId} MsToPoint: {(long)(timeToPoint*1000)}");
                long timeAtPoint = baseTime + (long)(timeToPoint*1000);
                newPos =  upcomingPoint;
                newHead = GameConstants.NewHeadForTurn(baseHeading, basePendingTurn);
                DoAtGridPoint(upcomingPoint, baseHeading, newHead, timeAtPoint);

                // pre-entively update the next grid position
                // There will be an observation reported arriving withte same data
                // but we're better off assuming it'll match and fixing it later than
                // waiting for it
                _updatePosition(newPos, newHead, timeAtPoint);
            }
        }

        private void _updatePosition(Vector2 pos, Heading head,  long apianTime)
        {
            optTime = apianTime;
            optPendingTurn = TurnDir.kUnset;
            optHeading = head;
            optPosition = pos;
            logger.Verbose($"_updatePosition() Bike: {bikeId}, Pos: {pos.ToString()} Head: {baseHeading.ToString()}");
        }

        // &&&&&&&&&&&&& remove
        // private void _updatePosition(float secs, long frameApianTime)
        // {
        //     if (secs == 0 || speed == 0)
        //         return;

        //     Vector2 upcomingPoint = UpcomingGridPoint();
        //     float timeToPoint = Vector2.Distance(position, upcomingPoint) / speed;

        //     Vector2 newPos = position;
        //     Heading newHead = heading;

        //     if (secs >= timeToPoint)
        //     {
        //         logger.Debug($"_updatePosition() Bike: {bikeId} MsToPoint: {(long)(timeToPoint*1000)}");
        //         secs -= timeToPoint;
        //         newPos =  upcomingPoint;
        //         newHead = GameConstants.NewHeadForTurn(heading, pendingTurn);
        //         pendingTurn = TurnDir.kUnset;
        //         DoAtGridPoint(upcomingPoint, heading, newHead, frameApianTime);// + (long)(timeToPoint*1000)); // Using the offset makes odd things happen on the server
        //         heading = newHead;
        //     }

        //     newPos += GameConstants.UnitOffset2ForHeading(heading) * secs * speed;

        //     position = newPos;
        // }

        // private float _rollbackTime(float secs)
        // {
        //     // Propagate the bike backwards in time by "secs" or almost the length of time that
        //     // takes it backwards to the previous point - whichever is shorter
        //     // This is to try to minimize message delays.
        //     // If, for instance, a bike command is received that we know happened .08 secs ago,
        //     // then the code handling the command can roll the bike back, apply the ecommand, and then
        //     // call bike.update(rolledBackTime) to have effectively back-applied the command.
        //     // it's not really safe to go backwards across a gridpoint, so that's as far as we'll go back.
        //     // It returns the amount of time rolled back as a positive float.
        //     if (speed == 0 || secs <= 0)
        //         return 0;
        //     Vector2 upcomingPoint = UpcomingGridPoint();
        //     float timeToNextPoint = Vector2.Distance(position, upcomingPoint) / speed;
        //     float timeSinceLastPoint = Mathf.Max(0,((Ground.gridSize * .8f) / speed) - timeToNextPoint); // Note QUITE all the way back
        //     secs = Mathf.Min(secs, timeSinceLastPoint);
        //     position -= GameConstants.UnitOffset2ForHeading(heading) * secs * speed;
        //     return secs;
        // }

        // private float _rollbackTime(float secs)
        // {
        //     // Propagate the bike backwards in time by "secs" to apply a command that should have happened in the past
        //     // (See above for a version that tries to avoid crossing gridpoints - this one does not anymore - the cure
        //     //  was worse than the disease)
        //     position -= GameConstants.UnitOffset2ForHeading(heading) * secs * speed;
        //     return secs;
        // }

        protected virtual void DoAtGridPoint(Vector2 pos, Heading entryHead, Heading exitHead, long apianTime)
        {
            BeamPlace p = gameData.GetPlace(pos);
            logger.Verbose($"DoAtGridPoint({pos.ToString()}) Bike: {bikeId} Time: {apianTime}");
            if (p == null)
            {
                int xIdx, zIdx;
                (xIdx, zIdx) = Ground.NearestGridIndices(pos);
                // is it on the map?
                if (gameData.Ground.IndicesAreOnMap(xIdx, zIdx))
                {
                    // Yes. Since it's empty send a claim report
                    // Doesn't matter if the bike is local or not - THIS peer thinks there's a claim
                    gameData.ReportPlaceClaimed(this, xIdx, zIdx, entryHead, exitHead);
                } else {
                    // Nope. Blow it up.
                    // TODO: should going off the map be a consensus event?
                    // Current thinking: yeah. But not now.
                    // A thought: Could just skip the on-map check and call it a place claim and report it
                    //   GameNet can grant/not grant it depending on the consensus rules, and if inst
                    //   gets the claim it can just blow it up then.

                    // This is stupid and temporary (rather than just getting rid of the test)
                    // TODO: FIX THIS!!!  &&&&&&&
                    gameData.ReportPlaceClaimed(this, xIdx, zIdx, entryHead, exitHead);
                    //gameInst.apian.SendPlaceClaimObs(apianTime, this, xIdx, zIdx, entryHead, exitHead);
                }
            } else {
                // Hit a marker. Report it.
                gameData.ReportPlaceHit(this, p.xIdx, p.zIdx, entryHead, exitHead);
            }
        }

        //
        // Static tools. Potentially useful publicly
        //
        public static Vector2 NearestGridPoint(Vector2 curPos)
        {
            // TODO: duplicate of Ground method
            float invGridSize = 1.0f / Ground.gridSize;
            return new Vector2(Mathf.Round(curPos.x * invGridSize) * Ground.gridSize, Mathf.Round(curPos.y * invGridSize) * Ground.gridSize);
        }

        public bool CloseToGridPoint(Vector2 curPos)
        {
            float dist = Vector2.Distance(curPos, NearestGridPoint(curPos));
            return (dist < length);
        }

        public static Vector2 UpcomingGridPoint(Vector2 pos, Heading head)
        {
            // it's either the current closest point (if direction to it is the same as heading)
            // or is the closest point + gridSize*unitOffsetForHeading[curHead] if closest point is behind us
            Vector2 point = NearestGridPoint( pos);
            if (Vector2.Dot(GameConstants.UnitOffset2ForHeading(head), point - pos) <= 0)
            {
                point += GameConstants.UnitOffset2ForHeading(head) * Ground.gridSize;
            }
            return point;
        }

        public Vector2 UpcomingGridPoint(Vector2 basePos)
        {
            return UpcomingGridPoint(basePos, baseHeading);
        }

        public void UpdatePosFromCommand(long timeStamp, long curTime, Vector2 posFromCmd, Heading cmdHead)
        {

        //     // Given an authoritative position from a command (claim or hit)
        //     // Compute where the bike should be now according to the command
        //     float deltaSecs = Mathf.Max((curTime - timeStamp) *.001f, .000001f);

       //      logger.Verbose($"UpdatePosFromCmd():  Bike: {bikeId}, CurTime: {curTime}, CmdTime: {timeStamp} DeltaSecs: {deltaSecs}");

            //if ( !basePosition.Equals(posFromCmd))
            //    logger.Info($"UpdatePosFromCmd(): *Conflict* Bike: {bikeId}, CurPos: {basePosition.ToString()}, CmdPos: {posFromCmd.ToString()}");

            basePosition = posFromCmd;
            baseTime = timeStamp;
            baseHeading = cmdHead;
            basePendingTurn = TurnDir.kUnset;
            optTime = 0; // "unsets" the opt vars

        //     position = cmdPos;

        //     // Roll back the current pos to the timestamp, average, and then
        //     // roll back forwards
        //     //logger.Info($"UpdatePosFromCmd(): Cur time: {gameInst.FrameApianTime}  Pos: {position.ToString()} Bike: {bikeId}");
        //     //logger.Info($"                    Cmd time: {timeStamp}  Pos: {posFromCmd.ToString()}");

        //     //Vector2 testPos = position - GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
        //     //logger.Info($"             Rolled-back Pos: {testPos.ToString()}");
        //     //Vector2 avgTsPos = (posFromCmd + testPos) * .5f;
        //     //logger.Info($"                     Avg Pos: {avgTsPos.ToString()}");
        //     //position = avgTsPos + GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
        //     //logger.Info($"                     New Pos: {position.ToString()}");
        }

        // public Vector2 PosAtTime(long testTime, long curTime)
        // {
        //     float deltaSecs = (curTime - testTime) * .001f;
        //     Vector2 testPos = position - GameConstants.UnitOffset2ForHeading(heading) * deltaSecs * speed;
        //     return position;
        // }

    }
}
