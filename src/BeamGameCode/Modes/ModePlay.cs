using System;
using System.Collections.Generic;
using System.Linq;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public enum CreateMode
    {
        JoinOnly = 0,
        CreateIfNeeded = 1,
        MustCreate = 2
    }

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

        protected Dictionary<string, BeamGameInfo> announcedGames;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
          protected BaseBike playerBike;

        // mode substates

        protected const int kStartingUp = 0;

        protected const int kPlaying = 8;
        protected const int kFailed = 9;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kListenForGamesSecs = 2.0f; // TODO: belongs here?

		public override void Start(object param = null)
        {
            base.Start();
            announcedGames = new Dictionary<string, BeamGameInfo>();

            settings = appl.frontend.GetUserSettings();

            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt;
            appl.AddAppCore(null);
            _SetState(kStartingUp); // was "connecting"
            appl.frontend?.OnStartMode(ModeId(), null );
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
                appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
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
            case kStartingUp:
                logger.Verbose($"{(ModeName())}: SetState: kStartingUp");

#if !SINGLE_THREADED
                _AsyncStartup();
#else
    #warning Need single threaded version!
#endif
                break;
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
            logger.Info($"{(ModeName())} - OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {SID(p.PeerId)}");
        }

        public void OnGameAnnounceEvt(object sender, BeamGameInfo gameInfo)
        {
            logger.Verbose($"{(ModeName())} - OnGameAnnounceEvt(): {gameInfo.GameName}");
            announcedGames[gameInfo.GameName] = gameInfo;
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
                appl.ConnectToNetwork(settings.p2pConnectionString); // should be async? GameNet.Connect() currently is not
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

#endif

    }
}


