using System;
using System.Collections.Generic;
using Apian;

namespace BeamGameCode
{
    public enum MessageSeverity { Info, Warning, Error };

    public class TargetIdParams {public string targetId;}

    public interface IFrontendModeHelper
    {
        void OnStartMode(int modeId, object param);
        void OnEndMode(int modeId, object param);
        void DispatchCmd(int modeId, int cmdId, object param);
    }

    public interface IBeamFrontend
    {
        IBeamApplication beamAppl {get;}
        IBeamAppCore appCore {get;}

        void SetBeamApplication(IBeamApplication application);
        void SetAppCore(IBeamAppCore core); // The Beam frontend only currently supports a single AppCore
        BeamUserSettings GetUserSettings();

        void DisplayMessage(MessageSeverity level, string msgText);

        void SelectGame(IDictionary<string, BeamGameInfo> existingGames);


        // Event/message handlers

        // Game Modes
        void OnStartMode(int modeId, object param = null);
        void OnEndMode(int modeId, object param = null);
        void DispatchModeCmd(int modeId, int cmdId, object param = null);

        // Players
        void OnPeerJoinedGameEvt(object sender, PeerJoinedArgs pa);
        void OnPeerLeftGameEvt(object sender, PeerLeftArgs pa);
        void OnPlayersClearedEvt(object sender, EventArgs e);
        // Bikes
        void OnNewBikeEvt(object sender, IBike ib);
        void OnBikeRemovedEvt(object sender, BikeRemovedData data);
        void OnBikesClearedEvt(object sender, EventArgs e);
        void OnPlaceClaimedEvt(object sender, BeamPlace place);
        // Places
        void OnPlaceHitEvt(object sender, PlaceHitArgs args);
        // scoring
        // void OnScoreEvent(string bikeId, ScoreEvent evt, Ground.Place place); Need this?

        // Ground events
        void OnPlaceFreedEvt(object sender, BeamPlace p);
        void OnSetupPlaceMarkerEvt(object sender, BeamPlace p);
        void OnPlacesClearedEvt(object sender, EventArgs e);

        // Game Events
        void OnReadyToPlay(object sender, EventArgs e);


    }

}
