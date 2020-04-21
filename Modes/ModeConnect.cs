using System.Linq;
using System;
using UnityEngine;

namespace BeamBackend
{
    public class ModeConnect : BeamGameMode
    {
        // States:
        // creatingGame
        //      start: issue createGame
        //      on GameCreatedEvt: joiningGame
        //
        // joiningGame
        //      start: issue joinGame
        //      on PeerJoinedGameEvt(local peer): waitingForRemoteBikes
        //
        // waitingForUnknownBikes
        //      start: reset timer
        //      loop: wait for kWaitForBikesSecs to go by without an UnknownBikeEvt event
        //          (the whole while we are issuing BikeCreaetData requests when we see a bike we don't know)
        //          if times out: creatingBikes
        //      on UnknownBikeEvt: reset wait timer
        //
        // creatingBikes:
        //      start:  issue create reqs for any AI bikes
        //              issue create for localPlayer bike
        //      loop: when _localBikesToCreate == 0: readyToPlay
        //      on NewBikeCreatedEvt: if local, _localBikesToCreate--
        //
        //  ready to play: go to play mode

        protected readonly float kWaitForBikesSecs = 1.75f * Ground.gridSize / BaseBike.defaultSpeed; // 1.75 grid time's worth
        protected const int kCreatingGame = 0;
        protected const int kJoiningGame = 1;
        protected const int kWaitingForUnknownBikes = 2; // wait for a clear couple seconds before creating local bike(s)
        protected const int kCreatingBikes = 3;
        protected const int kReadyToPlay = 4;

        public BeamGameInstance game = null;
        public BeamUserSettings settings = null;
        protected int _curState = kCreatingGame;

        protected float _curStateSecs = 0;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
        protected int _localBikesToCreate = 0;

		public override void Start(object param = null)
        {
            base.Start();

            game = core.mainGameInst;

            game.GameCreatedEvt += OnGameCreatedEvt;
            game.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
            game.PeerLeftGameEvt += OnPeerLeftGameEvt;
            game.UnknownBikeEvt += OnUnknownBikeEvt;
            game.NewBikeEvt += OnNewBikeEvt;

            settings = game.frontend.GetUserSettings();

            game.ClearPeers();
            game.ClearBikes();
            game.ClearPlaces();

            // need to "connect"first in order to have a p2pId
            core.gameNet.Connect(settings.p2pConnectionString);
            string p2pId = core.gameNet.LocalP2pId();
            BeamPeer localPeer = _CreateLocalPeer(p2pId, settings);
            game.AddLocalPeer(localPeer);

            if (!settings.tempSettings.ContainsKey("gameId"))
                _SetState(kCreatingGame, new BeamGameNet.GameCreationData());
            else
                _SetState(kJoiningGame, settings.tempSettings["gameId"]);

            game.frontend?.OnStartMode(ModeId(), null );
        }

        public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

		public override object End() {
            game.GameCreatedEvt -= OnGameCreatedEvt;
            game.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.PeerLeftGameEvt -= OnPeerLeftGameEvt;
            game.NewBikeEvt -= OnNewBikeEvt;
            game.UnknownBikeEvt -= OnUnknownBikeEvt;
            game.frontend?.OnEndMode(ModeId());
            return null;
        }

        protected void _SetState(int newState, object startParam = null)
        {
            _curStateSecs = 0;
            _curState = newState;
            _loopFunc = _DoNothingLoop; // default
            switch (newState)
            {
            case kCreatingGame:
                logger.Info($"{(ModeName())}: SetState: kCreatingGame");
                core.gameNet.CreateGame(startParam);
                break;
            case kJoiningGame:
                logger.Info($"{(ModeName())}: SetState: kJoiningGame");
                core.gameNet.JoinGame((string)startParam);
                break;
            case kWaitingForUnknownBikes:
                logger.Info($"{(ModeName())}: SetState: kWaitingForRemoteBikes");
                _loopFunc = _WaitForRemoteBikesLoop;
                break;
            case kCreatingBikes:
                logger.Info($"{(ModeName())}: SetState: kCreatingBike");
                _CreateLocalBike(settings.localPlayerCtrlType);
                for (int i=0; i<settings.aiBikeCount; i++)
                    _CreateADemoBike();
                _loopFunc = _WaitForLocalBikesLoop;
                break;
            case kReadyToPlay:
                logger.Info($"{(ModeName())}: SetState: kReadyToPlay");
                game.RaiseReadyToPlay();
                break;
            default:
                logger.Error($"ModeConnect._SetState() - Unknown state: {newState}");
                break;
            }
        }

        protected void _DoNothingLoop(float frameSecs) {}

        protected void _WaitForRemoteBikesLoop(float frameSecs)
        {
            // remote bike creations will keep resetting the timer
            if (_curStateSecs > kWaitForBikesSecs)
                _SetState(kCreatingBikes);
        }

        protected void _WaitForLocalBikesLoop(float frameSecs)
        {
            // remote bike creations will keep resetting the timer
            if (_localBikesToCreate == 0)
                _SetState(kReadyToPlay);
        }

		// Event handlers
        public void OnGameCreatedEvt(object sender, string newGameId)
        {
            logger.Info($"{(ModeName())} - OnGameCreatedEvt(): {newGameId}");
            if (_curState == kCreatingGame)
                _SetState(kJoiningGame, newGameId);
            else
                logger.Error($"{(ModeName())} - OnGameCreatedEvt() - Wrong state: {_curState}");
        }

        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            BeamPeer p = ga.peer;
            bool isLocal = p.PeerId == game.LocalPeerId;
            logger.Info($"{(ModeName())} - OnPeerJoinedGameEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {p.PeerId}");
            if (isLocal)
            {
                if (_curState == kJoiningGame)
                    _SetState(kWaitingForUnknownBikes, null);
                else
                    logger.Error($"{(ModeName())} - OnGameJoinedEvt() - Wrong state: {_curState}");
            }
        }

        public void OnPeerLeftGameEvt(object sender, PeerLeftGameArgs args)
        {
            logger.Info($"OnPeerLeftGameEvt - Peer Left Game: {args.p2pId}");
        }

        public void OnNewBikeEvt(object sender, IBike ib)
        {
            bool isLocal = ib.peerId == game.LocalPeerId;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - New {(isLocal?"Local":"Remote")} bike: {ib.bikeId}");
            if (_curState == kCreatingBikes && isLocal)
                _SetState(kReadyToPlay, null);
        }

        public void OnUnknownBikeEvt(object sender, string bikeId)
        {
            logger.Info($"{(ModeName())} - OnUnknownBikeEvt() bike: {bikeId}");
            if (_curState == kWaitingForUnknownBikes)
                _curStateSecs = 0; // reset and wait some more
        }

        //
        // utils
        //

        protected bool _RemoteBikeExists()
        {
            return game.gameData.Bikes.Values.Where( ib => ib.peerId != game.LocalPeerId).Count() > 0;
        }

        protected BeamPeer _CreateLocalPeer(string p2pId, BeamUserSettings settings)
        {
            // Game.LocalP2pId is not set yet
            return new BeamPeer(p2pId, settings.screenName);
        }

        protected void _CreateLocalBike(string bikeCtrlType)
        {
            if (bikeCtrlType == "none")
            {
                logger.Info($"No LOCAL PLAYER BIKE created.");
            } else {
                 _localBikesToCreate++;
                string scrName = game.frontend.GetUserSettings().screenName;
                string bikeId = string.Format("{0:X8}", (scrName + game.LocalPeerId).GetHashCode());
                BaseBike bb =  game.CreateBaseBike(bikeCtrlType, game.LocalPeerId, game.LocalPeer.Name, BikeDemoData.RandomTeam());
                game.PostBikeCreateData(bb); // will result in OnBikeInfo()
            }
        }

        protected string _CreateADemoBike()
        {
            _localBikesToCreate++;
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( game.gameData.Bikes.Values.ToList(), heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            IBike ib =  new BaseBike(game, bikeId, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam(),
                BikeFactory.AiCtrl, pos, heading, BaseBike.defaultSpeed);
            game.PostBikeCreateData(ib);
            logger.Debug($"{this.ModeName()}: CreateADemoBike({bikeId})");
            return ib.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

    }
}