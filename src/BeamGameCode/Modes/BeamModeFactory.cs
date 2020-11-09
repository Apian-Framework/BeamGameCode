using System;
using System.Collections.Generic;
using GameModeMgr;

namespace BeamGameCode
{
    public class BeamModeFactory : ModeFactory
    {
        public const int kSplash = 0;
        public const int kPlay = 1;
        public const int kPractice = 2;

        public BeamModeFactory()
        {
            ModeFactories =  new Dictionary<int, Func<IGameMode>>  {
                { kSplash, ()=> new ModeSplash() },
                { kPlay, ()=> new ModePlay() },
                { kPractice, ()=> new ModePractice() },
            };
        }
    }
}