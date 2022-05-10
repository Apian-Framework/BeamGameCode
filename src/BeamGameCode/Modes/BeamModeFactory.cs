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

        public BeamModeFactory()
        {
            AppModeCtors =  new Dictionary<int, Func<IAppMode>>  {
                { kSplash, ()=> new ModeSplash() },
                { kNetwork, ()=> new ModeNetwork() },
                { kPractice, ()=> new ModePractice() },
                { kNetPlay, ()=> new ModeNetPlay() },
            };
        }
    }
}