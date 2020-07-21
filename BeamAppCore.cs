using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using GameModeMgr;
using UnityEngine;
using GameNet;
using Apian;
using UniLog;

namespace BeamBackend
{

    public class BeamAppCore : IBeamAppCore
    {
        public event EventHandler<BeamCoreState> NewGameStateEvt;
        public event EventHandler<string> GroupJoinedEvt;
        public event EventHandler<PlayerJoinedArgs> PlayerJoinedEvt;
        public event EventHandler<PlayerLeftArgs> PlayerLeftEvt;

        public BeamCoreState CoreData {get; private set;}
        public  IBeamFrontend frontend {get; private set;}
        public BeamApian apian {get; private set;}
        public UniLogger logger;
        public BeamPlayer LocalPlayer { get; private set; } = null;
        public string LocalPeerId => apian?.GameNet.LocalP2pId(); // TODO: make LocalP2pId a property?
        public string CurrentGameId  { get; private set; }
        public long CurrentRunningGameTime => apian.CurrentRunningApianTime();

        public long NextCheckpointMs;

        // Not sure where this oughta be. The Loop() methd gets passed a "frameSecs" float that is based on
        // Whatever clck the driver is using. We want everything in the GameInstance to be based on the shared "ApianClock"
        // So - when Loop() is called we are going to read Apian.CurrentApianTime and stash that value to use "next frame"
        // to determine the ApianClock time between frames.
        public long FrameApianTime {get; private set;} = -1;

        // IBeamBackend events

        public event EventHandler PlayersClearedEvt;
        public event EventHandler<IBike> NewBikeEvt;
        public event EventHandler<BikeRemovedData> BikeRemovedEvt;
        public event EventHandler BikesClearedEvt;
        public event EventHandler<BeamPlace> PlaceClaimedEvt;
        public event EventHandler<PlaceHitArgs> PlaceHitEvt;
        public event EventHandler<string> UnknownBikeEvt;

        public event EventHandler ReadyToPlayEvt;
        public event EventHandler RespawnPlayerEvt;

        protected Dictionary<string, Action<BeamMessage>> commandHandlers;

        public BeamAppCore(IBeamFrontend fep)
        {
            logger = UniLogger.GetLogger("GameInstance");
            frontend = fep;
            CoreData = new BeamCoreState(frontend);
            NewGameStateEvt?.Invoke(this, CoreData);

            CoreData.PlaceTimeoutEvt += OnPlaceTimeoutEvt;
            CoreData.PlaceClaimObsEvt += OnPlaceClaimObsEvt;
            CoreData.PlaceHitObsEvt += OnPlaceHitObsEvt;

            commandHandlers = new  Dictionary<string, Action<BeamMessage>>()
            {
                [BeamMessage.kNewPlayer] = (msg) => OnNewPlayerCmd(msg as NewPlayerMsg),
                [BeamMessage.kPlayerLeft] = (msg) => OnPlayerLeftCmd(msg as PlayerLeftMsg),
                [BeamMessage.kBikeCreateData] = (msg) => this.OnCreateBikeCmd(msg as BikeCreateDataMsg),
                [BeamMessage.kRemoveBikeMsg] = (msg) => this.OnRemoveBikeCmd(msg as RemoveBikeMsg),
                [BeamMessage.kBikeTurnMsg] = (msg) => this.OnBikeTurnCmd(msg as BikeTurnMsg),
                [BeamMessage.kBikeCommandMsg] =(msg) => this.OnBikeCommandCmd(msg as BikeCommandMsg),
                [BeamMessage.kPlaceClaimMsg] = (msg) => this.OnPlaceClaimCmd(msg as PlaceClaimMsg),
                [BeamMessage.kPlaceHitMsg] = (msg) => this.OnPlaceHitCmd(msg as PlaceHitMsg),
                [BeamMessage.kPlaceRemovedMsg] = (msg) => this.OnPlaceRemovedCmd(msg as PlaceRemovedMsg),
            };
        }

        //
        // Lifespan
        //
        public void Start()
        {

        }

        public void End()
        {
            ClearPlayers();
            ClearBikes();
            ClearPlaces();
        }

        public bool Loop(float frameSecs)
        {
            //
            // Ignore passed in frameSecs.
            //

            bool isActive = apian.Update();  // returns "True" if Active

            if (isActive) // Don't call loop if not active
            {
                long prevFrameApianTime = FrameApianTime;
                UpdateFrameTime(apian.CurrentRunningApianTime());
                CoreData.Loop(FrameApianTime, FrameApianTime - prevFrameApianTime);
            }

            return true;
        }

        public void UpdateFrameTime(long curApianTime)
        {
            FrameApianTime = curApianTime;
        }

         public void OnCheckpointCommand(long seqNum, long timeStamp)
        {
            logger.Info($"OnCheckpointCommand() seqNum: {seqNum}, timestamp: {timeStamp}, Now: {FrameApianTime}");
            CoreData.UpdateCommandSequenceNumber(seqNum);
            string stateJson = CoreData.ApianSerialized(new BeamCoreState.SerialArgs(seqNum, FrameApianTime, timeStamp));
            logger.Info($"**** Checkpoint:\n{stateJson}\n************\n");
            apian.SendCheckpointState(FrameApianTime, seqNum, stateJson);

            // BeamGameState newState =  BeamGameState.FromApianSerialized(GameData, seqNum,  timeStamp,  "blahblah", stateJson);

        }

        //
        // IBeamApianClient
        //

        public void SetApianReference(ApianBase ap)
        {
            apian = ap as BeamApian;
        }


        public void OnGroupJoined(string groupId)
        {
            logger.Info($"OnGroupJoined({groupId}) - local peer joined");
            GroupJoinedEvt?.Invoke(this, groupId);
        }

        public void ApplyCheckpointStateData( long seqNum,  long timeStamp,  string stateHash,  string serializedData)
        {
            logger.Debug($"ApplyStateData() Seq#: seqNum ApianTime: {timeStamp}");

            UpdateFrameTime(timeStamp);
            CoreData = BeamCoreState.FromApianSerialized(seqNum,  timeStamp,  stateHash,  serializedData);

            foreach (BeamPlayer p in CoreData.Players.Values)
            {
                if (p.PeerId == LocalPeerId )
                    LocalPlayer = p;
                PlayerJoinedEvt.Invoke(this, new PlayerJoinedArgs(CurrentGameId, p));
            }

            foreach (IBike ib in CoreData.Bikes.Values)
                NewBikeEvt?.Invoke(this, ib);


            foreach (BeamPlace p in CoreData.activePlaces.Values)
                CoreData.AnnounceNewPlace(p);  //

        }

        public void OnApianCommand(ApianCommand cmd)
        {
            logger.Debug($"OnApianCommand() Seq#: {cmd.SequenceNum} Cmd: {cmd.CliMsgType}");
            CoreData.UpdateCommandSequenceNumber(cmd.SequenceNum);
            commandHandlers[cmd.ClientMsg.MsgType](cmd.ClientMsg as BeamMessage);
        }

        public void OnNewPlayerCmd(NewPlayerMsg msg)
        {
            BeamPlayer newPlayer = msg.newPlayer;
            logger.Info($"OnNewPlayerCmd() {((newPlayer.PeerId == LocalPeerId)?"Local":"Remote")} name: {newPlayer.Name}");
            _AddPlayer(newPlayer);
        }

        public void OnPlayerLeftCmd(PlayerLeftMsg msg)
        {
            logger.Info($"OnPlayerLeftCmd({msg.peerId})");
            _RemovePlayer(msg.peerId);
        }

        public void OnCreateBikeCmd(BikeCreateDataMsg msg)
        {
            logger.Verbose($"OnCreateBikeCmd(): {msg.bikeId}.");
            IBike ib = msg.ToBike(CoreData);
            logger.Verbose($"** OnCreateBike() created {ib.bikeId} at ({ib.basePosition.x}, {ib.basePosition.y})");
            if (_AddBike(ib))
            {
                // *** Bikes are created stationary now - so there's no need to correct for creation time delay
                logger.Verbose($"OnCreateBike() created {ib.bikeId} at ({ib.basePosition.x}, {ib.basePosition.y})");
            }
        }

        public void OnBikeCommandCmd(BikeCommandMsg msg)
        {
            BaseBike bb = CoreData.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnBikeCommandCmd({msg.cmd}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike:{msg.bikeId}");
            bb.ApplyCommand(msg.cmd, new Vector2(msg.nextPtX, msg.nextPtZ), msg.TimeStamp);
        }

        public void OnBikeTurnCmd(BikeTurnMsg msg)
        {
            BaseBike bb = CoreData.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnBikeTurnCmd({msg.dir}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike:{msg.bikeId}");
            if (bb == null)
                logger.Warn($"OnBikeTurnCmd() Bike:{msg.bikeId} not found!");
            bb?.ApplyTurn(msg.dir, msg.entryHead, new Vector2(msg.nextPtX, msg.nextPtZ),  msg.TimeStamp, msg.bikeState);
        }

        protected void ApplyScoreUpdate(Dictionary<string,int> update)
        {
            foreach( string id in update.Keys)
            {
                IBike bike =  CoreData.GetBaseBike(id);
                bike?.AddScore(update[id]);
                if (bike.score <= 0)
                {
                    logger.Info($"ApplyScoreUpdate(). Bike: {bike.bikeId} has no score anymore!");
                    apian.SendRemoveBikeObs(FrameApianTime, bike.bikeId);
                }
            }
        }

        public void OnPlaceClaimCmd(PlaceClaimMsg msg)
        {
            // Apian has said this message is authoritative
            BaseBike b = CoreData.GetBaseBike(msg.bikeId);
            if (CoreData.Ground.IndicesAreOnMap(msg.xIdx, msg.zIdx))
            {
                if (b == null)
                {
                    logger.Warn($"OnPlaceClaimCmd() Bike:{msg.bikeId} not found!"); // can happen if RemoveCmd and ClaimObs interleave
                    return;
                }

                b.UpdatePosFromCommand(msg.TimeStamp, FrameApianTime, BeamPlace.PlacePos( msg.xIdx, msg.zIdx), msg.exitHead);

                // Claim it
                BeamPlace p = CoreData.ClaimPlace(b, msg.xIdx, msg.zIdx, msg.TimeStamp+BeamPlace.kLifeTimeMs);
                if (p != null)
                {
                    ApplyScoreUpdate(msg.scoreUpdates);
                    logger.Verbose($"OnPlaceClaimCmd() Bike: {b.bikeId} claimed {BeamPlace.PlacePos( msg.xIdx, msg.zIdx).ToString()} at {msg.TimeStamp}");
                    //logger.Verbose($"                  BikePos: {b.position.ToString()}, FrameApianTime: {FrameApianTime} ");
                    //logger.Verbose($"   at Timestamp:  BikePos: {b.PosAtTime(msg.TimeStamp, FrameApianTime).ToString()}, Time: {msg.TimeStamp} ");
                    PlaceClaimedEvt?.Invoke(this, p);
                } else {
                    logger.Warn($"OnPlaceClaimCmd()) failed. Place already claimed.");
                }

            }
        }

        public void OnPlaceHitCmd(PlaceHitMsg msg)
        {
            // Apian has already checked the the place is claimed and the bike exists
            Vector2 pos = BeamPlace.PlacePos(msg.xIdx, msg.zIdx);
            BeamPlace p = CoreData.GetPlace(pos);
            BaseBike hittingBike = CoreData.GetBaseBike(msg.bikeId);
            if (p != null && hittingBike != null)
            {
                hittingBike.UpdatePosFromCommand(msg.TimeStamp, FrameApianTime, p.GetPos(), msg.exitHead);
                logger.Info($"OnPlaceHitCmd{p?.GetPos().ToString()} Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike: {hittingBike?.bikeId} Pos: {p?.GetPos().ToString()}");
                ApplyScoreUpdate(msg.scoreUpdates);
                PlaceHitEvt?.Invoke(this, new PlaceHitArgs(p, hittingBike));
            }
            else
            {
                logger.Info($"OnPlaceHitCmd{p?.GetPos().ToString()} Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike: {hittingBike?.bikeId} Pos: {p?.GetPos().ToString()}");
            }
        }

        public void OnRemoveBikeCmd(RemoveBikeMsg msg)
        {
            logger.Info($"OnRemoveBikeCmd({msg.bikeId}) Now: {FrameApianTime} Ts: {msg.TimeStamp}");
            IBike ib = CoreData.GetBaseBike(msg.bikeId);
            _RemoveBike(ib, true);
        }

        public void OnPlaceRemovedCmd(PlaceRemovedMsg msg)
        {
            BeamPlace p = CoreData.GetPlace(msg.xIdx, msg.zIdx);
            logger.Verbose($"OnPlaceRemovedCmd({msg.xIdx},{msg.zIdx}) {(p==null?"MISSING":"")} Now: {FrameApianTime} Ts: {msg.TimeStamp}");
            CoreData.PostPlaceRemoval(p);
        }

        //
        // IBeamBackend (requests from the frontend)
        //

        public void RaiseReadyToPlay() => ReadyToPlayEvt?.Invoke(this, EventArgs.Empty); // GameCode -> FE
        public void RaiseRespawnPlayer() => RespawnPlayerEvt?.Invoke(this, EventArgs.Empty); // FE -> GameCode
        public Ground GetGround() => CoreData.Ground;

        // public void OnSwitchModeReq(int newModeId, object modeParam)
        // {
        //    logger.Error("backend.OnSwitchModeReq() not working");
        //    // &&&&&modeMgr.SwitchToMode(newModeId, modeParam);
        // }

        // A couple of these are just acting as intermediaries to commands in GameNet that could potentially be called by the frontend
        // directly - if the particular FE code had a reference to the GameNet. It's a lot more likely to  have an IBackend ref.
        // I'm not completely convinced this is the best way to handle it.

        public void PostBikeCreateData(IBike ib, string destId = null)
        {
            logger.Info($"PostBikeCreateData(): {ib.bikeId}");

            apian.SendBikeCreateReq(FrameApianTime, ib, destId);
        }

        public void PostBikeCommand(IBike bike, BikeCommand cmd)
        {
            apian.SendBikeCommandReq(FrameApianTime, bike, cmd, (bike as BaseBike).UpcomingGridPoint(bike.basePosition));
        }

       public void PostBikeTurn(IBike bike, TurnDir dir)
        {
            // This only comes from local AI and player - and "too close" is probably already caught by the bike controller
            BikeDynState bs =  bike.DynamicState(CurrentRunningGameTime); // TODO: Really?
            Vector2 nextPt = (bike as BaseBike).UpcomingGridPoint(bs.position);

            float dx = Vector2.Distance(bs.position, nextPt);
            if (dx < BaseBike.length * .5f)
                logger.Warn($"PostBikeTurn(): Bike too close to turn: {dx} < {BaseBike.length * .5f}");
            else
                apian.SendBikeTurnReq(FrameApianTime, bike, dir, nextPt);
        }

        protected Dictionary<string,int> ComputeScoreUpdate(IBike bike, ScoreEvent evt, BeamPlace place)
        {
            // BIG NOTE: total score is NOT conserved.
            Dictionary<string,int> update = new Dictionary<string,int>();

            int scoreDelta = GameConstants.eventScores[(int)evt];
            update[bike.bikeId] = scoreDelta;

            logger.Debug($"ComputeScoreUpdate(). Bike: {bike.bikeId} Event: {evt}");
            if (evt == ScoreEvent.kHitEnemyPlace || evt == ScoreEvent.kHitFriendPlace)
            {
                // half of the deduction goes to the owner of the place, the rest is divded
                // among the owner's team
                // UNLESS: the bike doing the hitting IS the owner - then the rest of the team just splits it
                if (bike != place.bike) {
                    scoreDelta /= 2;
                    update[place.bike.bikeId] = -scoreDelta; // owner gets half
                }

                IEnumerable<IBike> rewardedOtherBikes =
                    CoreData.Bikes.Values.Where( b => b != bike && b.team == place.bike.team);  // Bikes other the "bike" on affected team
                if (rewardedOtherBikes.Count() > 0)
                {
                    foreach (BaseBike b  in rewardedOtherBikes)
                        update[b.bikeId] = -scoreDelta / rewardedOtherBikes.Count(); // all might not get exactly the same amount
                }
            }

            if (evt == ScoreEvent.kOffMap)
            {
                update[bike.bikeId] = -bike.score*2; // overdo it
            }

            return update;
        }


        protected void xOnScoreEvent(BaseBike bike, ScoreEvent evt, BeamPlace place)
        {
            // Score events:
            //
            // TODO: as with above: This is coming from the backend (BaseBike, mostly) and should
            // be comming from the Net/event/whatever layer
            // NOTE: I'm not so sure about above comment. It;'s not clear that score changes constitute "events"
            int scoreDelta = GameConstants.eventScores[(int)evt];
            bike.AddScore(scoreDelta);

            if (evt == ScoreEvent.kHitEnemyPlace || evt == ScoreEvent.kHitFriendPlace)
            {
                logger.Debug($"OnScoreEvent(). Bike: {bike.bikeId} Event: {evt}");

                // half of the deduction goes to the owner of the place, the rest is divded
                // among the owner's team
                // UNLESS: the bike doing the hitting IS the owner - then the rest of the team just splits it
                if (bike != place.bike) {
                    scoreDelta /= 2;
                    place.bike.AddScore(-scoreDelta); // adds
                }

                IEnumerable<IBike> rewardedOtherBikes =
                    CoreData.Bikes.Values.Where( b => b != bike && b.team == place.bike.team);  // Bikes other the "bike" on affected team
                if (rewardedOtherBikes.Count() > 0)
                {
                    foreach (BaseBike b  in rewardedOtherBikes)
                        b.AddScore(-scoreDelta / rewardedOtherBikes.Count());
                }
            }

            if (evt == ScoreEvent.kOffMap || bike.score <= 0)
            {
                bike.score = 0;
                logger.Info($"OnScoreEvent(). Sending RemoveBikeObs: {bike.bikeId}");
                apian.SendRemoveBikeObs(FrameApianTime, bike.bikeId);
            }
        }

        //  informational
        public void OnUnknownBike(string bikeId, string srcId)
        {
            UnknownBikeEvt?.Invoke(this, bikeId);
        }

        // Peer-related
        protected bool _AddPlayer(BeamPlayer p)
        {
            logger.Debug($"_AddPlayer(). Name: {p.Name} ID: {p.PeerId}");
            if  ( CoreData.Players.ContainsKey(p.PeerId))
            {
                logger.Warn($"_AddPlayer(). Player already exists!!!!");
                return false;
            }

            CoreData.Players[p.PeerId] = p;
            if (p.PeerId == LocalPeerId )
                LocalPlayer = p;
            PlayerJoinedEvt.Invoke(this, new PlayerJoinedArgs(CurrentGameId, p));
            return true;
        }

        protected bool _RemovePlayer(string p2pId)
        {
            if  (!CoreData.Players.ContainsKey(p2pId))
                return false;

            PlayerLeftEvt?.Invoke(this, new PlayerLeftArgs(CurrentGameId, p2pId));

            foreach (IBike ib in CoreData.LocalBikes(p2pId))
                _RemoveBike(ib, true); // Blow em up just for yuks.

            CoreData.Players.Remove(p2pId);
            return true;
        }

        public void ClearPlayers()
        {
            PlayersClearedEvt?.Invoke(this, EventArgs.Empty);
            CoreData.Players.Clear();
        }

        // Bike-related

        public BaseBike CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( this.CoreData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(CoreData, bikeId, peerId, name, t, ctrlType, CurrentRunningGameTime, pos, heading);
        }

        public bool _AddBike(IBike ib)
        {
            logger.Verbose($"_AddBike(): {ib.bikeId} at ({ib.basePosition.x}, {ib.basePosition.y})");

            if (CoreData.GetBaseBike(ib.bikeId) != null)
                return false;

            CoreData.Bikes[ib.bikeId] = ib;

            NewBikeEvt?.Invoke(this, ib);

            return true;
        }

        protected void _RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            logger.Info($"_RemoveBike(): {ib.bikeId}");
            CoreData.RemovePlacesForBike(ib);
            BikeRemovedEvt?.Invoke(this, new BikeRemovedData(ib.bikeId,  shouldBlowUp));
            CoreData.PostBikeRemoval(ib.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            BikesClearedEvt?.Invoke(this, EventArgs.Empty);
            CoreData.Bikes.Clear();
        }

       // Ground-related

        public void OnPlaceClaimObsEvt(object sender, PlaceReportArgs args)
        {
            logger.Verbose($"OnPlaceClaimObsEvt(): Bike: {args.bike.bikeId} Place: {BeamPlace.PlacePos(args.xIdx, args.zIdx).ToString()}");

            apian.SendPlaceClaimObs(FrameApianTime, args.bike, args.xIdx, args.zIdx, args.entryHead, args.exitHead,
                ComputeScoreUpdate(args.bike, ScoreEvent.kClaimPlace, null));
          }

        public void OnPlaceHitObsEvt(object sender, PlaceReportArgs args)
        {
            logger.Verbose($"OnPlaceHitObsEvt(): Bike: {args.bike.bikeId} Place: {BeamPlace.PlacePos(args.xIdx, args.zIdx).ToString()}");
            BeamPlace place = CoreData.GetPlace(args.xIdx, args.zIdx);
            ScoreEvent evType = place.bike.team == args.bike.team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace;
            apian.SendPlaceHitObs(FrameApianTime, args.bike, args.xIdx, args.zIdx, args.entryHead, args.exitHead, ComputeScoreUpdate(args.bike, evType, place));

        }

        public void OnPlaceTimeoutEvt(object sender, BeamPlace p)
        {
            logger.Verbose($"OnPlaceTimeoutEvt(): {p.GetPos().ToString()}");
            apian.SendPlaceRemovedObs(FrameApianTime, p.xIdx, p.zIdx);
        }

        public void ClearPlaces()
        {
            CoreData.ClearPlaces(); // notifies FE.
        }

    }

}