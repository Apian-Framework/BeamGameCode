using System;
using System.Collections.Generic;
using ModalApplication;

namespace BeamGameCode
{
    public class BeamModeFactory : AppModeFactory
    {
        public const int kSplash = 0;
        public const int kPlay = 1;
        public const int kPractice = 2;

        public BeamModeFactory()
        {
            AppModeCtors =  new Dictionary<int, Func<IAppMode>>  {
                { kSplash, ()=> new ModeSplash() },
                { kPlay, ()=> new ModePlay() },
                { kPractice, ()=> new ModePractice() },
            };
        }
    }
}