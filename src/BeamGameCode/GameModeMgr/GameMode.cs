
namespace GameModeMgr
{
	// ReSharper disable MemberCanBePrivate.Global,UnusedMember.Global,FieldCanBeMadeReadOnly.Global
	public interface IGameMode
	{
		void Setup(ModeManager mgr, IModalGame gameInst);
		void Start( object param = null);
		void Loop(float frameSecs);
		void Pause();
		void Resume(string prevModeName, object prevModeResult = null);
		object End();
		string ModeName();
	};

}

