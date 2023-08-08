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

        protected float kListenForGamesSecs = 5.0f;

        // mode substates
        protected const int kStartingUp = 0;
        protected const int kConnectedAndReady = 6;
        protected const int kFailed = 9;

		public override void Start(object param = null)
        {
            base.Start();
            settings = appl.frontend.GetUserSettings();
            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt;
            appl.PeerLeftEvt += _OnPeerLeftNetEvt;
            appl.GameAnnounceEvt += _OnGameAnnounceEvt;
            appl.JoinRejectedEvt += _OnJoinRejectedEvt;

            appl.AddAppCore(null); // reset

            appl.frontend?.OnStartMode(this);

            _loopFunc = _DoNothingLoop;

            _SetState(kStartingUp);
        }

        public override void Pause()
        {
            appl.frontend?.OnPauseMode(this);
        }

		public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

        public override void  Resume(string prevModeName, object param = null)
        {
            appl.frontend?.OnResumeMode(this);
            appl.ListenForGames();
        }

		public override object End() {
            appl.JoinRejectedEvt -= _OnJoinRejectedEvt;
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
            appl.PeerLeftEvt -= _OnPeerLeftNetEvt;
            appl.GameAnnounceEvt -= _OnGameAnnounceEvt;

            appl.DisconnectFromChain();
            appl.LeaveNetwork();
            appl.TearDownNetwork();

            appl.AddAppCore(null); // This is almost certainly unnecessary
            appl.frontend?.OnEndMode(this);
            return null;
        }

        // Loopfuncs

        private void _SetState(int newState, object startParam = null)
        {
            if (_curState == kFailed)
                return; // can't go anywhere from failed.

            _curStateSecs = 0;
            _curState = newState;
            _loopFunc = _DoNothingLoop; // default
            switch (newState)
            {
            case kStartingUp:
                logger.Verbose($"{(ModeName())}: SetState: kStartingUp");
                _AsyncStartup();
                break;
            case kConnectedAndReady:
               logger.Verbose($"{(ModeName())}: SetState: kConnectedAndReady");
                appl.OnNetworkReady(); // appl will tell the FE, which will do something
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
            bool isLocal = p.PeerAddr == appl.LocalPeer.PeerAddr;
            logger.Info($"{(ModeName())} - _OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {SID(p.PeerAddr)}");

        }
        private void _OnJoinRejectedEvt(object sender, JoinRejectedEventArgs rj)
        {
            logger.Info($"{(ModeName())} - _OnJoinRejectedEvt() - Reason: {rj.reason}");
            _SetState(kFailed, $"Failed to join network: {rj.reason}");
        }

        private void _OnPeerLeftNetEvt(object sender, PeerLeftEventArgs ga)
        {
            logger.Info($"{(ModeName())} - _OnPeerLeftNetEvt() - Peer {SID(ga.peerAddr)} left");
        }

        private void _OnGameAnnounceEvt(object sender, GameAnnounceEventArgs gaArgs)
        {
            BeamGameAnnounceData gameData = gaArgs.gameData;
            logger.Verbose($"{(ModeName())} - OnGameAnnounceEvt(): {gameData.GameInfo.GameName}");
        }

        // util code

        // MultiThreaded code
        private async void _AsyncStartup()
        {
            try {

                await appl.SetupCryptoAcctAsync(); // this takes a while if restoring a keystore

                appl.ConnectToChain(); // not async

                int  chainId = await appl.GetChainIdAsync(); // results in ChainIdEvt which frontend will react to (otherwise we'd use GetChainIdAsync() )
                appl.OnChainId(chainId,  null); // TODO: HHHAAACCKK!!!! &&&&

                string connectionStr =  settings.p2pConnectionSettings[settings.curP2pConnection];
                appl.SetupNetwork(connectionStr); // should be async? GameNet.Connect() currently is not
                GameNet.PeerJoinedNetworkData netJoinData = await appl.JoinBeamNetAsync(settings.apianNetworkName);

                logger.Info($"{this.ModeName()}: _AsyncStartup() - Waiting for game announcements.");
                await appl.GetExistingGamesAsync( (int)(kListenForGamesSecs * 1000f));

                _SetState(kConnectedAndReady);

            } catch (Exception ex) {
                _SetState(kFailed, ex.Message);
                return;
            }
        }
    }
}


