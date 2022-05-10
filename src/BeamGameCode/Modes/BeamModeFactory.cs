using System;
using System.Collections.Generic;
using ModalApplication;

namespace BeamGameCode
{
    public class BeamModeFactory : AppModeFactory
    {
        public const int kSplash = 0;
        public const int kNetwork = 1;
        public const int kPractice = 2;
        public const int kNetPlay = 3;

        public const string SplashModeName = "splash";
        public const string NetworkModeName = "network";
        public const string PracticeModeName = "practice";
        public const string NetPlayModeName = "net";



        public BeamModeFactory()
        {
            AppModeCtors =  new Dictionary<int, Func<IAppMode>>  {
                { kSplash, ()=> new ModeSplash() },
                { kNetwork, ()=> new ModeNetwork() },
                { kPractice, ()=> new ModePractice() },
                { kNetPlay, ()=> new ModeNetPlay() },
            };

            AppModeNames = new Dictionary<int, string> {
                { kSplash, SplashModeName },
                { kNetwork, NetworkModeName },
                { kPractice, PracticeModeName },
                { kNetPlay, NetPlayModeName },
            };
        }
    }
}