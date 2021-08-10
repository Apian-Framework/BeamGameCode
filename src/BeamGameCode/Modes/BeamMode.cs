using System;
using ModalApplication;
using UniLog;

namespace BeamGameCode
{
    public class BeamGameMode : ILoopMode
    {
		public LoopModeManager manager;
		public BeamApplication appl;
        public BeamAppCore appCore;
		public UniLogger logger;
		public int ModeId() => manager.CurrentModeId();

		public void Setup(AppModeManager mgr, IModalApp gInst = null)
		{
			// Called by manager before Start()
			// Not virtual
			// TODO: this should be the engine and not the modeMgr - but what IS an engine...
			manager = mgr as LoopModeManager;
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

        protected void CreateCorePair(BeamGameInfo gameInfo)
        {
            // Create gameinstance and ApianInstance
            appCore = new BeamAppCore();
            BeamApian apian = BeamApianFactory.Create(gameInfo.GroupInfo.GroupType, appl.beamGameNet, appCore);
            appl.AddAppCore(appCore);
        }

    }
}