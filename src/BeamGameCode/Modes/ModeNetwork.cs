//#define SINGLE_THREADED
using System;
using System.Collections.Generic;
using System.Linq;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public class ModeNetwork : BeamGameMode   {

        // Here's how I want this to work:

        // Mode begins with no AppCore, no Apian, not connection, nothing...
        // It has: net connection string and the name of the desired ApianNetwork to join.

        // - Connect to Gamenet
        // - Join the ApianNet. Wait for
        //     -> OnPeerJoinedNetwork()
        // - Get a list of available games

        // wait for the frontend to emit either
        // JoinGameEvent or DisconnectNetworkEvent

        // All of this time the fe should display current net info

        public BeamUserSettings settings;

        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;

        // mode substates
        protected const int kStartingUp = 0;
#if SINGLE_THREADED
        protected const int kJoiningNet = 2;
        protected const int kCheckingForGames = 3;
#endif
        protected const int kConnectedAndReady = 4;
        protected const int kFailed = 9;
        protected Dictionary<string, BeamGameAnnounceData> announcedGames; // TODO:

		public override void Start(object param = null)
        {
            base.Start();
            settings = appl.frontend.GetUserSettings();
            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt;
            appl.PeerLeftEvt += _OnPeerLeftNetEvt;
            appl.GameAnnounceEvt += OnGameAnnounceEvt;

            appl.AddAppCore(null); // reset

            _SetState(kStartingUp);
            appl.frontend?.OnStartMode(this);
        }

		public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

		public override object End() {
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
            appl.PeerLeftEvt -= _OnPeerLeftNetEvt;
            appl.GameAnnounceEvt -= OnGameAnnounceEvt;
            appl.beamGameNet.LeaveNetwork();
            appl.AddAppCore(null); // This is almost certainly unnecessary
            appl.frontend?.OnEndMode(this);
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
#endif
            case kConnectedAndReady:
                _loopFunc = _ConnectedLoop;
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

        private void _ConnectedLoop(float frameSecs)
        {

        }

        private void _FailedLoop(float frameSecs)
        {
            //if (_curStateSecs > 5)
        }

        // Event handlers

        private void _OnPeerJoinedNetEvt(object sender, PeerJoinedEventArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - _OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {SID(p.PeerId)}");

        }

        private void _OnPeerLeftNetEvt(object sender, PeerLeftEventArgs ga)
        {
            logger.Info($"{(ModeName())} - _OnPeerLeftNetEvt() - Peer {SID(ga.p2pId)} left");
        }

        public void OnGameAnnounceEvt(object sender, GameAnnounceEventArgs gaArgs)
        {
            BeamGameAnnounceData gameData = gaArgs.gameData;
            logger.Verbose($"{(ModeName())} - OnGameAnnounceEvt(): {gameData.GameInfo.GameName}");
            announcedGames[gameData.GameInfo.GameName] = gameData;
        }



#if !SINGLE_THREADED
        // MultiThreaded code
        private async void _AsyncStartup()
        {
            try {
                appl.ConnectToNetwork(settings.p2pConnectionString); // should be async? GameNet.Connect() currently is not
                GameNet.PeerJoinedNetworkData netJoinData = await appl.JoinBeamNetAsync(settings.apianNetworkName);

                _SetState(kConnectedAndReady);

            } catch (Exception ex) {
                _SetState(kFailed, ex.Message);
                return;
            }
        }
#else
        // Single threaded (Unity WebGL, for instance)
        private void _JoinNetwork()
        {
            appl.JoinBeamNet(settings.apianNetworkName);
            // Wait for OnNetJoinedEvt()
        }

        // TODO: Even if multi-threaded use this (unless, of course, it all ends up available from ApianGamenet)
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

#endif

    }
}


