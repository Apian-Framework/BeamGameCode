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
#if SINGLE_THREADED
        protected const int kSelectingGame = 4;
        protected const int kJoiningExistingGame = 5;
        protected const int kCreatingAndJoiningGame = 6;
        protected const int kWaitingForMembers = 7;
#endif
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
		public override object End() => _DoCleanup();



        private object _DoCleanup()
        {
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
            _UnsubscribeFromAppCoreEvents();
            appl.frontend?.OnEndMode(this);
            appCore?.End();
            appl.LeaveGame();
            appl.AddAppCore(null); // nulls FE.appCore, too
            return null;
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
#if !SINGLE_THREADED
            case kStartingUp:
                logger.Verbose($"{(ModeName())}: SetState: kStartingUp");
                _AsyncStartup();
                break;
#else
            case kStartingUp:
                logger.Verbose($"{(ModeName())}: SetState: kStartingUp");
                _SetState(kSelectingGame);
                break;
            case kSelectingGame:
                logger.Verbose($"{(ModeName())}: SetState: kSelectingGame");  // waiting for UI to return
                // BamAppl has a dict of available games keyed by groupId
                // just need to re-key by gamename
                _SelectGame(appl.NetInfo.BeamGames.Values.ToDictionary(bgd => bgd.GameInfo.GameName, bgd => bgd));
                break;
            case kJoiningExistingGame:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningExistingGame");
                _JoinExistingGame(startParam as GameSelectedEventArgs);
                _loopFunc = _JoinGameLoop;
                break;
            case kCreatingAndJoiningGame:
                logger.Verbose($"{(ModeName())}: SetState: kCreatingAndJoiningGame");
                _CreateAndJoinGame(startParam as GameSelectedEventArgs);
                _loopFunc = _JoinGameLoop;
                break;
            case kWaitingForMembers: // Not used
                logger.Verbose($"{(ModeName())}: SetState: kWaitingForMembers");
                break;
#endif
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

       private void _JoinGameLoop(float frameSecs)
        {
            if (_curStateSecs * 1000f > kJoinGameTimeoutMs )
            {
                appl.LeaveGame(); // leaves/closes group
                _SetState(kFailed, "Join Game failed: Timeout");
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

#if SINGLE_THREADED
           if (isLocal) // We have already joined
               logger.Warn($"{(ModeName())} - OnNetJoinedEvt() - Local peer {SID(p.PeerAddr)} has already joind the net");
#endif
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

#if !SINGLE_THREADED
        // MultiThreaded code
        private async void _AsyncStartup()
        {
            try {

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

            } catch (Exception ex) {
                _SetState(kFailed, ex.Message);
                return;
            }
        }
#else


        protected void _SelectGame(Dictionary<string, BeamGameAnnounceData> gamesAvail)
        {
            appl.GameSelectedEvent += OnGameSelectedEvt;
            appl.SelectGame(gamesAvail);
        }

        public void OnGameSelectedEvt(object sender, GameSelectedEventArgs selection)
        {
            appl.GameSelectedEvent -= OnGameSelectedEvt; // stop listening
            logger.Info($"{(ModeName())} - OnGameSelectedEvt(): {selection.gameInfo?.GameName}, result: {selection.result}");
            DispatchGameSelection(selection);
        }

        private void DispatchGameSelection(GameSelectedEventArgs selection)
        {
            if (selection.result == GameSelectedEventArgs.ReturnCode.kCancel)
            {
                appl.OnPopModeReq(null);
                //_SetState(kFailed,$"DispatchGameSelection() No Game Selected.");
                return;
            }

            BeamGameInfo gameInfo = selection.gameInfo;

            logger.Verbose($"{(ModeName())} - DispatchGameSelection(): info: {(JsonConvert.SerializeObject(selection.gameInfo))}");

            _SetupCorePair(gameInfo);

            List<string> availGameNames = appl.NetInfo.BeamGames.Values.Select(bgd => bgd.GameInfo.GameName).ToList();

            bool targetGameExisted = (gameInfo.GameName != null) && availGameNames.Contains(gameInfo.GameName);

            if (selection.result == GameSelectedEventArgs.ReturnCode.kCreate) // Createa and join
            {
                if (targetGameExisted)
                    _SetState(kFailed,$"Cannot create.  Beam Game \"{gameInfo.GameName}\" already exists");
                else
                    _SetState(kCreatingAndJoiningGame, selection);

            } else {
                    // Join existing
                if (!targetGameExisted)
                    _SetState(kFailed,$"Cannot Join.  Beam Game \"{gameInfo.GameName}\" not found");
                else
                    _SetState(kJoiningExistingGame, selection);
            }
        }

        private void _CreateAndJoinGame(GameSelectedEventArgs selection)
        {

            appl.CreateAndJoinGame(selection.gameInfo, appCore, selection.joinAsValidator); // now waiting for OnPlayerJoined for the local player
        }

        private void _JoinExistingGame(GameSelectedEventArgs selection)
        {
            appl.JoinExistingGame(selection.gameInfo, appCore, selection.joinAsValidator); // now waiting for OnPlayerJoined for the local player
        }

#endif

    }
}


