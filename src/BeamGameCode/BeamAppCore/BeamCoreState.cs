using System.Net;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Apian;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public class PlaceReportEventArgs : EventArgs // report events of claims and hits take these
    {
        public long apianTime;
        public IBike bike;
        public  int xIdx;
        public int zIdx;
        public  Heading entryHead;
        public  Heading exitHead;
        public PlaceReportEventArgs(long _apianTime, IBike _bike, int _xIdx, int _zIdx, Heading _entryH, Heading _exitH)
        {
            apianTime = _apianTime;
            bike = _bike;
            xIdx = _xIdx;
            zIdx = _zIdx;
            entryHead = _entryH;
            exitHead = _exitH;
         }
    }


    public class BeamCoreState : ApianCoreState
    {
        public event EventHandler<BeamPlaceEventArgs> PlaceFreedEvt;
        public event EventHandler<BeamPlaceEventArgs> PlaceTimeoutEvt;
        public event EventHandler PlacesClearedEvt;
        public event EventHandler<PlaceReportEventArgs> PlaceClaimObsEvt; // exact timestamp is the long
        public event EventHandler<PlaceReportEventArgs> PlaceHitObsEvt;
        public event EventHandler<BeamSquareEventArgs> SquareAddEvt; // called w/ base place
        public event EventHandler<BeamSquareEventArgs> SquareDelEvt;

     	    public Ground Ground { get; private set; } // TODO: Is there any mutable state here anymore? NO

        //
        // Here's the actual base state data:
        //

        // Data to serialize
        public Dictionary<string, BeamPlayer> Players { get; private set; }
        public Dictionary<string, IBike> Bikes { get; private set; }
        public Dictionary<int, BeamPlace> ActivePlaces { get; private set; } //  BeamPlace.PosHash() -indexed Dict of places.
        // end data to serialize

        // Ancillary data (initialize to empty if loading state data)
        public Dictionary<int, Team> ActiveSquares; // squares are redundant and can be constructed by examining ActivePlaces

        protected Stack<BeamPlace> freePlaces; // re-use released/expired ones

        // TODO: Is there an elegant way to get rid of these next 3 "side effect" members and still do what they do?
        protected List<string> _playerIdsToRemove;
        protected List<string> _bikeIdsToRemove; // Bikes destroyed during a command don;t get removed until the command has been applied completely
        protected List<BeamPlace> _placesToRemove; // Same goes for places.
        protected Dictionary<int, BeamPlace> _reportedTimedOutPlaces; // places that have been reported as timed out, but not removed yet

        public BeamCoreState(string sessionId) : base(sessionId)
        {

            Players = new Dictionary<string, BeamPlayer>();
            Bikes = new Dictionary<string, IBike>();
            Ground = new Ground();
            InitPlaces();

            ResetRemovalSideEffects();

        }

        public void Init()
        {
            Players.Clear();
            Bikes.Clear();
            InitPlaces();
        }

        protected void InitPlaces()
        {
            ActivePlaces = new Dictionary<int, BeamPlace>();
            ActiveSquares = new Dictionary<int, Team>();
            freePlaces = new Stack<BeamPlace>();
            _reportedTimedOutPlaces  = new Dictionary<int, BeamPlace>(); // check this before reporting. delete entry when removed.
        }

        public void ResetRemovalSideEffects()
        {
            _playerIdsToRemove = new List<string>();
            _bikeIdsToRemove = new List<string>();
            _placesToRemove = new List<BeamPlace>();
        }

        public void PostPlayerRemoval(string playerId) => _playerIdsToRemove.Add(playerId);
        public void PostBikeRemoval(string bikeId) => _bikeIdsToRemove.Add(bikeId);

        public void PostPlaceRemoval(BeamPlace p) => _placesToRemove.Add(p);

        public void DoRemovals()
        {
            _placesToRemove.RemoveAll( p => { RemoveActivePlace(p); return true; } ); // do places before bikes
            _bikeIdsToRemove.RemoveAll( bid => {Bikes.Remove(bid); return true; });
            _playerIdsToRemove.RemoveAll( pid => {Players.Remove(pid); return true; });
        }

        public void Loop(long nowMs, long frameMs)
        {
             foreach( IBike ib in Bikes.Values)
                ib.Loop(nowMs);  // Bike might get "destroyed" here and need to be removed

            LoopPlaces(nowMs);

        }

        protected void LoopPlaces(long nowMs)
        {
            List<BeamPlace> timedOutPlaces = new List<BeamPlace>();
            // Be very, very careful not to do something that might recusively delete a list member while iterating over the list
            // This is probably unneeded given that PostPlaceRemoval() exists
            foreach (BeamPlace p in ActivePlaces.Values)
                if (p.expirationTimeMs <= nowMs)
                    timedOutPlaces.Add(p);

            foreach (BeamPlace p  in timedOutPlaces )
            {
                if ( !_reportedTimedOutPlaces.ContainsKey(p.PosHash))
                {
                    _reportedTimedOutPlaces[p.PosHash] = p;
                    PlaceTimeoutEvt?.Invoke(this, new BeamPlaceEventArgs(p)); // causes GameInst to post a PlaceRemovedMsg observation (*actual time is in the place definition*)
                }
            }
        }


        public class SerialArgs
        {
            public long seqNum;
            public long chkPtTimeStamp; // used to check for expired places
            public SerialArgs(long sn, long ts) {seqNum=sn; chkPtTimeStamp=ts; }
        };

        public override string ApianSerialized(object args=null)
        {
            SerialArgs sArgs = args as SerialArgs;

            // create array index lookups for players, bikes to replace actual IDs (which are long) in serialized data
            Dictionary<string,int> playerIndicesDict =  Players.Values.OrderBy(p => p.PlayerAddr)
                .Select((p,idx) => new {p.PlayerAddr, idx}).ToDictionary( x =>x.PlayerAddr, x=>x.idx);

            Dictionary<string,int> bikeIndicesDict =  Bikes.Values.OrderBy(b => b.bikeId)
                .Select((b,idx) => new {b.bikeId, idx}).ToDictionary( x =>x.bikeId, x=>x.idx);

            // State data
            string[] playersData = Players.Values.OrderBy(p => p.PlayerAddr)
                .Select(p => p.ApianSerialized()).ToArray();
            string[] bikesData = Bikes.Values.OrderBy(ib => ib.bikeId)
                .Select(ib => ib.ApianSerialized(new BaseBike.SerialArgs(playerIndicesDict))).ToArray();

            // Note: it's possible for an expired place to still be on the local active list 'cause of timeslice differences
            // when the Checkpoint command is fielded (it would get expired during the next loop) so we want to explicitly
            // filter out any that are expired as of the command timestamp
            string[] placesData = ActivePlaces.Values
                .Where( p => p.expirationTimeMs > sArgs.chkPtTimeStamp ) // not expired as of command timestamp
                .Where ( p => Bikes.ContainsKey(p.bike?.bikeId))  // just to make sure the bike hasn;t gone away (note the p.bike? as well as the Bikes dict check)
                .OrderBy(p => p.expirationTimeMs).ThenBy(p => p.PosHash)
                .Select(p => p.ApianSerialized(new BeamPlace.SerialArgs(bikeIndicesDict))).ToArray();


            return  JsonConvert.SerializeObject(new object[]{
                ApianSerializedBaseData(), // serialize all of the AppCoreBase data
                playersData,
                bikesData,
                placesData
            });
        }


        public static BeamCoreState FromApianSerialized(long seqNum,  string stateHash,  string serializedData)
        {
            BeamCoreState newState = new BeamCoreState(null); // sessionId is in serialized state

            JArray sData = JArray.Parse(serializedData);

            newState.ApplyDeserializedBaseData((string)sData[0]); // Populate the base ApianCoreState  data
            long newSeq = newState.CommandSequenceNumber; // got applied above

            Dictionary<string, BeamPlayer> newPlayers = (sData[1] as JArray)
                .Select( s => BeamPlayer.FromApianJson((string)s))
                .ToDictionary(p => p.PlayerAddr);

            List<string> playerAddrs = newPlayers.Values.OrderBy(p => p.PlayerAddr).Select((p) => p.PlayerAddr).ToList(); // to replace array indices in bikes
            Dictionary<string, IBike> newBikes = (sData[2] as JArray)
                .Select( s => (IBike)BaseBike.FromApianJson((string)s, newState, playerAddrs))
                .ToDictionary(p => p.bikeId);

            List<string> bikeIds = newBikes.Values.OrderBy(p => p.bikeId).Select((p) => p.bikeId).ToList(); // to replace array indices in places
            Dictionary<int, BeamPlace> newPlaces = (sData[3] as JArray)
                .Select( s => BeamPlace.FromApianJson((string)s, bikeIds, newBikes))
                .ToDictionary(p => p.PosHash);


            newState.Players = newPlayers;
            newState.Bikes = newBikes;
            newState.ActivePlaces = newPlaces;

            newState.RebuildSquares();

            return newState;
        }


        //
        // Player stuff
        //

        public BeamPlayer GetPlayer(string playerAddr)
        {
            try { return Players[playerAddr];} catch (KeyNotFoundException){ return null;}
        }

        // Bike stuff

        public BaseBike GetBaseBike(string bikeId)
        {
            try { return Bikes[bikeId] as BaseBike;} catch (KeyNotFoundException){ return null;}
        }

        public IBike ClosestBikeToPos(long curTime, Vector2 pos)
        {
            return Bikes.Count <= 1 ? null : Bikes.Values
                    .OrderBy(b => Vector2.Distance(b.DynamicState(curTime).position, pos))
                    .First();
        }

        public IBike ClosestBike(long curTime, IBike thisBike)
        {
            BikeDynState thisBikeState = thisBike.DynamicState(curTime);
            return Bikes.Count <= 1 ? null : Bikes.Values.Where(b => b != thisBike)
                    .OrderBy(b => Vector2.Distance(b.DynamicState(curTime).position, thisBikeState.position)).First();
        }

        public List<IBike> LocalBikes(string playerAddr)
        {
            return Bikes.Values.Where(ib => ib.playerAddr == playerAddr).ToList();
        }

        public List<Vector2> CloseBikePositions(long curTime, IBike thisBike, int maxCnt)
        {
            // Todo: this is actually "current enemy pos"
            BikeDynState thisBikeState = thisBike.DynamicState(curTime);
            return Bikes.Values.Where(b => b != thisBike)
                .OrderBy(b => Vector2.Distance(b.DynamicState(curTime).position, thisBikeState.position)).Take(maxCnt) // IBikes
                .Select(ob => ob.DynamicState(curTime).position).ToList(); // TODO: extract dynamic states rather than recalc? Maybe not?
        }

        // Places stuff

        // Set up a place instance for use or re-use
        protected BeamPlace SetupPlace(IBike bike, int xIdx, int zIdx, long expireTimeMs )
        {
            BeamPlace p = freePlaces.Count > 0 ? freePlaces.Pop() : new BeamPlace();
            // Maybe populating a new one, maybe re-populating a used one.
            p.expirationTimeMs = expireTimeMs;
            p.xIdx = xIdx;
            p.zIdx = zIdx;
            p.bike = bike;
            ActivePlaces[p.PosHash] = p;

            (bool ne, bool nw, bool sw, bool se) = PlaceIsInSquares(p);

            if (ne)
                AddSquare( BeamPlace.MakePosHash(p.xIdx, p.zIdx), bike.team);
            if (nw)
                AddSquare( BeamPlace.MakePosHash(p.xIdx-1, p.zIdx), bike.team);
            if (sw)
                AddSquare( BeamPlace.MakePosHash(p.xIdx-1, p.zIdx-1), bike.team);
            if (se)
                AddSquare( BeamPlace.MakePosHash(p.xIdx, p.zIdx-1), bike.team);

            return p;
        }

        protected void RemoveActivePlace(BeamPlace p)
        {
            if (p != null)
            {
                (bool ne, bool nw, bool sw, bool se) = PlaceIsInSquares(p);

                if (ne)
                    RemoveSquare( BeamPlace.MakePosHash(p.xIdx, p.zIdx));
                if (nw)
                    RemoveSquare( BeamPlace.MakePosHash(p.xIdx-1, p.zIdx));
                if (sw)
                    RemoveSquare( BeamPlace.MakePosHash(p.xIdx-1, p.zIdx-1));
                if (se)
                    RemoveSquare( BeamPlace.MakePosHash(p.xIdx, p.zIdx-1));

                Logger.Verbose($"RemoveActivePlace({p.GetPos().ToString()}) Bike: {SID(p.bike?.bikeId)}");
                PlaceFreedEvt?.Invoke(this, new BeamPlaceEventArgs(p) );
                freePlaces.Push(p); // add to free list
                ActivePlaces.Remove(p.PosHash);
                _reportedTimedOutPlaces.Remove(p.PosHash);
                p.bike = null; // this is the only reference it holds

            }
        }

        public void ClearPlaces()
        {
            InitPlaces();
            PlacesClearedEvt?.Invoke(this, EventArgs.Empty);
        }

        public void RemovePlacesForBike(IBike bike)
        {
            Logger.Info($"RemovePlacesForBike({SID(bike.bikeId)})");
            foreach (BeamPlace p in PlacesForBike(bike))
                PostPlaceRemoval(p);
        }

        public List<BeamPlace> PlacesForBike(IBike ib)
        {
            return ActivePlaces.Values.Where(p => p.bike?.bikeId == ib.bikeId).ToList();
        }

        //
        // Squares are defined by the ActivePlaces, but it's handy to have a separate dict for them
        //

        protected void RebuildSquares()
        {
            ActiveSquares = new Dictionary<int, Team>();

            foreach( KeyValuePair<int,BeamPlace> kp in ActivePlaces ) {
                if (PlaceIsBaseForSquare(kp.Value))
                    AddSquare(kp.Key, kp.Value.bike.team);
            }
        }

        protected void AddSquare(int posHash, Team t)
        {
            if (!ActiveSquares.ContainsKey(posHash))
            {
                ActiveSquares[posHash] = t;
                SquareAddEvt?.Invoke(this, new BeamSquareEventArgs(posHash, t));
            }
        }

        protected void RemoveSquare(int posHash)
        {
            SquareDelEvt?.Invoke(this, new BeamSquareEventArgs(posHash, null));
            ActiveSquares.Remove(posHash);
        }

        private int[] xAround = {1,1,0,-1,-1,-1,0,1}; // x offsets starting with [x+1, z] going ariund clockwise
        private int[] zAround = {0,1,1,1,0,-1,-1,-1}; // same for z offsets in a square around [x,z] = [0,0]


        public bool PlaceIsBaseForSquare(BeamPlace p)
        {
            // returns true if p is the southwest (-x,-z) corner of a square
            // TODO: would be quicker to have a quick exit of the implied Linq iters on first failure.

            IList<bool> samesAround = Enumerable.Range(0, 3)  // 0,1, and 2 are the (x+1,z), (x+1,z+1), (x,z+1) neighbors
                .Select(i => GetPlace(p.xIdx + xAround[i], p.zIdx + zAround[i])?.bike)
                .Select(b => b?.team.TeamID == p.bike.team.TeamID).ToList();
            return samesAround[0] && samesAround[1] && samesAround[2];
        }


        public bool PlaceIsInSquare(BeamPlace p)
        {
            // returns true if p is in 1 or more squares
            IList<bool> samesAround = Enumerable.Range(0, 8)
                .Select(i => GetPlace(p.xIdx + xAround[i], p.zIdx + zAround[i])?.bike)
                .Select(b => b?.team.TeamID == p.bike.team.TeamID).ToList();

            return samesAround[0] && samesAround[1] && samesAround[2]
                || samesAround[2] && samesAround[3] && samesAround[4]
                || samesAround[4] && samesAround[5] && samesAround[6]
                || samesAround[6] && samesAround[7] && samesAround[0];
        }



        public (bool,bool,bool,bool) PlaceIsInSquares(BeamPlace p)
        {
            // returns a tuple in quadrant order of true/false if there are squares

            IList<bool> samesAround = Enumerable.Range(0, 8)
                .Select(i => GetPlace(p.xIdx + xAround[i], p.zIdx + zAround[i])?.bike)
                .Select(b => b?.team.TeamID == p.bike.team.TeamID).ToList();

            return ( samesAround[0] && samesAround[1] && samesAround[2],
                     samesAround[2] && samesAround[3] && samesAround[4],
                     samesAround[4] && samesAround[5] && samesAround[6],
                     samesAround[6] && samesAround[7] && samesAround[0] );
        }


        // public List<BeamPlace> PlacesForBike(IBike ib)
        // {
        //     return activePlaces.Values.Where(p =>
        //         {
        //             if ( ib == null)
        //                 Logger.Warn($"PlacesForBike() null Bike!");

        //             if ( p == null)
        //                 Logger.Warn($"PlacesForBike() null place!");
        //             if (p.bike == null)
        //                 Logger.Warn($"PlacesForBike() Active place {p.GetPos().ToString()} has null bike.");
        //             return p.bike?.bikeId == ib.bikeId;
        //         } ).ToList();
        // }

        public BeamPlace GetPlace(int xIdx, int zIdx) {
            BeamPlace ret = null;
            try {
                ret = ActivePlaces.GetValueOrDefault(BeamPlace.MakePosHash(xIdx,zIdx), null);
            } catch (Exception ) { }
            return ret;
        }

        public BeamPlace GetPlace(Vector2 pos)
        {
            Vector2 gridPos = Ground.NearestGridPoint(pos);
            int xIdx = (int)Mathf.Floor((gridPos.x - Ground.minX) / Ground.gridSize ); // TODO: this is COPY/PASTA EVERYWHERE!!! FIX!!!
            int zIdx = (int)Mathf.Floor((gridPos.y - Ground.minZ) / Ground.gridSize );
            //Debug.Log(string.Format("gridPos: {0}, xIdx: {1}, zIdx: {2}", gridPos, xIdx, zIdx));
            return Ground.IndicesAreOnMap(xIdx,zIdx) ? GetPlace(xIdx,zIdx) : null; // note this returns null for "no place" and for "out of bounds"
        }

        public List<BeamPlace> GetNearbyPlaces(Vector2 pos, float maxDist)
        {
            // maxDist is a Manhattan distance,
            int baseX;
            int baseZ;
            int grids = (int)Mathf.Round( (maxDist +.5f) / Ground.gridSize ); // how many grid lengths is maxDist

            List<int> possiblePlaceHashes = new List<int>();
            (baseX, baseZ) = Ground.NearestGridIndices(pos);
            foreach(int x in Enumerable.Range(baseX-grids, 2*grids))
                foreach(int z in Enumerable.Range(baseZ-grids, 2*grids))
                    possiblePlaceHashes.Add(BeamPlace.MakePosHash(x,z));

            List<BeamPlace> places = possiblePlaceHashes.Select( hash => ActivePlaces.GetValueOrDefault(hash, null))
                                        .Where( p => p != null).ToList();

            return places;
        }

        public BeamPlace ClaimPlace(IBike bike, int xIdx, int zIdx, long expireTimeMs)
        {
            BeamPlace p = Ground.IndicesAreOnMap(xIdx,zIdx) ? ( GetPlace(xIdx,zIdx) ?? SetupPlace(bike, xIdx, zIdx,expireTimeMs) ) : null;
            return (p?.bike == bike) ? p : null;
        }

        // Called by bikes to report observed stuff
        public void ReportPlaceClaimed( long apianTime, IBike bike, int xIdx, int zIdx, Heading entryHead, Heading exitHead)
        {
            PlaceClaimObsEvt?.Invoke(this, new PlaceReportEventArgs(apianTime,bike, xIdx, zIdx, entryHead, exitHead) ); // causes GameInst to post a PlaceClaimed observation
        }

        public void ReportPlaceHit( long apianTime, IBike bike, int xIdx, int zIdx, Heading entryHead, Heading exitHead)
        {
            PlaceHitObsEvt?.Invoke(this, new PlaceReportEventArgs(apianTime, bike, xIdx, zIdx, entryHead, exitHead) ); // causes GameInst to post a PlaceHit observation
        }

    }

}