using System;
using System.Collections.Generic;
using GameModeMgr;
using UniLog;

namespace BeamGameCode
{
    public class BeamGameMode : IGameMode
    {
		public ModeManager manager;
		public BeamApplication appl;
        public BeamAppCore appCore = null;
		public UniLogger logger;
		public int ModeId() => manager.CurrentModeId();

		public void Setup(ModeManager mgr, IModalGame gInst = null)
		{
			// Called by manager before Start()
			// Not virtual
			// TODO: this should be the engine and not the modeMgr - but what IS an engine...
			manager = mgr;
			appl = gInst as BeamApplication;
			logger = UniLogger.GetLogger("BeamMode");
        }

		public virtual void Start( object param = null)	{
            logger.Info($"Starting {(ModeName())}");
        }

		public virtual void Loop(float frameSecs) {}

		public virtual void Pause() {}

		public virtual void Resume( string prevModeName, object param = null)	{
            logger.Info($"Resuming {(ModeName())} from {prevModeName}");
        }

		public virtual object End() => null;
        public virtual string ModeName() => this.GetType().Name;

		// Utils

        protected void _CreateCorePair(BeamGameInfo gameInfo)
        {
            // Create gameinstance and ApianInstance
            appCore = new BeamAppCore();
            BeamApian apian = BeamApianFactory.Create(gameInfo.GroupInfo.GroupType, appl.beamGameNet, appCore);
            appl.AddAppCore(appCore);
        }

    }
}