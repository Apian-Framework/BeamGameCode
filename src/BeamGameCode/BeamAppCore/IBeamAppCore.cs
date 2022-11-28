using System;
using Apian;

namespace BeamGameCode
{
    //
    // Event args
    //
    public class StringEventArgs : EventArgs
    {
        public string str;
        public StringEventArgs(string s) {str = s; }
    }


    public class PlayerJoinedEventArgs : EventArgs {
        public string groupChannel;
        public BeamPlayer player;
        public PlayerJoinedEventArgs(string g, BeamPlayer p) {groupChannel=g; player=p;}
    }
    public class PlayerLeftEventArgs : EventArgs {
        public string groupChannel;
        public string playerAddr;
        public PlayerLeftEventArgs(string g, string p) {groupChannel=g; playerAddr=p;}
    }

    public class BikeEventArgs : EventArgs {
        public IBike ib;
        public BikeEventArgs(IBike iBike) { ib = iBike; }
    }

    public class BikeRemovedEventArgs : EventArgs {
        public string bikeId;
        public bool doExplode;
        public BikeRemovedEventArgs(string i, bool b) {bikeId=i; doExplode=b;}
    }

    public class BeamPlaceEventArgs : EventArgs
    {
        public BeamPlace p;
        public BeamPlaceEventArgs(BeamPlace _p) { p=_p; }
    }

    public class PlaceHitEventArgs : EventArgs
    {
        public BeamPlace p;
        public IBike ib;
        public PlaceHitEventArgs(BeamPlace _p, IBike _ib) { p=_p; ib=_ib; }
    }


    public interface IBeamAppCore : IApianAppCore
    {
        // API for application code

        // Events
        event EventHandler<StringEventArgs> GroupJoinedEvt;
        event EventHandler<PlayerJoinedEventArgs> PlayerJoinedEvt;
        event EventHandler<PlayerLeftEventArgs> PlayerLeftEvt;
        event EventHandler<PlayerLeftEventArgs> PlayerMissingEvt; // not Gone... yet
        event EventHandler<PlayerLeftEventArgs> PlayerReturnedEvt;
        event EventHandler PlayersClearedEvt;
        event EventHandler<BikeEventArgs> NewBikeEvt;
        event EventHandler<BikeRemovedEventArgs> BikeRemovedEvt;
        event EventHandler BikesClearedEvt;
        event EventHandler<BeamPlaceEventArgs> PlaceClaimedEvt;
        event EventHandler<PlaceHitEventArgs> PlaceHitEvt;
        event EventHandler<StringEventArgs> UnknownBikeEvt;

        // Instigated by game mode code
        event EventHandler ReadyToPlayEvt;
        event EventHandler RespawnPlayerEvt;
		void RaiseReadyToPlay();
		void RaiseRespawnPlayer();

        // Access
        Ground GetGround();

        string LocalPlayerAddr {get;}
        BeamCoreState CoreState {get;}

        string ApianNetId {get;}
        string ApianGroupName {get;}
        string ApianGroupId {get;}
        long CurrentRunningGameTime {get;}



    }
}
