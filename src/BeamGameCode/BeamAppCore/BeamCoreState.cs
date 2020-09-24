using System.Net;
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Apian;
using UniLog;

namespace BeamGameCode
{
    public struct PlaceReportArgs // reports of claims and hits take these
    {
        public IBike bike;
        public  int xIdx;
        public int zIdx;
        public  Heading entryHead;
        public  Heading exitHead;
        public PlaceReportArgs(IBike _bike, int _xIdx, int _zIdx, Heading _entryH, Heading _exitH)
        {
            bike = _bike;
            xIdx = _xIdx;
            zIdx = _zIdx;
            entryHead = _entryH;
            exitHead = _exitH;
         }
    }


    public class BeamCoreState : IApianCoreData
    {
        public event EventHandler<BeamPlace> PlaceFreedEvt;
        public event EventHandler<BeamPlace> SetupPlaceMarkerEvt;
        public event EventHandler<BeamPlace> PlaceTimeoutEvt;
        public event EventHandler PlacesClearedEvt;
        public event EventHandler<PlaceReportArgs> PlaceClaimObsEvt;
        public event EventHandler<PlaceReportArgs> PlaceHitObsEvt;

        public UniLogger Logger;

        //
        // Here's the actual base state data:
        //
	    public Ground Ground { get; private set; } = null; // TODO: Is there any mutable state here anymore? NO

        public long CommandSequenceNumber { get; private set; } = -1;
        public Dictionary<string, BeamPlayer> Players { get; private set; } = null;
        public Dictionary<string, IBike> Bikes { get; private set; } = null;
        public Dictionary<int, BeamPlace> activePlaces = null; //  BeamPlace.PosHash() -indexed Dict of places.

        // Ancillary data (initialize to empty if loading state data)
        protected Stack<BeamPlace> freePlaces = null; // re-use released/expired ones

        // TODO: Is there an elegant way to get rid of these next 3 "side effect" members and still do what they do?
        protected List<string> _playerIdsToRemove;
        protected List<string> _bikeIdsToRemove; // Bikes destroyed during a command don;t get removed until the command has been applied completely
        protected List<BeamPlace> _placesToRemove; // Same goes for places.
        protected Dictionary<int, BeamPlace> _reportedTimedOutPlaces; // places that have been reported as timed out, but not removed yet

        public BeamCoreState(IBeamFrontend fep)
        {
            Logger = UniLogger.GetLogger("GameState");
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
            activePlaces = new Dictionary<int, BeamPlace>();
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
            foreach (BeamPlace p in activePlaces.Values)
                if (p.expirationTimeMs <= nowMs)
                    timedOutPlaces.Add(p);

            foreach (BeamPlace p  in timedOutPlaces )
            {
                if ( !_reportedTimedOutPlaces.ContainsKey(p.PosHash))
                {
                    _reportedTimedOutPlaces[p.PosHash] = p;
                    PlaceTimeoutEvt?.Invoke(this,p); // causes GameInst to post a PlaceRemovedMsg request
                }
            }
        }

        public void UpdateCommandSequenceNumber(long newNum)
        {
            CommandSequenceNumber = newNum;
        }

        public class SerialArgs
        {
            public long seqNum;
            public long apianTime;
            public long timeStamp;
            public SerialArgs(long sn, long curTime, long ts) {seqNum=sn; apianTime=curTime; timeStamp=ts;}
        };

        public string ApianSerialized(object args=null)
        {
            SerialArgs sArgs = args as SerialArgs;

            // create array index lookups for peers, bikes to replace actual IDs (which are long) in serialized data
            Dictionary<string,int> peerIndicesDict =  Players.Values.OrderBy(p => p.PeerId)
                .Select((p,idx) => new {p.PeerId, idx}).ToDictionary( x =>x.PeerId, x=>x.idx);

            Dictionary<string,int> bikeIndicesDict =  Bikes.Values.OrderBy(b => b.bikeId)
                .Select((b,idx) => new {b.bikeId, idx}).ToDictionary( x =>x.bikeId, x=>x.idx);

            // State data
            string[] peersData = Players.Values.OrderBy(p => p.PeerId)
                .Select(p => p.ApianSerialized()).ToArray();
            string[] bikesData = Bikes.Values.OrderBy(ib => ib.bikeId)
                .Select(ib => ib.ApianSerialized(new BaseBike.SerialArgs(peerIndicesDict, sArgs.apianTime, sArgs.timeStamp))).ToArray();

            // Note: it's possible for an expired place to still be on the local active list 'cause of timeslice differences
            // when the Checkpoint command is fielded (it would get expired during the next loop) so we want to explicitly
            // filter out any that are expired as of the command timestamp
            string[] placesData = activePlaces.Values
                .Where( p => p.expirationTimeMs > sArgs.timeStamp ) // not expired as of command timestamp
                .Where ( p => Bikes.ContainsKey(p.bike?.bikeId))  // just to make sure the bike hasn;t gone away (note the p.bike? as well as the Bikes dict check)
                .OrderBy(p => p.expirationTimeMs).ThenBy(p => p.PosHash)
                .Select(p => p.ApianSerialized(new BeamPlace.SerialArgs(bikeIndicesDict))).ToArray();

            return  JsonConvert.SerializeObject(new object[]{
                CommandSequenceNumber,
                peersData,
                bikesData,
                placesData
            });
        }


        public static BeamCoreState FromApianSerialized( long seqNum,  long timeStamp,  string stateHash,  string serializedData)
        {
            BeamCoreState newState = new BeamCoreState(null);

            JArray sData = JArray.Parse(serializedData);
            long newSeq = (long)sData[0];

            Dictionary<string, BeamPlayer> newPlayers = (sData[1] as JArray)
                .Select( s => BeamPlayer.FromApianJson((string)s))
                .ToDictionary(p => p.PeerId);

            List<string> peerIds = newPlayers.Values.OrderBy(p => p.PeerId).Select((p) => p.PeerId).ToList(); // to replace array indices in bikes
            Dictionary<string, IBike> newBikes = (sData[2] as JArray)
                .Select( s => (IBike)BaseBike.FromApianJson((string)s, newState, peerIds, timeStamp))
                .ToDictionary(p => p.bikeId);

            List<string> bikeIds = newBikes.Values.OrderBy(p => p.bikeId).Select((p) => p.bikeId).ToList(); // to replace array indices in places
            Dictionary<int, BeamPlace> newPlaces = (sData[3] as JArray)
                .Select( s => BeamPlace.FromApianJson((string)s, bikeIds, newBikes))
                .ToDictionary(p => p.PosHash);


            newState.Players = newPlayers;
            newState.Bikes = newBikes;
            newState.activePlaces = newPlaces;

            newState.UpdateCommandSequenceNumber(seqNum);

            return newState;
        }


        //
        // Player stuff
        //

        public BeamPlayer GetPlayer(string peerId)
        {
            try { return Players[peerId];} catch (KeyNotFoundException){ return null;}
        }

        // Bike stuff

        public BaseBike GetBaseBike(string bikeId)
        {
            try { return Bikes[bikeId] as BaseBike;} catch (KeyNotFoundException){ return null;}
        }

        public IBike ClosestBike(long curTime, IBike thisBike)
        {
            BikeDynState thisBikeState = thisBike.DynamicState(curTime);
            return Bikes.Count <= 1 ? null : Bikes.Values.Where(b => b != thisBike)
                    .OrderBy(b => Vector2.Distance(b.DynamicState(curTime).position, thisBikeState.position)).First();
        }

        public List<IBike> LocalBikes(string peerId)
        {
            return Bikes.Values.Where(ib => ib.peerId == peerId).ToList();
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
            return SetupPlace(p);
        }

        public void AnnounceNewPlace(BeamPlace p )
        {
            // TODO: New Players and bikes get announced in appcore, especially since on
            // deserialization CoreState gets created anew
            // SHould all of this be in AppCore? Or should the stuff in AppCore be here?
            SetupPlaceMarkerEvt?.Invoke(this,p);
        }

        protected BeamPlace SetupPlace(BeamPlace p )
        {
            // This is so a list un un-serialized places can be added directly
            activePlaces[p.PosHash] = p;
            AnnounceNewPlace(p);
            return p;
        }

        protected void RemoveActivePlace(BeamPlace p)
        {
            if (p != null)
            {
                Logger.Verbose($"RemoveActivePlace({p.GetPos().ToString()}) Bike: {p.bike?.bikeId}");
                PlaceFreedEvt?.Invoke(this,p);
                freePlaces.Push(p); // add to free list
                activePlaces.Remove(p.PosHash);
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
            Logger.Info($"RemovePlacesForBike({bike.bikeId})");
            foreach (BeamPlace p in PlacesForBike(bike))
                PostPlaceRemoval(p);
        }

        public List<BeamPlace> PlacesForBike(IBike ib)
        {
            return activePlaces.Values.Where(p => p.bike?.bikeId == ib.bikeId).ToList();
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

        public BeamPlace GetPlace(int xIdx, int zIdx) => activePlaces.GetValueOrDefault(BeamPlace.MakePosHash(xIdx,zIdx), null);

        public BeamPlace GetPlace(Vector2 pos)
        {
            Vector2 gridPos = Ground.NearestGridPoint(pos);
            int xIdx = (int)Mathf.Floor((gridPos.x - Ground.minX) / Ground.gridSize ); // TODO: this is COPY/PASTA EVERYWHERE!!! FIX!!!
            int zIdx = (int)Mathf.Floor((gridPos.y - Ground.minZ) / Ground.gridSize );
            //Debug.Log(string.Format("gridPos: {0}, xIdx: {1}, zIdx: {2}", gridPos, xIdx, zIdx));
            return Ground.IndicesAreOnMap(xIdx,zIdx) ? GetPlace(xIdx,zIdx) : null; // note this returns null for "no place" and for "out of bounds"
        }

        public BeamPlace ClaimPlace(IBike bike, int xIdx, int zIdx, long expireTimeMs)
        {
            BeamPlace p = Ground.IndicesAreOnMap(xIdx,zIdx) ? ( GetPlace(xIdx,zIdx) ?? SetupPlace(bike, xIdx, zIdx,expireTimeMs) ) : null;
            return (p?.bike == bike) ? p : null;
        }

        // Called by bikes to report observed stuff
        public void ReportPlaceClaimed( IBike bike, int xIdx, int zIdx, Heading entryHead, Heading exitHead)
        {
            PlaceClaimObsEvt?.Invoke(this, new PlaceReportArgs(bike, xIdx, zIdx, entryHead, exitHead )); // causes GameInst to post a PlaceClaimed observation
        }

        public void ReportPlaceHit( IBike bike, int xIdx, int zIdx, Heading entryHead, Heading exitHead)
        {
            PlaceHitObsEvt?.Invoke(this, new PlaceReportArgs(bike, xIdx, zIdx, entryHead, exitHead )); // causes GameInst to post a PlaceClaimed observation
        }

    }

}