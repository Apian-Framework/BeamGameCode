using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public enum CreateMode
    {
        JoinOnly = 0,
        CreateIfNeeded = 1,
        MustCreate = 2
    }

    public class ModeNetPlay : BeamGameMode
    {
        // Network is assumed to be up and running and stable
        // It's also assumed that a request has already been made for a list of available games
        // and at lest *some* wait has happened for responses.

        protected CreateMode gameCreateMode = CreateMode.CreateIfNeeded;
        public BeamUserSettings settings;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
        protected BaseBike playerBike;

        // mode substates
        protected const int kStartingUp = 0;
        protected const int kSettlingAfterJoin = 8;
        protected const int kPlaying = 9;
        protected const int kFailed = 10;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const int kJoinGameTimeoutMs = 10000; // 10 secs
        protected const int kJoinGameSettleMs = 2000; // <- after NewPlayer message shows wait this long before creating bike and stuff jsut in case we get "un-synced"

		public override void Start(object param = null)
        {
            base.Start();
            settings = appl.frontend.GetUserSettings();
            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt; // We have already joined
            appl.AddAppCore(null);
            appl.frontend?.OnStartMode(this);
            _SetState(kStartingUp); // was "connecting"

        }

		public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

        private void _UnsubscribeFromAppCoreEvents()
        {
           if (appCore != null)
            {
                appCore.PlayerJoinedEvt -= _OnPlayerJoinedEvt;
                appCore.NewBikeEvt -= _OnNewBikeEvt;
                appCore.RespawnPlayerEvt -= _OnRespawnPlayerEvt;
            }
        }
		public override object End()
        {
            _DoCleanup();
             appl.frontend?.OnEndMode(this);
             return null;
        }


        private void _DoCleanup()
        {
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
            _UnsubscribeFromAppCoreEvents();
            appCore?.End(); // see below: should happen in GameNet.LeaveGame() /ApianInst.ShutDown() or whatever
            appl.LeaveGame(); //TODO: THis should call GameNet.LeaveGame(), which should do ALL of this stuff
            appl.AddAppCore(null); // nulls FE.appCore, too
         }

        // Loopfuncs

        private void _SetState(int newState, object startParam = null)
        {
            int prevState = _curState;
            _curStateSecs = 0;
            _curState = newState;
            _loopFunc = _DoNothingLoop; // default
            switch (newState)
            {
            case kStartingUp:
                logger.Verbose($"{(ModeName())}: SetState: kStartingUp");
                _AsyncStartup();
                break;
            case kSettlingAfterJoin:
                logger.Verbose($"{(ModeName())}: SetState: kSettlingAfterJoin");
                _loopFunc = _SettleAfterJoinLoop;
                break;
            case kPlaying:
                logger.Verbose($"{(ModeName())}: SetState: kPlaying");
                SpawnPlayerBike();
                for (int i=0; i<settings.aiBikeCount; i++)
                    SpawnAiBike();
                _loopFunc = _PlayLoop;
                break;
            case kFailed:
                logger.Warn($"{(ModeName())}: SetState: kFailed. Prev State: {prevState} Reason: {(string)startParam}");
                appl.frontend.DisplayMessage(MessageSeverity.Error, (string)startParam);
                _DoCleanup();
                _loopFunc = _FailedLoop;
                break;
            default:
                logger.Error($"{(ModeName())}._SetState() - Unknown state: {newState}");
                break;
            }
        }

        private void _DoNothingLoop(float frameSecs) {}

        private void _PlayLoop(float frameSecs)
        {
            if (settings.regenerateAiBikes)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    if (appCore.CoreState.LocalBikes(appCore.LocalPlayerAddr).Where(ib => ib.ctrlType==BikeFactory.AiCtrl).Count() < settings.aiBikeCount)
                        SpawnAiBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

       private void _SettleAfterJoinLoop(float frameSecs)
        {
            // wait a little while before requesting new bika and all in case
            // there's in/out of sync jiggling
            if (_curStateSecs * 1000f > kJoinGameSettleMs)
            {
                _SetState(kPlaying);
            }
       }

        private void _FailedLoop(float frameSecs)
        {

        }

         // utils

        private void _SetupCorePair(BeamGameInfo gameInfo)
        {
            if (gameInfo == null)
                throw new ArgumentException($"_SetupCorePair(): null gameInfo");

            CreateCorePair(gameInfo);
            appCore.PlayerJoinedEvt += _OnPlayerJoinedEvt;  // Wait for AppCore to report local player has joined
            appCore.NewBikeEvt += _OnNewBikeEvt;
            appCore.RespawnPlayerEvt += _OnRespawnPlayerEvt;
        }

        // Event handlers

        private void _OnPeerJoinedNetEvt(object sender, PeerJoinedEventArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerAddr == appl.LocalPeer.PeerAddr;
            logger.Info($"{(ModeName())} - _OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {SID(p.PeerAddr)}");
        }


        private void _OnPlayerJoinedEvt(object sender, PlayerJoinedEventArgs ga)
        {
            bool isLocal = ga.player.PlayerAddr == appl.LocalPeer.PeerAddr;
            logger.Info($"{(ModeName())} - OnPlayerJoinedEvt() - {(isLocal?"Local":"Remote")} Member Joined: {ga.player.Name}, ID: {SID(ga.player.PlayerAddr)}");
            if (ga.player.PlayerAddr == appl.LocalPeer.PeerAddr)
            {
                //_SetState(kPlaying);
                _SetState(kSettlingAfterJoin); // wait a little while before starting to play
            }
        }

        private void _OnNewBikeEvt(object sender, BikeEventArgs newBikeArgs)
        {
            IBike newBike = newBikeArgs?.ib;
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.playerAddr == appl.LocalPeer.PeerAddr;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {SID(newBike.bikeId)}");
            if (isLocal)
            {
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

        private void _OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt
        }

        // Gameplay control

        protected string SpawnAiBike()
        {
            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, appCore.LocalPlayerAddr, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ SID(bb.bikeId)})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnPlayerBike()
        {
            if (settings.localPlayerCtrlType != "none")
            {
                BaseBike bb =  appl.CreateBaseBike( settings.localPlayerCtrlType, appCore.LocalPlayerAddr, appCore.LocalPlayer.Name, BikeDemoData.RandomTeam());
                appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb);
                logger.Debug($"{this.ModeName()}: SpawnPlayerBike({SID(bb.bikeId)})");
                return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
            }
            return null;
        }

        // MultiThreaded code
        private async void _AsyncStartup()
        {
            //try {

                Dictionary<string, BeamGameAnnounceData> gamesAvail = appl.NetInfo.BeamGames.Values.ToDictionary(bgd => bgd.GameInfo.GameName, bgd => bgd);

                logger.Info($"{this.ModeName()}: _AsyncStartup() - # gamesAvail={gamesAvail.Values.Count}");

                GameSelectedEventArgs selection = await appl.SelectGameAsync(gamesAvail);

                // OnGameSelected( selection )
                if (selection.result == GameSelectedEventArgs.ReturnCode.kCancel)
                {
                    //throw new ArgumentException($"_AsyncStartup() No Game Selected.");
                    appl.OnPopModeReq(null);
                    return;
                }


                BeamGameInfo gameInfo = selection.gameInfo;

                _SetupCorePair(gameInfo);

                bool targetGameExisted = (gameInfo.GameName != null) && gamesAvail.ContainsKey(gameInfo.GameName);
                LocalPeerJoinedGameData gameJoinData = null;

                if (selection.result == GameSelectedEventArgs.ReturnCode.kCreate)
                {
                    // Create and join
                    if (targetGameExisted)
                        throw new ArgumentException($"Cannot create.  Beam Game \"{gameInfo.GameName}\" already exists");

                    gameJoinData = await appl.CreateAndJoinGameAsync(gameInfo, appCore, kJoinGameTimeoutMs, selection.joinAsValidator);

                } else {
                    // Join existing
                    if (!targetGameExisted)
                        throw new ArgumentException($"Cannot Join.  Beam Game \"{gameInfo.GameName}\" not found");

                    gameJoinData = await appl.JoinExistingGameAsync(gameInfo, appCore, kJoinGameTimeoutMs, selection.joinAsValidator);
                }


                if (!gameJoinData.success)
                    throw new Exception($"Failed to join game: {gameJoinData.failureReason}");

                // Now we are waiting for the AppCore to report that the local player has joined the CoreGame
                // AppCore.PlayerJoinedEvt

           // } catch (Exception ex) {
           //     _SetState(kFailed, ex.Message);
           //     return;
           // }
        }
    }
}


