using System;
using Apian;

namespace BeamGameCode
{
    //
    // Event args
    //
    public struct PlayerJoinedArgs {
        public string groupChannel;
        public BeamPlayer player;
        public PlayerJoinedArgs(string g, BeamPlayer p) {groupChannel=g; player=p;}
    }
    public struct PlayerLeftArgs {
        public string groupChannel;
        public string p2pId;
        public PlayerLeftArgs(string g, string p) {groupChannel=g; p2pId=p;}
    }

    public struct BikeRemovedData {
        public string bikeId;
        public bool doExplode;
        public BikeRemovedData(string i, bool b) {bikeId=i; doExplode=b;}
    }

    public struct PlaceHitArgs
    {
        public BeamPlace p;
        public IBike ib;
        public PlaceHitArgs(BeamPlace _p, IBike _ib) { p=_p; ib=_ib; }
    }


    public interface IBeamAppCore : IApianAppCore
    {
        // API for application code

        // Events
        event EventHandler<string> GroupJoinedEvt;
        event EventHandler<BeamCoreState> NewCoreStateEvt;
        event EventHandler<PlayerJoinedArgs> PlayerJoinedEvt;
        event EventHandler<PlayerLeftArgs> PlayerLeftEvt;
        event EventHandler PlayersClearedEvt;
        event EventHandler<IBike> NewBikeEvt;
        event EventHandler<BikeRemovedData> BikeRemovedEvt;
        event EventHandler BikesClearedEvt;
        event EventHandler<BeamPlace> PlaceClaimedEvt;
        event EventHandler<PlaceHitArgs> PlaceHitEvt;
        event EventHandler<string> UnknownBikeEvt;

        // Instigated by game mode code
        event EventHandler ReadyToPlayEvt;
        event EventHandler RespawnPlayerEvt;
		void RaiseReadyToPlay();
		void RaiseRespawnPlayer();

        // Access
        Ground GetGround();

        string LocalPeerId {get;}
        BeamCoreState CoreData {get;}

        string ApianGameId {get;}
        string ApianGroupName {get;}

        long CurrentRunningGameTime {get;}

        // Requests from FE
        // TODO: This is getting sparse - is it needed?
        void PostBikeCommand(IBike bike, BikeCommand cmd);
        void PostBikeTurn(IBike bike, TurnDir dir);
        void PostBikeCreateData(IBike ib);

    }
}
