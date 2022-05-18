//#define SINGLE_THREADED
using System;
using System.Collections.Generic;
using System.Linq;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{

    public class ModePlay : BeamGameMode
    {

        // Here's how I want this to work:

        // Mode begins with no AppCore, no Apian, not connection, nothing...
        // It has: net connection string and the name of the desired ApianNetwork to join.

        // - Connect to Gamenet
        // - Join the ApianNet. Wait for
        //     -> OnPeerJoinedNetwork()
        // - Get a list of available games
        // - Ask FE for a game to create/join. Wait for:
        //     -> OnGameSelected()
        // - Create/Join game. Wait for:
        //     -> OnPlayerJoinedEvt()
        // - Start playing  (could wait for others to join...)


        protected string networkName;
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
        protected const int kJoiningNet = 2;
        protected const int kCheckingForGames = 3;
        protected const int kSelectingGame = 4;
        protected const int kJoiningExistingGame = 5;
        protected const int kCreatingAndJoiningGame = 6;
        protected const int kWaitingForMembers = 7;
#endif
        protected const int kPlaying = 8;
        protected const int kFailed = 9;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kListenForGamesSecs = 2.0f; // TODO: belongs here?

#if SINGLE_THREADED
        protected Dictionary<string, BeamGameAnnounceData> announcedGames;
#endif


		public override void Start(object param = null)
        {
            base.Start();
            settings = appl.frontend.GetUserSettings();
            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt;
            appl.AddAppCore(null);
            _SetState(kStartingUp); // was "connecting"
            appl.frontend?.OnStartMode(this);
        }

		public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

		public override object End() {
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
            if (appCore != null)
            {
                appCore.PlayerJoinedEvt -= _OnPlayerJoinedEvt;
                appCore.NewBikeEvt -= _OnNewBikeEvt;
                appl.frontend?.OnEndMode(this);
                appCore.End();
                appl.beamGameNet.LeaveNetwork();
            }
            appl.AddAppCore(null);
            return null;
        }

        // Loopfuncs

        private void _SetState(int newState, object startParam = null)
        {
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
                try {
                    appl.ConnectToNetwork(settings.p2pConnectionString);
                } catch (Exception ex) {
                    _SetState(kFailed, ex.Message);
                    return;
                }
                _SetState(kJoiningNet);
                break;
            case kJoiningNet:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningNet");
                _JoinNetwork();
                break;
            case kCheckingForGames:
                logger.Verbose($"{(ModeName())}: SetState: kCheckingForGames");
                _CheckForGames();
                _loopFunc = _CheckingForGamesLoop;
                break;
            case kSelectingGame:
                logger.Verbose($"{(ModeName())}: SetState: kSelectingGame");  // waiting for UI to return
                _SelectGame();
                break;
            case kJoiningExistingGame:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningExistingGame");
                _JoinExistingGame(startParam as BeamGameInfo);
                break;
            case kCreatingAndJoiningGame:
                logger.Verbose($"{(ModeName())}: SetState: kCreatingAndJoiningGame");
                _CreateAndJoinGame(startParam as BeamGameInfo);
                break;
            case kWaitingForMembers:
                logger.Verbose($"{(ModeName())}: SetState: kWaitingForMembers");
                break;
#endif
            case kPlaying:
                logger.Verbose($"{(ModeName())}: SetState: kPlaying");
                SpawnPlayerBike();
                for (int i=0; i<settings.aiBikeCount; i++)
                    SpawnAiBike();
                _loopFunc = _PlayLoop;
                break;
            case kFailed:
                logger.Warn($"{(ModeName())}: SetState: kFailed  Reason: {(string)startParam}");
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
                    if (appCore.CoreState.LocalBikes(appCore.LocalPeerId).Where(ib => ib.ctrlType==BikeFactory.AiCtrl).Count() < settings.aiBikeCount)
                        SpawnAiBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

        private void _FailedLoop(float frameSecs)
        {
            //if (_curStateSecs > 5)
        }

         // utils

        private void _SetupCorePair(BeamGameInfo gameInfo)
        {
            if (gameInfo == null)
                throw new ArgumentException($"_SetupCorePair(): null gameInfo");

            CreateCorePair(gameInfo);
            appCore.PlayerJoinedEvt += _OnPlayerJoinedEvt;  // Wait for AppCore to report local player has joined
            appCore.NewBikeEvt += _OnNewBikeEvt;
        }

        // Event handlers

        private void _OnPeerJoinedNetEvt(object sender, PeerJoinedEventArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - _OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {SID(p.PeerId)}");

#if SINGLE_THREADED
           if (isLocal)
            {
                if (_curState == kJoiningNet)
                {
                    // This used to create the AppCore and apian instance. Now we wait until after a game is selected
                    // so what we create can depend on what was selected
                    _SetState(kCheckingForGames, null);
                }
                else
                    logger.Error($"{(ModeName())} - OnNetJoinedEvt() - Wrong state: {_curState}");
            }
#endif
        }



        private void _OnPlayerJoinedEvt(object sender, PlayerJoinedEventArgs ga)
        {
            bool isLocal = ga.player.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPlayerJoinedEvt() - {(isLocal?"Local":"Remote")} Member Joined: {ga.player.Name}, ID: {SID(ga.player.PeerId)}");
            if (ga.player.PeerId == appl.LocalPeer.PeerId)
            {
                appCore.RespawnPlayerEvt += _OnRespawnPlayerEvt;  // FIXME: why does this happen here?  &&&&
                //_SetState(kWaitingForMembers);
                _SetState(kPlaying);
            }
        }

        private void _OnNewBikeEvt(object sender, BikeEventArgs newBikeArgs)
        {
            IBike newBike = newBikeArgs?.ib;
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.peerId == appl.LocalPeer.PeerId;
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
            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, appCore.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ SID(bb.bikeId)})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnPlayerBike()
        {
            if (settings.localPlayerCtrlType != "none")
            {
                BaseBike bb =  appl.CreateBaseBike( settings.localPlayerCtrlType, appCore.LocalPeerId, appCore.LocalPlayer.Name, BikeDemoData.RandomTeam());
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
                appl.SetupNetwork(settings.p2pConnectionString); // should be async? GameNet.Connect() currently is not
                GameNet.PeerJoinedNetworkData netJoinData = await appl.JoinBeamNetAsync(settings.apianNetworkName);

                Dictionary<string, BeamGameAnnounceData> gamesAvail = await appl.GetExistingGamesAsync((int)(kListenForGamesSecs*1000));
                GameSelectedEventArgs selection = await appl.SelectGameAsync(gamesAvail);

                // OnGameSelected( selection )
                if (selection.result == GameSelectedEventArgs.ReturnCode.kCancel)
                    throw new ArgumentException($"_AsyncStartup() No Game Selected.");

                BeamGameInfo gameInfo = selection.gameInfo;

                _SetupCorePair(gameInfo);

                bool targetGameExisted = (gameInfo.GameName != null) && gamesAvail.ContainsKey(gameInfo.GameName);
                LocalPeerJoinedGameData gameJoinData = null;

                if (selection.result == GameSelectedEventArgs.ReturnCode.kCreate)
                {
                    // Create and join
                    if (targetGameExisted)
                        throw new ArgumentException($"Cannot create.  Beam Game \"{gameInfo.GameName}\" already exists");
                    gameJoinData = await appl.CreateAndJoinGameAsync(gameInfo, appCore);

                } else {
                    // Join existing
                    if (!targetGameExisted)
                        throw new ArgumentException($"Cannot Join.  Beam Game \"{gameInfo.GameName}\" not found");
                    gameJoinData = await appl.JoinExistingGameAsync(gameInfo, appCore);
                }

                // Now we are waiting for the AppCore to report that the local player has joined the CoreGame
                // AppCore.PlayerJoinedEvt

            } catch (Exception ex) {
                _SetState(kFailed, ex.Message);
                return;
            }
        }
#else
        private void _JoinNetwork()
        {
            appl.JoinBeamNet(settings.apianNetworkName);
            // Wait for OnNetJoinedEvt()
        }

        private void _CheckForGames()
        {
            announcedGames = new Dictionary<string, BeamGameAnnounceData>();
            appl.GameAnnounceEvt += OnGameAnnounceEvt;
            appl.ListenForGames();
        }

        protected void _CheckingForGamesLoop(float frameSecs)
        {
            if (_curStateSecs > kListenForGamesSecs)
            {
                // Stop listening for games and ask the FE to choose one
                appl.GameAnnounceEvt -= OnGameAnnounceEvt; // stop listening
                _SetState(kSelectingGame); // ends with OnGameSelected()
            }
        }

        public void OnGameAnnounceEvt(object sender, GameAnnounceEventArgs gaArgs)
        {
            BeamGameAnnounceData gameData = gaArgs.gameData;
            logger.Verbose($"{(ModeName())} - OnGameAnnounceEvt(): {gameData.GameInfo.GameName}");
            announcedGames[gameData.GameInfo.GameName] = gameData;
        }

        protected void _SelectGame()
        {
            appl.GameSelectedEvent += OnGameSelectedEvt;
            appl.SelectGame(announcedGames);
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
                _SetState(kFailed,$"DispatchGameSelection() No Game Selected.");
                return;
            }

            BeamGameInfo gameInfo = selection.gameInfo;
            _SetupCorePair(gameInfo);

            bool targetGameExisted = (gameInfo.GameName != null) && announcedGames.ContainsKey(gameInfo.GameName);

            if (selection.result == GameSelectedEventArgs.ReturnCode.kCreate) // Createa and join
            {
                if (targetGameExisted)
                    _SetState(kFailed,$"Cannot create.  Beam Game \"{gameInfo.GameName}\" already exists");
                else
                    _SetState(kCreatingAndJoiningGame, gameInfo);

            } else {
                    // Join existing
                if (!targetGameExisted)
                    _SetState(kFailed,$"Cannot Join.  Beam Game \"{gameInfo.GameName}\" not found");
                else
                    _SetState(kJoiningExistingGame, gameInfo);
            }
        }

        private void _CreateAndJoinGame(BeamGameInfo info)
        {
            appl.CreateAndJoinGame(info, appCore); // now waiting for OnPlayerJoined for the local player
        }

        private void _JoinExistingGame(BeamGameInfo gameInfo)
        {
            appl.JoinExistingGame(gameInfo, appCore); // now waiting for OnPlayerJoined for the local player
        }

#endif

    }


}


