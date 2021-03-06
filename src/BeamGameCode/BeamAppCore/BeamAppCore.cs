using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using GameModeMgr;
using UnityEngine;
using GameNet;
using Apian;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{

    public class BeamAppCore : ApianAppCore,  IBeamAppCore
    {
        public event EventHandler<BeamCoreState> NewCoreStateEvt;
        public event EventHandler<string> GroupJoinedEvt;
        public event EventHandler<PlayerJoinedArgs> PlayerJoinedEvt;
        public event EventHandler<PlayerLeftArgs> PlayerLeftEvt;
        public event EventHandler<PlayerLeftArgs> PlayerMissingEvt; // not Gone... yet
        public event EventHandler<PlayerLeftArgs> PlayerReturnedEvt;

        public BeamCoreState CoreState {get; private set;}
        public BeamApian apian {get; private set;}
        public UniLogger logger;
        public BeamPlayer LocalPlayer { get; private set; } = null;
        public string LocalPeerId => apian?.GameNet.LocalP2pId(); // TODO: make LocalP2pId a property?

        public string ApianNetId => apian?.NetworkId;
        public string ApianGroupName => apian?.GroupName;
        public string ApianGroupId => apian?.GroupId; // <net>/<group>
        public long CurrentRunningGameTime => apian.CurrentRunningApianTime();

        // public long NextCheckpointMs; FIXME: appears unused

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

        public BeamAppCore()
        {
            logger = UniLogger.GetLogger("AppCore");
            CoreState = new BeamCoreState();
            OnNewCoreState();

            ClientMsgCommandHandlers = new  Dictionary<string, Action<ApianCoreMessage, long>>()
            {
                [BeamMessage.kNewPlayer] = (msg, seqNum) => OnNewPlayerCmd(msg as NewPlayerMsg, seqNum),
                [BeamMessage.kPlayerLeft] = (msg, seqNum) => OnPlayerLeftCmd(msg as PlayerLeftMsg, seqNum),
                [BeamMessage.kBikeCreateData] = (msg, seqNum) => this.OnCreateBikeCmd(msg as BikeCreateMsg, seqNum),
                [BeamMessage.kRemoveBikeMsg] = (msg, seqNum) => this.OnRemoveBikeCmd(msg as RemoveBikeMsg, seqNum),
                [BeamMessage.kBikeTurnMsg] = (msg, seqNum) => this.OnBikeTurnCmd(msg as BikeTurnMsg, seqNum),
                [BeamMessage.kBikeCommandMsg] =(msg, seqNum) => this.OnBikeCommandCmd(msg as BikeCommandMsg, seqNum),
                [BeamMessage.kPlaceClaimMsg] = (msg, seqNum) => this.OnPlaceClaimCmd(msg as PlaceClaimMsg, seqNum),
                [BeamMessage.kPlaceHitMsg] = (msg, seqNum) => this.OnPlaceHitCmd(msg as PlaceHitMsg, seqNum),
                [BeamMessage.kPlaceRemovedMsg] = (msg, seqNum) => this.OnPlaceRemovedCmd(msg as PlaceRemovedMsg, seqNum),
                [ApianMessage.CheckpointMsg] = (msg, seqNum) => this.OnCheckpointCommand(msg as ApianCheckpointMsg, seqNum),
            };
        }

        // IApianAppCore
        public override void SetApianReference(ApianBase ap)
        {
            apian = ap as BeamApian;
        }

        public override ApianCoreMessage DeserializeCoreMessage(ApianWrappedCoreMessage aMsg)
        {
            return BeamCoreMessageDeserializer.FromJSON(aMsg.CoreMsgType, aMsg.SerializedCoreMsg);
        }

        public override bool CommandIsValid(ApianCoreMessage cmdMsg)
        {
            throw new NotImplementedException();
        }
        public override void OnApianCommand(long cmdSeqNum, ApianCoreMessage coreMsg)
        {
            logger.Debug($"OnApianCommand() Seq#: {cmdSeqNum} Cmd: {coreMsg.MsgType}");
            CoreState.UpdateCommandSequenceNumber(cmdSeqNum);
            CoreState.ResetRemovalSideEffects();
            ClientMsgCommandHandlers[coreMsg.MsgType](coreMsg, cmdSeqNum);
            CoreState.DoRemovals();
        }

        // what effect does the previous msg have on the testMsg?
        public override (ApianConflictResult result, string reason) ValidateCoreMessages(ApianCoreMessage prevMsg, ApianCoreMessage testMsg)
        {
            return BeamMessageValidity.ValidateObservations( prevMsg as BeamMessage, testMsg as BeamMessage);
        }

        public override void OnCheckpointCommand(ApianCheckpointMsg msg, long seqNum)
        {
            logger.Info($"OnCheckpointCommand() seqNum: {seqNum}, timestamp: {msg.TimeStamp}, Now: {FrameApianTime}");
            CoreState.UpdateCommandSequenceNumber(seqNum);
            string stateJson = CoreState.ApianSerialized(new BeamCoreState.SerialArgs(seqNum, msg.TimeStamp));
            logger.Debug($"**** Checkpoint:\n{stateJson}\n************\n");
            apian.SendCheckpointState(FrameApianTime, seqNum, stateJson);

            // BeamGameState newState =  BeamGameState.FromApianSerialized(GameData, seqNum,  timeStamp,  "blahblah", stateJson);
        }
        public override void ApplyCheckpointStateData(long seqNum,  long timeStamp,  string stateHash,  string serializedData)
        {
            logger.Debug($"ApplyStateData() Seq#: seqNum ApianTime: {timeStamp}");

            UpdateFrameTime(timeStamp);
            CoreState = BeamCoreState.FromApianSerialized(seqNum,  stateHash,  serializedData);
            OnNewCoreState(); // send NewCoreStateEvt

            foreach (BeamPlayer p in CoreState.Players.Values)
            {
                if (p.PeerId == LocalPeerId )
                    LocalPlayer = p;
                PlayerJoinedEvt.Invoke(this, new PlayerJoinedArgs(ApianGroupId, p));
            }

            foreach (IBike ib in CoreState.Bikes.Values)
                NewBikeEvt?.Invoke(this, ib);
        }

        // Beam stuff

        protected void OnNewCoreState()
        {
            NewCoreStateEvt?.Invoke(this, CoreState);

            CoreState.PlaceTimeoutEvt += OnPlaceTimeoutEvt;
            CoreState.PlaceClaimObsEvt += OnPlaceClaimObsEvt;
            CoreState.PlaceHitObsEvt += OnPlaceHitObsEvt;
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
                CoreState.Loop(FrameApianTime, FrameApianTime - prevFrameApianTime);
            }

            apian.EndObservationSet(); // TODO: should this be 1 command?
            apian.StartObservationSet();


            return true;
        }

        public void UpdateFrameTime(long curApianTime)
        {
            FrameApianTime = curApianTime;
        }


        //
        // Beam Apian talks to these
        // Might want to become an Apian interface
        //

        public void OnGroupJoined(string groupId)
        {
            logger.Info($"OnGroupJoined({groupId}) - local peer joined");
            GroupJoinedEvt?.Invoke(this, groupId);
        }


        public void OnPlayerMissing(string groupId, string p2pId)
        {
            logger.Info($"Player: {SID(p2pId)} is missing!");
            PlayerMissingEvt?.Invoke(this, new PlayerLeftArgs(groupId, p2pId));
        }

        public void OnPlayerReturned(string groupId, string p2pId)
        {
            logger.Info($"Player: {SID(p2pId)} has returned!");
            PlayerReturnedEvt?.Invoke(this, new PlayerLeftArgs(groupId, p2pId));
        }

        //
        // ClientMsg command handlers
        //

        public void OnNewPlayerCmd(NewPlayerMsg msg, long seqNum)
        {
            BeamPlayer newPlayer = msg.newPlayer;
            logger.Info($"OnNewPlayerCmd(#{seqNum}) {((newPlayer.PeerId == LocalPeerId)?"Local":"Remote")} name: {newPlayer.Name}");
            _AddPlayer(newPlayer);
        }

        public void OnPlayerLeftCmd(PlayerLeftMsg msg, long seqNum)
        {
            logger.Info($"OnPlayerLeftCmd(#{seqNum}, {SID(msg.peerId) })");
            _RemovePlayer(msg.peerId);
        }

        public void OnCreateBikeCmd(BikeCreateMsg msg, long seqNum)
        {
            logger.Verbose($"OnCreateBikeCmd(#{seqNum}): {SID(msg.bikeId)}.");
            IBike ib = msg.ToBike(CoreState);

            logger.Verbose($"** OnCreateBike() created {SID(ib.bikeId) } at ({ib.basePosition.x}, {ib.basePosition.y})");

            if (_AddBike(ib))
            {
                // *** Bikes are created stationary now - so there's no need to correct for creation time delay
                logger.Verbose($"OnCreateBike() created {SID(ib.bikeId)} at ({ib.basePosition.x}, {ib.basePosition.y})");
            }
        }

        public void OnBikeCommandCmd(BikeCommandMsg msg, long seqNum)
        {
            BaseBike bb = CoreState.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnBikeCommandCmd(#{seqNum}, {msg.cmd}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike:{SID(msg.bikeId)}");
            bb.ApplyCommand(msg.cmd, new Vector2(msg.nextPtX, msg.nextPtZ), msg.TimeStamp);
        }

        public void OnBikeTurnCmd(BikeTurnMsg msg, long seqNum)
        {
            BaseBike bb = CoreState.GetBaseBike(msg.bikeId);
            logger.Verbose($"OnBikeTurnCmd(#{seqNum}, {msg.dir}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike:{SID(msg.bikeId)}");
            if (bb == null)
                logger.Warn($"OnBikeTurnCmd() Bike:{SID(msg.bikeId)} not found!");
            bb?.ApplyTurn(msg.dir, msg.entryHead, new Vector2(msg.nextPtX, msg.nextPtZ),  msg.TimeStamp, msg.bikeState);
        }

        public void OnPlaceClaimCmd(PlaceClaimMsg msg, long seqNum)
        {
            // Apian has said this message is authoritative
            BaseBike b = CoreState.GetBaseBike(msg.bikeId);
            if (CoreState.Ground.IndicesAreOnMap(msg.xIdx, msg.zIdx))
            {
                if (b == null)
                {
                    logger.Warn($"OnPlaceClaimCmd(#{seqNum}) Bike:{SID(msg.bikeId)} not found!"); // can happen if RemoveCmd and ClaimObs interleave
                    return;
                }

                b.UpdatePosFromCommand(msg.TimeStamp, FrameApianTime, BeamPlace.PlacePos( msg.xIdx, msg.zIdx), msg.exitHead);

                // Claim it
                BeamPlace p = CoreState.ClaimPlace(b, msg.xIdx, msg.zIdx, msg.TimeStamp+BeamPlace.kLifeTimeMs);
                if (p != null)
                {
                    _ApplyScoreUpdate(msg.TimeStamp, msg.scoreUpdates);
                    logger.Verbose($"OnPlaceClaimCmd(#{seqNum}) Bike: {SID(b.bikeId)} claimed {BeamPlace.PlacePos( msg.xIdx, msg.zIdx).ToString()} at {msg.TimeStamp}");
                    //logger.Verbose($"                  BikePos: {b.position.ToString()}, FrameApianTime: {FrameApianTime} ");
                    //logger.Verbose($"   at Timestamp:  BikePos: {b.PosAtTime(msg.TimeStamp, FrameApianTime).ToString()}, Time: {msg.TimeStamp} ");
                    PlaceClaimedEvt?.Invoke(this, p);
                } else {
                    // &&&& Debugger crap for BeamGameCode#5
                    // p = CoreData.GetPlace(msg.xIdx, msg.zIdx);
                    logger.Warn($"OnPlaceClaimCmd(#{seqNum})) failed. Place already claimed.");
                }

            }
            else
            {
                // TODO: Haaack!!!! A claim of a place not on the map means "Blow it up!"
                // Fix this both here and in the BaseBike code.
                if (b != null) // might already be gone
                {
                    logger.Info($"OnPlaceClaimCmd(#{seqNum}) - OFF MAP! Boom! Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike: {SID(b?.bikeId)}");
                    _ApplyScoreUpdate(msg.TimeStamp, new Dictionary<string,int>(){ {b.bikeId, -b.score} });
                }

            }
        }

        public void OnPlaceHitCmd(PlaceHitMsg msg, long seqNum)
        {
            // Apian has already checked the the place is claimed and the bike exists
            Vector2 pos = BeamPlace.PlacePos(msg.xIdx, msg.zIdx);
            BeamPlace p = CoreState.GetPlace(pos);
            BaseBike hittingBike = CoreState.GetBaseBike(msg.bikeId);
            if (p != null && hittingBike != null)
            {
                hittingBike.UpdatePosFromCommand(msg.TimeStamp, FrameApianTime, p.GetPos(), msg.exitHead);
                logger.Info($"OnPlaceHitCmd( #{seqNum}, {p?.GetPos().ToString()}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike: {SID(hittingBike?.bikeId)} Pos: {p?.GetPos().ToString()}");
                _ApplyScoreUpdate(msg.TimeStamp, msg.scoreUpdates);
                PlaceHitEvt?.Invoke(this, new PlaceHitArgs(p, hittingBike));
            }
            else
            {
                logger.Info($"OnPlaceHitCmd(#{seqNum}, {p?.GetPos().ToString()}) Now: {FrameApianTime} Ts: {msg.TimeStamp} Bike: {SID(hittingBike?.bikeId)} Pos: {p?.GetPos().ToString()}");
            }
        }

        public void OnRemoveBikeCmd(RemoveBikeMsg msg, long seqNum)
        {
            logger.Info($"OnRemoveBikeCmd(#{seqNum}, {SID(msg.bikeId)}) Now: {FrameApianTime} Ts: {msg.TimeStamp}");
            IBike ib = CoreState.GetBaseBike(msg.bikeId);
            if (ib != null)
            {
                _RemoveBike(ib, true);
            } else {
                logger.Warn($"OnRemoveBikeCmd() {SID(msg.bikeId)} does not exist.");
            }

        }

        public void OnPlaceRemovedCmd(PlaceRemovedMsg msg, long seqNum)
        {
            BeamPlace p = CoreState.GetPlace(msg.xIdx, msg.zIdx);
            logger.Verbose($"OnPlaceRemovedCmd(#{seqNum}, {msg.xIdx},{msg.zIdx}) {(p==null?"MISSING":"")} Now: {FrameApianTime} Ts: {msg.TimeStamp}");
            CoreState.PostPlaceRemoval(p);
        }

        protected void _ApplyScoreUpdate(long causeApianTime, Dictionary<string,int> update)
        {
            foreach( string id in update.Keys)
            {
                IBike bike =  CoreState.GetBaseBike(id);
                bike?.AddScore(update[id]);
                if (bike.score <= 0)
                {
                    logger.Info($"ApplyScoreUpdate(). Bike: {SID(bike.bikeId)} has no score anymore!");
                    apian.SendRemoveBikeObs(causeApianTime+1, bike.bikeId); // Bike removal is one 'tick' after whatever caused it.
                }
            }
        }


        //
        // IBeamBackend (requests from the frontend)
        //

        public void RaiseReadyToPlay() => ReadyToPlayEvt?.Invoke(this, EventArgs.Empty); // GameCode -> FE
        public void RaiseRespawnPlayer() => RespawnPlayerEvt?.Invoke(this, EventArgs.Empty); // FE -> GameCode
        public Ground GetGround() => CoreState.Ground;


        protected Dictionary<string,int> _ComputeScoreUpdate(IBike bike, ScoreEvent evt, BeamPlace place)
        {
            // BIG NOTE: total score is NOT conserved.
            Dictionary<string,int> update = new Dictionary<string,int>();

            int scoreDelta = GameConstants.eventScores[(int)evt];
            update[bike.bikeId] = scoreDelta;

            logger.Debug($"ComputeScoreUpdate(). Bike: {SID(bike.bikeId)} Event: {evt}");
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
                    CoreState.Bikes.Values.Where( b => b != bike && b.team == place.bike.team);  // Bikes other the "bike" on affected team
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


        //  informational
        public void OnUnknownBike(string bikeId, string srcId)
        {
            UnknownBikeEvt?.Invoke(this, bikeId);
        }

        // Peer-related
        protected bool _AddPlayer(BeamPlayer p)
        {
            logger.Debug($"_AddPlayer(). Name: {p.Name} ID: {SID(p.PeerId)}");
            if  ( CoreState.Players.ContainsKey(p.PeerId))
            {
                logger.Warn($"_AddPlayer(). Player already exists!!!!");
                return false;
            }

            CoreState.Players[p.PeerId] = p;
            if (p.PeerId == LocalPeerId )
                LocalPlayer = p;
            PlayerJoinedEvt.Invoke(this, new PlayerJoinedArgs(ApianGroupId, p));
            return true;
        }

        protected bool _RemovePlayer(string p2pId)
        {
            if  (!CoreState.Players.ContainsKey(p2pId))
                return false;

            PlayerLeftEvt?.Invoke(this, new PlayerLeftArgs(ApianGroupId, p2pId));

            foreach (IBike ib in CoreState.LocalBikes(p2pId))
                _RemoveBike(ib, true); // Blow em up just for yuks.

            CoreState.PostPlayerRemoval(p2pId);
            return true;
        }

        public void ClearPlayers()
        {
            PlayersClearedEvt?.Invoke(this, EventArgs.Empty);
            CoreState.Players.Clear();
        }

        // Bike-related

        public bool _AddBike(IBike ib)
        {
            logger.Verbose($"_AddBike(): {SID(ib.bikeId)} at ({ib.basePosition.x}, {ib.basePosition.y})");

            if (CoreState.GetBaseBike(ib.bikeId) != null)
                return false;

            CoreState.Bikes[ib.bikeId] = ib;

            NewBikeEvt?.Invoke(this, ib);

            return true;
        }

        protected void _RemoveBike(IBike ib, bool shouldBlowUp=true)
        {
            logger.Info($"_RemoveBike(): {SID(ib.bikeId)}");
            CoreState.RemovePlacesForBike(ib);
            BikeRemovedEvt?.Invoke(this, new BikeRemovedData(ib.bikeId,  shouldBlowUp));
            CoreState.PostBikeRemoval(ib.bikeId); // we're almost certainly iterating over the list of bikes so don;t remove it yet.
        }
        public void ClearBikes()
        {
            BikesClearedEvt?.Invoke(this, EventArgs.Empty);
            CoreState.Bikes.Clear();
        }

       // Ground-related

        public void OnPlaceClaimObsEvt(object sender, PlaceReportArgs args)
        {
            logger.Verbose($"OnPlaceClaimObsEvt(): Bike: {SID(args.bike.bikeId)} Place: {BeamPlace.PlacePos(args.xIdx, args.zIdx).ToString()}");

            apian.SendPlaceClaimObs(FrameApianTime, args.bike, args.xIdx, args.zIdx, args.entryHead, args.exitHead,
                _ComputeScoreUpdate(args.bike, ScoreEvent.kClaimPlace, null));
          }

        public void OnPlaceHitObsEvt(object sender, PlaceReportArgs args)
        {
            logger.Verbose($"OnPlaceHitObsEvt(): Bike: {SID(args.bike.bikeId)} Place: {BeamPlace.PlacePos(args.xIdx, args.zIdx).ToString()}");
            BeamPlace place = CoreState.GetPlace(args.xIdx, args.zIdx);
            if (place != null)
            {
                ScoreEvent evType = place.bike.team == args.bike.team ? ScoreEvent.kHitFriendPlace : ScoreEvent.kHitEnemyPlace;
                apian.SendPlaceHitObs(FrameApianTime, args.bike, args.xIdx, args.zIdx, args.entryHead, args.exitHead, _ComputeScoreUpdate(args.bike, evType, place));
            } else {
                logger.Warn($"OnPlaceHitObsEvt(): Bike: {SID(args.bike.bikeId)} No place found at: {BeamPlace.PlacePos(args.xIdx, args.zIdx).ToString()}");
            }

        }

        public void OnPlaceTimeoutEvt(object sender, BeamPlace p)
        {
            logger.Verbose($"OnPlaceTimeoutEvt(): {p.GetPos().ToString()}");
            apian.SendPlaceRemovedObs(p.expirationTimeMs, p.xIdx, p.zIdx);
        }

        public void ClearPlaces()
        {
            // TODO: Clean this up. This method probably shouldn;t even be here.
            CoreState.ClearPlaces(); // notifies FE.
        }

    }

}