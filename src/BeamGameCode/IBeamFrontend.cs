using System;
using System.Collections.Generic;
using Apian;

#if !SINGLE_THREADED
using System.Threading.Tasks;
#endif

namespace BeamGameCode
{
    public enum MessageSeverity { Info, Warning, Error };

    public class TargetIdParams {public string targetId;}

    public interface IBeamFrontend
    {
        IBeamApplication beamAppl {get;}
        IBeamAppCore appCore {get;}

        void SetBeamApplication(IBeamApplication application);
        void SetAppCore(IBeamAppCore core); // The Beam frontend only currently supports a single AppCore
        BeamUserSettings GetUserSettings();

        void DisplayMessage(MessageSeverity level, string msgText);

        void SelectGame(IDictionary<string, BeamGameAnnounceData> existingGames);
#if !SINGLE_THREADED
        Task<GameSelectedEventArgs> SelectGameAsync(IDictionary<string, BeamGameAnnounceData> existingGames);
#endif
        // Event/message handlers

        // Game Modes
        // Start and end subscibe/unsubscribe
        void OnStartMode(BeamGameMode mode, object param = null);
        void OnEndMode(BeamGameMode mode, object param = null);

        // Players
        void OnPeerJoinedGameEvt(object sender, PeerJoinedEventArgs pa);
        void OnPeerLeftGameEvt(object sender, PeerLeftEventArgs pa);
        void OnPlayersClearedEvt(object sender, EventArgs e);
        // Bikes
        void OnNewBikeEvt(object sender, BikeEventArgs ib);
        void OnBikeRemovedEvt(object sender, BikeRemovedEventArgs data);
        void OnBikesClearedEvt(object sender, EventArgs e);
        void OnPlaceClaimedEvt(object sender, BeamPlaceEventArgs place);
        // Places
        void OnPlaceHitEvt(object sender, PlaceHitEventArgs args);
        // scoring
        // void OnScoreEvent(string bikeId, ScoreEvent evt, Ground.Place place); Need this?

        // Ground events
        void OnPlaceFreedEvt(object sender, BeamPlaceEventArgs p);
        void OnPlacesClearedEvt(object sender, EventArgs e);

        // Game Events
        void OnReadyToPlay(object sender, EventArgs e);


    }

}
