using System;
using System.Collections.Generic;
using GameModeMgr;

namespace BeamGameCode
{
    public class BeamModeFactory : ModeFactory
    {
        public const int kSplash = 0;
        public static int kConnect = 1;
        public const int kPlay = 2;
        public const int kPractice = 3;

        public BeamModeFactory()
        {
            ModeFactories =  new Dictionary<int, Func<IGameMode>>  {
                { kConnect, ()=> new ModeConnect() },
                { kSplash, ()=> new ModeSplash() },
                { kPlay, ()=> new ModePlay() },
                { kPractice, ()=> new ModePractice() },
            };
        }
    }
}