
namespace ModalApplication
{

	public interface IAppMode
	{
		void Setup(AppModeManager mgr, IModalApp gameInst);
		void Start( object param = null);
		void Pause();
		void Resume(string prevModeName, object prevModeResult = null);
		object End();
		string ModeName();
	}

	public interface ILoopMode : IAppMode
	{
		void Loop(float frameSecs);
	};

}

